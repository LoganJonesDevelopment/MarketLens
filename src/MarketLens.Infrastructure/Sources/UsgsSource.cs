using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class UsgsSource(HttpClient httpClient, ILogger<UsgsSource> logger) : INewsSource
{
    private const string BaseUrl = "https://pubs.usgs.gov/pubs-services/publication";

    private static readonly string[] SearchTerms =
    [
        "mineral commodity",
        "critical mineral",
        "uranium",
        "lithium",
        "copper",
        "nickel",
        "zinc",
        "rare earth",
    ];

    public string Name => SourceNames.Usgs;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        var seen = new HashSet<string>();
        var since = DateTime.UtcNow.AddDays(-90);

        foreach (var term in SearchTerms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"{BaseUrl}?q={Uri.EscapeDataString(term)}&pageSize=20&orderBy=displayToPublicDate+desc";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("records", out var records)) continue;

                foreach (var rec in records.EnumerateArray())
                {
                    var id = rec.TryGetProperty("id", out var idProp) ? idProp.ToString() : "";
                    if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;

                    var title = rec.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var dateStr = rec.TryGetProperty("displayToPublicDate", out var d) ? d.GetString() ?? "" : "";
                    if (!DateTime.TryParse(dateStr, out var dt)) continue;
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    if (dt < since) continue;

                    var pubType = "";
                    if (rec.TryGetProperty("publicationType", out var pt) && pt.TryGetProperty("text", out var ptText))
                        pubType = ptText.GetString() ?? "";

                    var doi = rec.TryGetProperty("doi", out var doiProp) ? doiProp.GetString() ?? "" : "";
                    var docUrl = !string.IsNullOrEmpty(doi) ? $"https://doi.org/{doi}" : $"https://pubs.usgs.gov/publication/{id}";

                    var abstractText = rec.TryGetProperty("docAbstract", out var abs) ? abs.GetString() : null;

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Usgs,
                        SourceId: $"usgs:{id}",
                        Symbol: null,
                        Headline: $"USGS {pubType}: {title}",
                        Summary: abstractText,
                        Url: docUrl,
                        Publisher: "U.S. Geological Survey",
                        PublishedAt: dt,
                        RawJson: rec.GetRawText()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "USGS fetch failed for term {Term}", term);
            }
        }
        return results;
    }
}
