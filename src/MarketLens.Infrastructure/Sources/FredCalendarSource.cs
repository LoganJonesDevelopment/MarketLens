using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FredCalendarSource(
    HttpClient httpClient,
    IOptions<FredOptions> options,
    ILogger<FredCalendarSource> logger) : IEconomicCalendarSource
{
    private readonly FredOptions _options = options.Value;

    public string Name => "fred_calendar";

    public async Task<IReadOnlyList<EconomicEventRecord>> FetchAsync(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<string>? symbols,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return [];

        try
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/releases/dates?api_key={_options.ApiKey}&file_type=json&include_release_dates_with_no_data=true&realtime_start={fromUtc:yyyy-MM-dd}&realtime_end={toUtc:yyyy-MM-dd}";
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("release_dates", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var results = new List<EconomicEventRecord>();
            foreach (var item in arr.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var releaseId = TryInt(item, "release_id");
                var releaseName = TryString(item, "release_name") ?? "FRED release";
                var dateStr = TryString(item, "date");
                if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date)) continue;
                date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc).AddHours(13);

                results.Add(new EconomicEventRecord(
                    Source: Name,
                    SourceId: $"{releaseId}:{dateStr}",
                    EventType: "fred_release",
                    Symbol: null,
                    Label: releaseName,
                    ScheduledAt: date,
                    IsTimeSpecific: false,
                    Status: date < DateTime.UtcNow ? "passed" : "scheduled",
                    Notes: null,
                    RawJson: item.GetRawText()));
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FRED release calendar fetch failed");
            return [];
        }
    }

    private static string? TryString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
