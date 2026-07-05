using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class CourtListenerSource(
    HttpClient httpClient,
    IWatchlistProvider watchlist,
    ILogger<CourtListenerSource> logger) : INewsSource
{
    private const string BaseUrl = "https://www.courtlistener.com/api/rest/v4/search/";
    private const int LookbackDays = 14;
    private static readonly TimeSpan DelayBetweenRequests = TimeSpan.FromMilliseconds(200);

    public string Name => SourceNames.CourtListener;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);
        if (watched.Count == 0) return results;

        var cutoff = DateTime.UtcNow.AddDays(-LookbackDays).ToString("yyyy-MM-dd");

        foreach (var ticker in watched)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var queries = BuildSearchTerms(ticker);
            foreach (var q in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var encoded = Uri.EscapeDataString(q);
                    var url = $"{BaseUrl}?type=r&q={encoded}&filed_after={cutoff}&order_by=dateFiled+desc&format=json";
                    var json = await httpClient.GetStringAsync(url, cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (!doc.RootElement.TryGetProperty("results", out var resultsEl)) continue;

                    foreach (var item in resultsEl.EnumerateArray())
                    {
                        var article = ParseDocket(item, ticker.Symbol);
                        if (article is not null)
                            results.Add(article);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "CourtListener fetch failed for {Ticker} query '{Query}'", ticker.Symbol, q);
                }

                await Task.Delay(DelayBetweenRequests, cancellationToken);
            }
        }

        return results;
    }

    private static readonly string[] CorporateSuffixes =
        ["Corp", "Corporation", "Inc", "Incorporated", "LLC", "Ltd", "Limited",
         "Holdings", "Holding", "Technologies", "Technology", "Platforms",
         "Industries", "Group", "plc", "NV", "AG", "SA"];

    private static IReadOnlyList<string> BuildSearchTerms(WatchedTicker ticker)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"\"{ticker.Name}\"" };
        foreach (var alias in ticker.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;
            if (IsAmbiguousForLegalSearch(alias)) continue;
            terms.Add($"\"{alias}\"");
        }
        return terms.ToList();
    }

    private static bool IsAmbiguousForLegalSearch(string alias)
    {
        if (alias.Length <= 5 && alias.All(c => char.IsLetter(c) && (char.IsUpper(c) || char.IsDigit(c))))
            return true;
        if (alias.Length < 8 && !CorporateSuffixes.Any(s => alias.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return true;
        return false;
    }

    private static IngestedArticle? ParseDocket(JsonElement item, string symbol)
    {
        var caseName = item.TryGetProperty("caseName", out var cn) ? cn.GetString() : null;
        if (string.IsNullOrWhiteSpace(caseName)) return null;

        var docketId = item.TryGetProperty("docket_id", out var did) ? did.GetInt64().ToString() : null;
        var docketUrl = item.TryGetProperty("docket_absolute_url", out var durl) ? durl.GetString() : null;
        var dateFiled = item.TryGetProperty("dateFiled", out var df) ? df.GetString() : null;
        var court = item.TryGetProperty("court", out var ct) ? ct.GetString() : null;
        var cause = item.TryGetProperty("cause", out var ca) ? ca.GetString() : null;
        var docketNumber = item.TryGetProperty("docketNumber", out var dn) ? dn.GetString() : null;

        if (string.IsNullOrWhiteSpace(docketId)) return null;

        if (!DateTime.TryParse(dateFiled, out var publishedAt))
            publishedAt = DateTime.UtcNow;
        publishedAt = DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
        if (publishedAt > DateTime.UtcNow)
            publishedAt = DateTime.UtcNow;

        var headline = $"{caseName} — {court ?? "Federal Court"}";
        var summary = BuildSummary(caseName, court, cause, docketNumber, dateFiled);
        var fullUrl = string.IsNullOrWhiteSpace(docketUrl)
            ? $"https://www.courtlistener.com/docket/{docketId}/"
            : $"https://www.courtlistener.com{docketUrl}";

        return new IngestedArticle(
            Source: SourceNames.CourtListener,
            SourceId: StableId(docketId),
            Symbol: symbol,
            Headline: headline,
            Summary: summary,
            Url: fullUrl,
            Publisher: "CourtListener / PACER",
            PublishedAt: publishedAt,
            RawJson: item.GetRawText());
    }

    private static string BuildSummary(string caseName, string? court, string? cause, string? docketNumber, string? dateFiled)
    {
        var sb = new StringBuilder();
        sb.Append($"Case: {caseName}.");
        if (!string.IsNullOrWhiteSpace(court)) sb.Append($" Court: {court}.");
        if (!string.IsNullOrWhiteSpace(cause)) sb.Append($" Cause: {cause}.");
        if (!string.IsNullOrWhiteSpace(docketNumber)) sb.Append($" Docket: {docketNumber}.");
        if (!string.IsNullOrWhiteSpace(dateFiled)) sb.Append($" Filed: {dateFiled}.");
        return sb.ToString();
    }

    private static string StableId(string docketId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"courtlistener:{docketId}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
