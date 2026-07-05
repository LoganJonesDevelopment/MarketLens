using System.Globalization;
using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class FinnhubPriceBarSource(
    HttpClient httpClient,
    IOptions<FinnhubOptions> options,
    ILogger<FinnhubPriceBarSource> logger) : IPriceBarSource
{
    private readonly FinnhubOptions _options = options.Value;

    public string Name => "finnhub";

    public async Task<PriceBarBatch?> FetchAsync(
        string symbol,
        string interval,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return null;

        var resolution = MapResolution(interval);
        if (resolution is null)
        {
            logger.LogWarning("Unsupported interval {Interval} for FinnhubPriceBarSource", interval);
            return null;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol))
            return null;

        var fromUnix = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var toUnix = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        try
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/stock/candle?symbol={Uri.EscapeDataString(normalizedSymbol)}&resolution={resolution}&from={fromUnix}&to={toUnix}&token={Uri.EscapeDataString(_options.ApiKey)}";
            var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                logger.LogWarning("Finnhub /stock/candle returned 403 for {Symbol} — endpoint requires premium tier", normalizedSymbol);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Finnhub /stock/candle returned {Status} for {Symbol}", response.StatusCode, normalizedSymbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("s", out var statusEl) || statusEl.GetString() != "ok")
                return new PriceBarBatch(normalizedSymbol, interval, Name, []);

            var times = ReadLongArray(root, "t");
            var opens = ReadDecimalArray(root, "o");
            var highs = ReadDecimalArray(root, "h");
            var lows = ReadDecimalArray(root, "l");
            var closes = ReadDecimalArray(root, "c");
            var volumes = ReadLongArray(root, "v");

            var count = times.Count;
            if (opens.Count != count || highs.Count != count || lows.Count != count || closes.Count != count)
            {
                logger.LogWarning("Finnhub /stock/candle returned mismatched arrays for {Symbol}", normalizedSymbol);
                return null;
            }

            var bars = new List<PriceBarRow>(count);
            for (var i = 0; i < count; i++)
            {
                var ts = DateTimeOffset.FromUnixTimeSeconds(times[i]).UtcDateTime;
                long? volume = i < volumes.Count ? volumes[i] : null;
                bars.Add(new PriceBarRow(ts, opens[i], highs[i], lows[i], closes[i], volume));
            }

            return new PriceBarBatch(normalizedSymbol, interval, Name, bars);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Finnhub price bar fetch failed for {Symbol} {Interval}", symbol, interval);
            return null;
        }
    }

    private static string? MapResolution(string interval) => interval.Trim().ToLowerInvariant() switch
    {
        "1m" => "1",
        "5m" => "5",
        "15m" => "15",
        "30m" => "30",
        "1h" or "60m" => "60",
        "1d" or "d" => "D",
        "1w" or "w" => "W",
        "1mo" or "mo" => "M",
        _ => null,
    };

    private static List<long> ReadLongArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<long>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
            if (item.TryGetInt64(out var v)) list.Add(v);
        return list;
    }

    private static List<decimal> ReadDecimalArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<decimal>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
            if (item.TryGetDecimal(out var v)) list.Add(v);
        return list;
    }
}
