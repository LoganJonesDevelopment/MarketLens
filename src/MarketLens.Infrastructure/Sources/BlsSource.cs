using System.Globalization;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class BlsOptions
{
    public string BaseUrl { get; set; } = "https://api.bls.gov/publicAPI/v2/timeseries/data";
    public string ApiKey { get; set; } = string.Empty;
    public int HistoryYears { get; set; } = 2;
    public BlsSeriesConfig[] Series { get; set; } =
    [
        new()
        {
            SeriesId = "CUSR0000SA0",
            Label = "CPI-U all items",
            Release = "Consumer Price Index",
            Url = "https://www.bls.gov/news.release/cpi.htm",
        },
        new()
        {
            SeriesId = "CUSR0000SA0L1E",
            Label = "Core CPI-U",
            Release = "Consumer Price Index",
            Url = "https://www.bls.gov/news.release/cpi.htm",
        },
        new()
        {
            SeriesId = "WPSFD4",
            Label = "PPI final demand",
            Release = "Producer Price Index",
            Url = "https://www.bls.gov/news.release/ppi.htm",
        },
    ];
}

public class BlsSeriesConfig
{
    public string SeriesId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Release { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class BlsSource(HttpClient httpClient, IOptions<BlsOptions> options, ILogger<BlsSource> logger) : INewsSource
{
    private readonly BlsOptions _options = options.Value;

    public string Name => SourceNames.Bls;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        var endYear = DateTime.UtcNow.Year;
        var startYear = Math.Max(1900, endYear - Math.Max(1, _options.HistoryYears));

        foreach (var series in _options.Series.Where(s => !string.IsNullOrWhiteSpace(s.SeriesId)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await httpClient.GetStringAsync(BuildUrl(series.SeriesId, startYear, endYear), cancellationToken);
                using var doc = JsonDocument.Parse(json);

                var status = doc.RootElement.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : null;
                if (!string.Equals(status, "REQUEST_SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("BLS fetch for {SeriesId} returned status {Status}", series.SeriesId, status ?? "unknown");
                    continue;
                }

                if (!doc.RootElement.TryGetProperty("Results", out var resultsElement) ||
                    !resultsElement.TryGetProperty("series", out var seriesArray))
                {
                    continue;
                }

                var payload = seriesArray.EnumerateArray().FirstOrDefault();
                if (payload.ValueKind == JsonValueKind.Undefined ||
                    !payload.TryGetProperty("data", out var observations))
                {
                    continue;
                }

                var points = observations
                    .EnumerateArray()
                    .Select(ParseObservation)
                    .Where(p => p is not null)
                    .Select(p => p!)
                    .OrderByDescending(p => p.Year)
                    .ThenByDescending(p => p.Month)
                    .ToList();

                var latest = points.FirstOrDefault();
                if (latest is null) continue;

                var previousMonth = points.FirstOrDefault(p => IsPreviousMonth(latest, p));
                var previousYear = points.FirstOrDefault(p => p.Year == latest.Year - 1 && p.Month == latest.Month);
                var changeText = BuildChangeText(latest, previousMonth, previousYear);
                var period = $"{latest.PeriodName} {latest.Year}";
                var value = latest.Value.ToString("0.###", CultureInfo.InvariantCulture);

                results.Add(new IngestedArticle(
                    Source: SourceNames.Bls,
                    SourceId: $"bls-api:{series.SeriesId}:{latest.Year}:M{latest.Month:00}:{value}",
                    Symbol: null,
                    Headline: $"BLS {series.Label}: {value} for {period}{changeText.HeadlineSuffix}",
                    Summary: $"Bureau of Labor Statistics {series.Release} latest observation for {period}: {value}.{changeText.SummarySuffix}",
                    Url: string.IsNullOrWhiteSpace(series.Url)
                        ? $"https://www.bls.gov/data/#series_id={Uri.EscapeDataString(series.SeriesId)}"
                        : series.Url,
                    Publisher: "U.S. Bureau of Labor Statistics",
                    PublishedAt: new DateTime(latest.Year, latest.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                    RawJson: latest.RawJson));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BLS fetch failed for {SeriesId}", series.SeriesId);
            }
        }

        return results;
    }

    private string BuildUrl(string seriesId, int startYear, int endYear)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(seriesId)}?startyear={startYear}&endyear={endYear}";
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            url += $"&registrationkey={Uri.EscapeDataString(_options.ApiKey)}";
        return url;
    }

    private static BlsObservation? ParseObservation(JsonElement item)
    {
        var yearText = item.TryGetProperty("year", out var yearProp) ? yearProp.GetString() : null;
        var periodText = item.TryGetProperty("period", out var periodProp) ? periodProp.GetString() : null;
        var periodName = item.TryGetProperty("periodName", out var periodNameProp)
            ? periodNameProp.GetString() ?? periodText ?? string.Empty
            : periodText ?? string.Empty;
        var valueText = item.TryGetProperty("value", out var valueProp) ? valueProp.GetString() : null;

        if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return null;
        if (string.IsNullOrWhiteSpace(periodText) || !periodText.StartsWith("M", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(periodText[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var month)) return null;
        if (month is < 1 or > 12) return null;
        if (!decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;

        return new BlsObservation(year, month, periodName, value, item.GetRawText());
    }

    private static bool IsPreviousMonth(BlsObservation latest, BlsObservation candidate)
    {
        var previousYear = latest.Month == 1 ? latest.Year - 1 : latest.Year;
        var previousMonth = latest.Month == 1 ? 12 : latest.Month - 1;
        return candidate.Year == previousYear && candidate.Month == previousMonth;
    }

    private static BlsChangeText BuildChangeText(
        BlsObservation latest,
        BlsObservation? previousMonth,
        BlsObservation? previousYear)
    {
        var pieces = new List<string>();
        if (previousMonth is not null)
            pieces.Add($"month-over-month {PercentChange(latest.Value, previousMonth.Value):+0.##;-0.##;0}%");
        if (previousYear is not null)
            pieces.Add($"year-over-year {PercentChange(latest.Value, previousYear.Value):+0.##;-0.##;0}%");

        if (pieces.Count == 0) return new BlsChangeText(string.Empty, string.Empty);

        var text = string.Join(", ", pieces);
        return new BlsChangeText($" ({text})", $" Change: {text}.");
    }

    private static decimal PercentChange(decimal latest, decimal previous)
        => previous == 0 ? 0 : (latest - previous) / previous * 100m;

    private sealed record BlsObservation(int Year, int Month, string PeriodName, decimal Value, string RawJson);

    private sealed record BlsChangeText(string HeadlineSuffix, string SummarySuffix);
}
