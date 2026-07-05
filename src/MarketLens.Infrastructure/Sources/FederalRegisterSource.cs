using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class FederalRegisterSource(
    HttpClient httpClient,
    IWatchlistProvider watchlist,
    ILogger<FederalRegisterSource> logger) : INewsSource
{
    private const string BaseUrl = "https://www.federalregister.gov/api/v1/documents.json";
    private const int LookbackDays = 90;

    public string Name => SourceNames.Bis;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        var cutoff = DateTime.UtcNow.AddDays(-LookbackDays).ToString("yyyy-MM-dd");
        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"{BaseUrl}?conditions%5Bagencies%5D%5B%5D=industry-and-security-bureau" +
                      $"&conditions%5Bpublication_date%5D%5Bgte%5D={cutoff}" +
                      $"&per_page=100&page={page}";

            try
            {
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("results", out var resultsEl)) break;

                var items = resultsEl.EnumerateArray().ToList();
                if (items.Count == 0) break;

                foreach (var item in items)
                {
                    var article = ParseDocument(item, watched);
                    if (article is not null)
                        results.Add(article);
                }

                if (!doc.RootElement.TryGetProperty("next_page_url", out var nextPage) ||
                    string.IsNullOrWhiteSpace(nextPage.GetString()))
                    break;

                page++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FederalRegister fetch failed on page {Page}", page);
                break;
            }
        }

        return results;
    }

    private static IngestedArticle? ParseDocument(JsonElement item, IReadOnlyList<WatchedTicker> watched)
    {
        var documentNumber = item.TryGetProperty("document_number", out var dn) ? dn.GetString() : null;
        if (string.IsNullOrWhiteSpace(documentNumber)) return null;

        var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(title)) return null;

        var abstractText = item.TryGetProperty("abstract", out var ab) ? ab.GetString() : null;
        var htmlUrl = item.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
        var publicationDate = item.TryGetProperty("publication_date", out var pd) ? pd.GetString() : null;

        if (!DateTime.TryParse(publicationDate, out var publishedAt))
            publishedAt = DateTime.UtcNow;
        publishedAt = DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);

        return new IngestedArticle(
            Source: SourceNames.Bis,
            SourceId: StableId(documentNumber),
            Symbol: WatchlistMatcher.MatchSymbol(watched, title, abstractText),
            Headline: title,
            Summary: abstractText,
            Url: htmlUrl,
            Publisher: "Federal Register / BIS",
            PublishedAt: publishedAt,
            RawJson: item.GetRawText());
    }

    private static string StableId(string documentNumber)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"bis:{documentNumber}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
