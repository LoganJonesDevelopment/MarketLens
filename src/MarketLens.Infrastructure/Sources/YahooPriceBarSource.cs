using System.Text.Json;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class YahooPriceBarSource(
    HttpClient httpClient,
    ILogger<YahooPriceBarSource> logger) : IPriceBarSource
{
    private const string BaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";
    private static readonly Dictionary<string, string> SymbolMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BF.B"] = "BF-B",
        ["BRK.B"] = "BRK-B",
    };

    public string Name => "yahoo";

    public async Task<PriceBarBatch?> FetchAsync(
        string symbol,
        string interval,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var yahooInterval = MapInterval(interval);
        if (yahooInterval is null)
        {
            logger.LogWarning("Unsupported interval {Interval} for YahooPriceBarSource", interval);
            return null;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSymbol)) return null;
        var yahooSymbol = MapSymbol(normalizedSymbol);

        var fromUnix = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var toUnix = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        try
        {
            var url = $"{BaseUrl}/{Uri.EscapeDataString(yahooSymbol)}?interval={yahooInterval}&period1={fromUnix}&period2={toUnix}&includePrePost=false&events=div%2Csplit";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MarketLens/1.0)");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yahoo chart returned {Status} for {Symbol} ({ProviderSymbol})", response.StatusCode, normalizedSymbol, yahooSymbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("chart", out var chart)) return null;
            if (!chart.TryGetProperty("result", out var resultArr) || resultArr.ValueKind != JsonValueKind.Array || resultArr.GetArrayLength() == 0)
                return new PriceBarBatch(normalizedSymbol, interval, Name, [], yahooSymbol);

            var result = resultArr[0];
            if (!result.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.Array)
                return new PriceBarBatch(normalizedSymbol, interval, Name, [], yahooSymbol);

            if (!result.TryGetProperty("indicators", out var ind) ||
                !ind.TryGetProperty("quote", out var quoteArr) ||
                quoteArr.ValueKind != JsonValueKind.Array || quoteArr.GetArrayLength() == 0)
                return new PriceBarBatch(normalizedSymbol, interval, Name, [], yahooSymbol);

            var quote = quoteArr[0];
            var times = ReadLongArray(tsEl);
            var opens = ReadDecimalArray(quote, "open");
            var highs = ReadDecimalArray(quote, "high");
            var lows = ReadDecimalArray(quote, "low");
            var closes = ReadDecimalArray(quote, "close");
            var volumes = ReadLongArray(quote, "volume");

            var count = times.Count;
            var bars = new List<PriceBarRow>(count);
            for (var i = 0; i < count; i++)
            {
                if (i >= opens.Count || i >= highs.Count || i >= lows.Count || i >= closes.Count) break;
                if (opens[i] is null || highs[i] is null || lows[i] is null || closes[i] is null) continue;

                var ts = DateTimeOffset.FromUnixTimeSeconds(times[i]).UtcDateTime;
                long? volume = i < volumes.Count ? volumes[i] : null;
                bars.Add(new PriceBarRow(ts, opens[i]!.Value, highs[i]!.Value, lows[i]!.Value, closes[i]!.Value, volume));
            }

            return new PriceBarBatch(normalizedSymbol, interval, Name, bars, yahooSymbol);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Yahoo chart fetch failed for {Symbol} {Interval}", symbol, interval);
            return null;
        }
    }

    private static string? MapInterval(string interval) => interval.Trim().ToLowerInvariant() switch
    {
        "1m" => "1m",
        "5m" => "5m",
        "15m" => "15m",
        "30m" => "30m",
        "1h" or "60m" => "60m",
        "1d" or "d" => "1d",
        "1w" or "w" => "1wk",
        "1mo" or "mo" => "1mo",
        _ => null,
    };

    public static string MapSymbol(string symbol)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return SymbolMap.TryGetValue(normalized, out var mapped) ? mapped : normalized;
    }

    private static List<long> ReadLongArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<long>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var v)) list.Add(v);
            else list.Add(0);
        }
        return list;
    }

    private static List<long> ReadLongArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<long>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var v)) list.Add(v);
            else list.Add(0);
        }
        return list;
    }

    private static List<decimal?> ReadDecimalArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return [];
        var list = new List<decimal?>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var v)) list.Add(v);
            else list.Add(null);
        }
        return list;
    }
}
