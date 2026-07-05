using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FinnhubOptions
{
    public string BaseUrl { get; set; } = "https://finnhub.io/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public int LookbackDays { get; set; } = 1;
    public int DelayBetweenSymbolsMs { get; set; } = 1500;
}

public class FinnhubSource(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    IWatchlistProvider watchlist,
    ILogger<FinnhubSource> logger) : INewsSource
{
    private readonly FinnhubOptions _options = options.Value;
    public string Name => SourceNames.Finnhub;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("Finnhub API key not configured; skipping Finnhub ingestion");
            return [];
        }

        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);
        if (watched.Count == 0) return [];

        var results = new List<IngestedArticle>();
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-_options.LookbackDays);

        foreach (var entry in watched)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"{_options.BaseUrl}/company-news?symbol={Uri.EscapeDataString(entry.Symbol)}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={_options.ApiKey}";
                var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Finnhub returned {Status} for {Ticker}", response.StatusCode, entry.Symbol);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var id = element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                        ? idProp.GetInt64() : 0;
                    if (id == 0) continue;

                    var headline = element.TryGetProperty("headline", out var hl) ? hl.GetString() ?? "" : "";
                    var summary = element.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var newsUrl = element.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var publisher = element.TryGetProperty("source", out var src) ? src.GetString() : null;
                    var datetime = element.TryGetProperty("datetime", out var dt) && dt.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(dt.GetInt64()).UtcDateTime
                        : DateTime.UtcNow;
                    datetime = DateTime.SpecifyKind(datetime, DateTimeKind.Utc);

                    if (!WatchlistMatcher.Mentions(entry, headline, summary))
                    {
                        continue;
                    }

                    if (!MarketMateriality.IsAggregatorMaterial(headline, summary))
                    {
                        continue;
                    }

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Finnhub,
                        SourceId: id.ToString(),
                        Symbol: entry.Symbol,
                        Headline: headline,
                        Summary: summary,
                        Url: newsUrl,
                        Publisher: publisher,
                        PublishedAt: datetime,
                        RawJson: element.GetRawText()));
                }

                await Task.Delay(_options.DelayBetweenSymbolsMs, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Finnhub fetch failed for {Ticker}", entry.Symbol);
            }
        }
        return results;
    }

}
