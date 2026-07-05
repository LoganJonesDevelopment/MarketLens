using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FredOptions
{
    public string BaseUrl { get; set; } = "https://api.stlouisfed.org/fred";
    public string ApiKey { get; set; } = string.Empty;
    public string[] Series { get; set; } =
    [
        "CPIAUCSL",
        "CPILFESL",
        "PCEPI",
        "PCEPILFE",
        "UNRATE",
        "PAYEMS",
        "AHETPI",
        "JTSJOL",
        "ICSA",
        "FEDFUNDS",
        "DFF",
        "DGS2",
        "DGS10",
        "T10Y2Y",
        "BAMLH0A0HYM2",
        "GDP",
        "INDPRO",
        "RSAFS",
        "MICH",
        "UMCSENT",
        "HOUST",
        "CSUSHPISA",
        "TOTALSL",
        "MVLOAS",
        "DRCCLACBS",
        "DRSFRMACBS",
        "CUSR0000SETA02",
        "DCOILWTICO",
        "POILBREUSDM",
        "PCOPPUSDM",
        "PURANUSDM",
        "PNICKUSDM",
        "PZINCUSDM",
        "GOLDPMGBD228NLBM",
        "PALUMUSDM",
        "M2SL",
        "WALCL",
    ];
}

public class FredSource(HttpClient httpClient, IOptions<FredOptions> options, ILogger<FredSource> logger) : INewsSource
{
    private readonly FredOptions _options = options.Value;
    public string Name => SourceNames.Fred;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("FRED API key not configured; skipping FRED ingestion");
            return [];
        }

        var results = new List<IngestedArticle>();
        var since = DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-dd");

        foreach (var seriesId in _options.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"{_options.BaseUrl}/series/observations?series_id={seriesId}&observation_start={since}&api_key={_options.ApiKey}&file_type=json";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("observations", out var obs)) continue;

                foreach (var o in obs.EnumerateArray())
                {
                    var dateStr = o.GetProperty("date").GetString() ?? string.Empty;
                    var value = o.GetProperty("value").GetString() ?? "";
                    if (!DateTime.TryParse(dateStr, out var dt)) continue;
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Fred,
                        SourceId: $"{seriesId}:{dateStr}",
                        Symbol: null,
                        Headline: $"FRED {seriesId} release: {value} ({dateStr})",
                        Summary: $"Federal Reserve Economic Data series {seriesId} observation {value} for {dateStr}.",
                        Url: $"https://fred.stlouisfed.org/series/{seriesId}",
                        Publisher: "Federal Reserve Bank of St. Louis",
                        PublishedAt: dt,
                        RawJson: o.GetRawText()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FRED fetch failed for {Series}", seriesId);
            }
        }
        return results;
    }
}
