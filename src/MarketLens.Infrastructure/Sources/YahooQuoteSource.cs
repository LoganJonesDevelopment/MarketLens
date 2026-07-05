using System.Text.Json;
using MarketLens.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class YahooQuoteSource(
    HttpClient httpClient,
    ILogger<YahooQuoteSource> logger) : IQuoteSource
{
    private const string BaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";

    public string Name => "yahoo";

    public async Task<IReadOnlyList<QuoteSnapshot>> FetchAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0) return [];

        var results = new List<QuoteSnapshot>(symbols.Count);
        foreach (var raw in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = raw.Trim();
            if (string.IsNullOrEmpty(symbol)) continue;

            try
            {
                var url = $"{BaseUrl}/{Uri.EscapeDataString(symbol)}?interval=5m&range=1d&includePrePost=true";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MarketLens/1.0)");
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    results.Add(Failure(symbol, $"http {(int)response.StatusCode}"));
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                    !chart.TryGetProperty("result", out var resultArr) ||
                    resultArr.ValueKind != JsonValueKind.Array ||
                    resultArr.GetArrayLength() == 0)
                {
                    results.Add(Failure(symbol, "empty chart result"));
                    continue;
                }

                var rootResult = resultArr[0];
                var meta = rootResult.TryGetProperty("meta", out var m) ? m : default;
                if (meta.ValueKind != JsonValueKind.Object)
                {
                    results.Add(Failure(symbol, "missing meta"));
                    continue;
                }

                var (lastBar, lastBarTime) = ReadLatestBar(rootResult);
                var last = lastBar ?? ReadDecimal(meta, "regularMarketPrice");
                var prev = ReadDecimal(meta, "chartPreviousClose") ?? ReadDecimal(meta, "previousClose");
                decimal? change = last is not null && prev is not null ? last - prev : null;
                decimal? changePct = last is not null && prev is { } p && p != 0
                    ? Math.Round((decimal)((double)(last.Value - p) / (double)p * 100d), 4)
                    : null;

                DateTime? asOf = lastBarTime;
                if (asOf is null)
                {
                    var asOfUnix = ReadLong(meta, "regularMarketTime");
                    if (asOfUnix is > 0)
                        asOf = DateTimeOffset.FromUnixTimeSeconds(asOfUnix.Value).UtcDateTime;
                }

                results.Add(new QuoteSnapshot(
                    Symbol: ReadString(meta, "symbol") ?? symbol,
                    DisplayName: ReadString(meta, "shortName") ?? ReadString(meta, "longName"),
                    InstrumentType: ReadString(meta, "instrumentType"),
                    Exchange: ReadString(meta, "exchangeName") ?? ReadString(meta, "fullExchangeName"),
                    Currency: ReadString(meta, "currency"),
                    Last: last,
                    PreviousClose: prev,
                    Change: change,
                    ChangePercent: changePct,
                    AsOf: asOf,
                    Status: last is not null ? "ok" : "no_quote",
                    Error: last is not null ? null : "no regularMarketPrice"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Yahoo quote fetch failed for {Symbol}", symbol);
                results.Add(Failure(symbol, ex.Message[..Math.Min(ex.Message.Length, 200)]));
            }
        }

        return results;
    }

    private static QuoteSnapshot Failure(string symbol, string error) => new(
        Symbol: symbol,
        DisplayName: null,
        InstrumentType: null,
        Exchange: null,
        Currency: null,
        Last: null,
        PreviousClose: null,
        Change: null,
        ChangePercent: null,
        AsOf: null,
        Status: "error",
        Error: error);

    private static string? ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? ReadDecimal(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static long? ReadLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static (decimal? Close, DateTime? Time) ReadLatestBar(JsonElement result)
    {
        if (!result.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.Array)
            return (null, null);
        if (!result.TryGetProperty("indicators", out var ind) ||
            !ind.TryGetProperty("quote", out var quoteArr) ||
            quoteArr.ValueKind != JsonValueKind.Array || quoteArr.GetArrayLength() == 0)
            return (null, null);

        var quote = quoteArr[0];
        if (!quote.TryGetProperty("close", out var closes) || closes.ValueKind != JsonValueKind.Array)
            return (null, null);

        var tsLen = ts.GetArrayLength();
        var closeLen = closes.GetArrayLength();
        var len = Math.Min(tsLen, closeLen);

        for (var i = len - 1; i >= 0; i--)
        {
            var c = closes[i];
            if (c.ValueKind != JsonValueKind.Number) continue;
            if (!c.TryGetDecimal(out var closeVal)) continue;

            var t = ts[i];
            if (t.ValueKind != JsonValueKind.Number || !t.TryGetInt64(out var tUnix)) continue;

            return (closeVal, DateTimeOffset.FromUnixTimeSeconds(tUnix).UtcDateTime);
        }

        return (null, null);
    }
}
