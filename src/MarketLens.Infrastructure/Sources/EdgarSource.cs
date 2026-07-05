using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class EdgarSource(
    HttpClient httpClient,
    IWatchlistProvider watchlist,
    ILogger<EdgarSource> logger) : INewsSource
{
    public string Name => SourceNames.Edgar;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();

        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);
        var withCik = watched.Where(w => !string.IsNullOrWhiteSpace(w.Cik)).ToList();
        if (withCik.Count == 0) return [];

        foreach (var entry in withCik)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"https://data.sec.gov/submissions/CIK{entry.Cik}.json";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var recent = doc.RootElement.GetProperty("filings").GetProperty("recent");

                var forms = recent.GetProperty("form");
                var accessions = recent.GetProperty("accessionNumber");
                var dates = recent.GetProperty("filingDate");
                var primaryDocs = recent.GetProperty("primaryDocument");
                var items = recent.TryGetProperty("items", out var i) ? i : default;

                for (int idx = 0; idx < forms.GetArrayLength(); idx++)
                {
                    var form = forms[idx].GetString();
                    if (!SecFormDescriptions.IsTracked(form)) continue;

                    var accession = accessions[idx].GetString() ?? string.Empty;
                    var filingDate = dates[idx].GetString() ?? string.Empty;
                    var primaryDoc = primaryDocs[idx].GetString() ?? string.Empty;
                    var itemString = items.ValueKind == JsonValueKind.Array && idx < items.GetArrayLength()
                        ? items[idx].GetString() ?? string.Empty
                        : string.Empty;

                    if (!DateTime.TryParse(filingDate, out var publishedAt)) continue;
                    publishedAt = DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
                    if (publishedAt < DateTime.UtcNow.AddDays(-120)) continue;

                    var (headline, summary) = BuildFilingText(entry.Name, form!, itemString);

                    var cikNoZeros = long.Parse(entry.Cik!);
                    var accNoDashes = accession.Replace("-", "");
                    var filingUrl = $"https://www.sec.gov/Archives/edgar/data/{cikNoZeros}/{accNoDashes}/{primaryDoc}";

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Edgar,
                        SourceId: accession,
                        Symbol: entry.Symbol,
                        Headline: headline,
                        Summary: summary,
                        Url: filingUrl,
                        Publisher: "SEC EDGAR",
                        PublishedAt: publishedAt,
                        RawJson: $$"""{"cik":"{{entry.Cik}}","ticker":"{{entry.Symbol}}","accession":"{{accession}}","items":"{{itemString}}","form":"{{form}}"}"""));
                }

                await Task.Delay(120, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "EDGAR fetch failed for {Ticker} ({Cik})", entry.Symbol, entry.Cik);
            }
        }

        return results;
    }

    private static (string Headline, string Summary) BuildFilingText(string companyName, string form, string itemString)
    {
        if (form.StartsWith("8-K", StringComparison.OrdinalIgnoreCase))
        {
            var itemList = itemString
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            var headline = itemList.Length > 0
                ? $"{companyName} {form}: {string.Join("; ", itemList.Select(SecItemDescriptions.Describe))}"
                : $"{companyName} {form} filing";
            var summary = itemList.Length > 0
                ? string.Join("\n", itemList.Select(it => $"Item {it}: {SecItemDescriptions.Describe(it)}"))
                : "current report";
            return (headline, summary);
        }

        var description = SecFormDescriptions.Describe(form);
        return ($"{companyName} {form}: {description}", description);
    }
}
