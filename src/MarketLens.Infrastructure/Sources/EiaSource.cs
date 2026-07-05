using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class EiaOptions
{
    public string BaseUrl { get; set; } = "https://api.eia.gov/v2";
    public string ApiKey { get; set; } = string.Empty;
    public EiaSeriesConfig[] Series { get; set; } =
    [
        new() { Route = "nuclear-outages/us-nuclear-outages/data", Label = "Nuclear outage status", DataColumns = ["percentOutage", "outage", "capacity"] },
        new() { Route = "electricity/electric-power-operational-data/data", Facet = "fueltypeid", FacetValue = "NUC", Label = "Nuclear electric power generation", DataColumns = ["generation"], ExtraFacets = new() { ["location"] = "US", ["sectorid"] = "98" } },
        new() { Route = "electricity/electric-power-operational-data/data", Facet = "fueltypeid", FacetValue = "ALL", Label = "Total electricity generation", DataColumns = ["generation"], ExtraFacets = new() { ["location"] = "US", ["sectorid"] = "98" } },
        new() { Route = "electricity/electric-power-operational-data/data", Facet = "fueltypeid", FacetValue = "SUN", Label = "Solar electric power generation", DataColumns = ["generation"], ExtraFacets = new() { ["location"] = "US", ["sectorid"] = "98" } },
        new() { Route = "electricity/electric-power-operational-data/data", Facet = "fueltypeid", FacetValue = "WND", Label = "Wind electric power generation", DataColumns = ["generation"], ExtraFacets = new() { ["location"] = "US", ["sectorid"] = "98" } },
        new() { Route = "electricity/electric-power-operational-data/data", Facet = "fueltypeid", FacetValue = "NG", Label = "Natural gas electric power generation", DataColumns = ["generation"], ExtraFacets = new() { ["location"] = "US", ["sectorid"] = "98" } },
        new() { Route = "electricity/operating-generator-capacity/data", Facet = "status", FacetValue = "OP", Label = "Operating generator capacity", DataColumns = ["nameplate-capacity-mw"] },
        new() { Route = "steo/data", Facet = "seriesId", FacetValue = "NUEPGEN_US", Label = "STEO nuclear generation projection", DataColumns = ["value"] },
        new() { Route = "steo/data", Facet = "seriesId", FacetValue = "PAPR_WORLD", Label = "STEO world petroleum production", DataColumns = ["value"] },
    ];
}

public class EiaSeriesConfig
{
    public string Route { get; set; } = string.Empty;
    public string? Facet { get; set; }
    public string? FacetValue { get; set; }
    public Dictionary<string, string> ExtraFacets { get; set; } = new();
    public string Label { get; set; } = string.Empty;
    public string[] DataColumns { get; set; } = [];
}

public class EiaSource(HttpClient httpClient, IOptions<EiaOptions> options, ILogger<EiaSource> logger) : INewsSource
{
    private readonly EiaOptions _options = options.Value;
    public string Name => SourceNames.Eia;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning("EIA API key not configured; skipping EIA ingestion");
            return [];
        }

        var results = new List<IngestedArticle>();
        var since = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM");

        foreach (var series in _options.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = BuildUrl(series, since);
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("response", out var response)) continue;
                if (!response.TryGetProperty("data", out var data)) continue;

                foreach (var row in data.EnumerateArray())
                {
                    var period = row.TryGetProperty("period", out var p) ? p.GetString() ?? "" : "";
                    if (!TryParsePeriod(period, out var dt)) continue;

                    var (value, unit) = ExtractValue(row, series);
                    var desc = row.TryGetProperty("seriesDescription", out var sd) ? sd.GetString() ?? series.Label : series.Label;
                    var facetKey = series.FacetValue ?? series.Route.Split('/')[^2];

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Eia,
                        SourceId: $"eia:{series.Route}:{facetKey}:{period}",
                        Symbol: null,
                        Headline: $"EIA {series.Label}: {value} {unit} ({period})",
                        Summary: $"U.S. Energy Information Administration data — {desc}: {value} {unit} for period {period}.",
                        Url: $"https://www.eia.gov/opendata/browser/{series.Route}",
                        Publisher: "U.S. Energy Information Administration",
                        PublishedAt: dt,
                        RawJson: row.GetRawText()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "EIA fetch failed for {Route}/{FacetValue}", series.Route, series.FacetValue ?? "all");
            }
        }
        return results;
    }

    private string BuildUrl(EiaSeriesConfig series, string since)
    {
        var url = $"{_options.BaseUrl}/{series.Route}?api_key={_options.ApiKey}";
        if (!string.IsNullOrEmpty(series.Facet) && !string.IsNullOrEmpty(series.FacetValue))
            url += $"&facets[{series.Facet}][]={series.FacetValue}";
        foreach (var (key, value) in series.ExtraFacets)
            url += $"&facets[{key}][]={value}";
        url += $"&start={since}&sort[0][column]=period&sort[0][direction]=desc&length=50";
        for (var i = 0; i < series.DataColumns.Length; i++)
            url += $"&data[{i}]={series.DataColumns[i]}";
        return url;
    }

    private static (string Value, string Unit) ExtractValue(JsonElement row, EiaSeriesConfig series)
    {
        if (row.TryGetProperty("value", out var v))
        {
            var unit = row.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : "";
            return (v.ToString(), unit);
        }

        foreach (var col in series.DataColumns)
        {
            if (row.TryGetProperty(col, out var colVal))
            {
                var unitKey = $"{col}-units";
                var unit = row.TryGetProperty(unitKey, out var uv) ? uv.GetString() ?? "" : "";
                return (colVal.ToString(), unit);
            }
        }

        return ("N/A", "");
    }

    private static bool TryParsePeriod(string period, out DateTime dt)
    {
        dt = default;
        if (DateTime.TryParse(period, out dt))
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }
        if (period.Length == 7 && DateTime.TryParse(period + "-01", out dt))
        {
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }
        return false;
    }
}
