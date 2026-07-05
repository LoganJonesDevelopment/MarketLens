using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FinnhubEarningsCalendarSource(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubEarningsCalendarSource> logger) : IEconomicCalendarSource
{
    private readonly FinnhubOptions _options = options.Value;

    public string Name => "finnhub_earnings";

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
            var url = $"{baseUrl}/calendar/earnings?from={fromUtc:yyyy-MM-dd}&to={toUtc:yyyy-MM-dd}&token={Uri.EscapeDataString(_options.ApiKey)}";
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Finnhub /calendar/earnings returned {Status}", response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("earningsCalendar", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var symbolFilter = symbols is { Count: > 0 }
                ? new HashSet<string>(symbols.Select(s => s.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase)
                : null;

            var results = new List<EconomicEventRecord>();
            foreach (var item in arr.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var symbol = TryString(item, "symbol")?.ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;
                if (symbolFilter is not null && !symbolFilter.Contains(symbol)) continue;

                var dateStr = TryString(item, "date");
                if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date)) continue;
                date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

                var hour = TryString(item, "hour");
                var (scheduled, isTimeSpecific) = ResolveHour(date, hour);
                var quarter = TryInt(item, "quarter");
                var year = TryInt(item, "year");
                var label = year.HasValue && quarter.HasValue
                    ? $"{symbol} earnings Q{quarter.Value} {year.Value}"
                    : $"{symbol} earnings";

                results.Add(new EconomicEventRecord(
                    Source: Name,
                    SourceId: $"{symbol}:{dateStr}",
                    EventType: "earnings",
                    Symbol: symbol,
                    Label: label,
                    ScheduledAt: scheduled,
                    IsTimeSpecific: isTimeSpecific,
                    Status: scheduled < DateTime.UtcNow ? "passed" : "scheduled",
                    Notes: hour switch
                    {
                        "bmo" => "Before market open",
                        "amc" => "After market close",
                        "dmh" => "During market hours",
                        _ => null,
                    },
                    RawJson: item.GetRawText()));
            }
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Finnhub earnings calendar fetch failed");
            return [];
        }
    }

    private static (DateTime scheduled, bool isTimeSpecific) ResolveHour(DateTime utcDate, string? hour)
    {
        var easternBase = DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Unspecified);
        DateTime localEastern;
        bool isTimeSpecific;
        switch (hour)
        {
            case "bmo": localEastern = easternBase.AddHours(8); isTimeSpecific = true; break;
            case "amc": localEastern = easternBase.AddHours(16).AddMinutes(15); isTimeSpecific = true; break;
            case "dmh": localEastern = easternBase.AddHours(12); isTimeSpecific = true; break;
            default: localEastern = easternBase.AddHours(13); isTimeSpecific = false; break;
        }
        return (TimeZoneInfo.ConvertTimeToUtc(localEastern, EasternTimeZone), isTimeSpecific);
    }

    private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    private static string? TryString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
