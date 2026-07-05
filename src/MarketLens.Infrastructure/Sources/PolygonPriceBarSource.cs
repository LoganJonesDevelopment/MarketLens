using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class PolygonPriceBarSource(
    HttpClient httpClient,
    IOptions<PolygonOptions> options,
    ILogger<PolygonPriceBarSource> logger) : IPriceBarSource
{
    private readonly PolygonOptions _options = options.Value;

    public string Name => "polygon";

    public async Task<PriceBarBatch?> FetchAsync(
        string symbol,
        string interval,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return null;

        var span = MapTimespan(interval);
        if (span is null)
        {
            logger.LogWarning("Unsupported interval {Interval} for PolygonPriceBarSource", interval);
            return null;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
            return null;

        var fromMs = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var toMs = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        try
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v2/aggs/ticker/{Uri.EscapeDataString(normalizedSymbol)}/range/{span.Multiplier}/{span.Unit}/{fromMs}/{toMs}?adjusted=true&sort=asc&limit=50000&apiKey={Uri.EscapeDataString(_options.ApiKey)}";

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("Polygon aggregates rate-limited for {Symbol} {Interval}", normalizedSymbol, interval);
                return null;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Polygon aggregates returned {Status} for {Symbol} — check plan/key", response.StatusCode, normalizedSymbol);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Polygon aggregates returned {Status} for {Symbol} {Interval}", response.StatusCode, normalizedSymbol, interval);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusEl))
            {
                var status = statusEl.GetString();
                if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : "unknown";
                    logger.LogWarning("Polygon aggregates error for {Symbol} {Interval}: {Error}", normalizedSymbol, interval, error);
                    return null;
                }
            }

            if (!root.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
                return new PriceBarBatch(normalizedSymbol, interval, Name, []);

            var bars = new List<PriceBarRow>(resultsEl.GetArrayLength());
            foreach (var item in resultsEl.EnumerateArray())
            {
                if (!item.TryGetProperty("t", out var tEl) || !tEl.TryGetInt64(out var tMs)) continue;
                if (!item.TryGetProperty("o", out var oEl) || !oEl.TryGetDecimal(out var o)) continue;
                if (!item.TryGetProperty("h", out var hEl) || !hEl.TryGetDecimal(out var h)) continue;
                if (!item.TryGetProperty("l", out var lEl) || !lEl.TryGetDecimal(out var l)) continue;
                if (!item.TryGetProperty("c", out var cEl) || !cEl.TryGetDecimal(out var c)) continue;

                long? volume = null;
                if (item.TryGetProperty("v", out var vEl) && vEl.ValueKind == JsonValueKind.Number)
                {
                    if (vEl.TryGetInt64(out var vLong)) volume = vLong;
                    else if (vEl.TryGetDecimal(out var vDec)) volume = (long)vDec;
                }

                var ts = DateTimeOffset.FromUnixTimeMilliseconds(tMs).UtcDateTime;
                bars.Add(new PriceBarRow(ts, o, h, l, c, volume));
            }

            return new PriceBarBatch(normalizedSymbol, interval, Name, bars);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Polygon price bar fetch failed for {Symbol} {Interval}", symbol, interval);
            return null;
        }
    }

    private static Timespan? MapTimespan(string interval) => interval.Trim().ToLowerInvariant() switch
    {
        "1m" => new Timespan(1, "minute"),
        "5m" => new Timespan(5, "minute"),
        "15m" => new Timespan(15, "minute"),
        "30m" => new Timespan(30, "minute"),
        "1h" or "60m" => new Timespan(1, "hour"),
        "1d" or "d" => new Timespan(1, "day"),
        "1w" or "w" => new Timespan(1, "week"),
        "1mo" or "mo" => new Timespan(1, "month"),
        _ => null,
    };

    private sealed record Timespan(int Multiplier, string Unit);
}
