using System.Text.Json;
using MarketLens.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarketLens.Infrastructure.Sources;

public class PolygonOptions
{
    public string BaseUrl { get; set; } = "https://api.polygon.io";
    public string ApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public class PolygonQuoteSource(
    HttpClient httpClient,
    IOptions<PolygonOptions> options,
    ILogger<PolygonQuoteSource> logger) : IQuoteSource
{
    private readonly PolygonOptions _options = options.Value;

    public string Name => "polygon";

    public async Task<IReadOnlyList<QuoteSnapshot>> FetchAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0) return [];

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            logger.LogWarning(
                "Polygon API key is not configured (Polygon:ApiKey). Quote ingestion is inert until a key is provided. " +
                "Set the POLYGON_API_KEY environment variable, or use `dotnet user-secrets set Polygon:ApiKey <key>`.");
            return symbols.Select(s => UnconfiguredFailure(s)).ToList();
        }

        var trimmed = symbols
            .Select(s => s?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (trimmed.Count == 0) return [];

        var results = new Dictionary<string, QuoteSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in Chunk(trimmed, 50))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshotOutcome = await TrySnapshotBatchAsync(batch, results, cancellationToken);
            if (snapshotOutcome == BatchOutcome.NotAuthorized)
            {
                foreach (var s in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (results.TryGetValue(s, out var existing) && existing.Status == "ok") continue;
                    results[s] = await FetchPrevAggregateAsync(s, cancellationToken);
                }
            }
        }

        return trimmed
            .Select(s => results.TryGetValue(s, out var v) ? v : Failure(s, "missing"))
            .ToList();
    }

    private enum BatchOutcome { Ok, NotAuthorized, Error }

    private async Task<BatchOutcome> TrySnapshotBatchAsync(
        List<string> batch,
        Dictionary<string, QuoteSnapshot> results,
        CancellationToken cancellationToken)
    {
        try
        {
            var tickerParam = string.Join(',', batch.Select(Uri.EscapeDataString));
            var url = $"{_options.BaseUrl.TrimEnd('/')}/v3/snapshot?ticker.any_of={tickerParam}&limit={batch.Count}&apiKey={Uri.EscapeDataString(_options.ApiKey)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403 ||
                body.Contains("NOT_AUTHORIZED", StringComparison.Ordinal))
            {
                logger.LogInformation(
                    "Polygon snapshot endpoint not authorized on this plan; falling back to prev-day aggregates for [{Batch}]",
                    string.Join(',', batch));
                return BatchOutcome.NotAuthorized;
            }

            if (!response.IsSuccessStatusCode)
            {
                var bodyPreview = body.Length <= 240 ? body : body[..240];
                logger.LogWarning("Polygon snapshot returned {Status} for batch [{Batch}]: {Body}",
                    response.StatusCode, string.Join(',', batch), bodyPreview);
                foreach (var s in batch) results[s] = Failure(s, $"http {(int)response.StatusCode}");
                return BatchOutcome.Error;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var resultsEl) || resultsEl.ValueKind != JsonValueKind.Array)
            {
                foreach (var s in batch) results[s] = Failure(s, "no results array");
                return BatchOutcome.Error;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in resultsEl.EnumerateArray())
            {
                var snap = ParseSnapshot(item);
                if (snap is null) continue;
                seen.Add(snap.Symbol);
                results[snap.Symbol] = snap;
            }
            foreach (var s in batch)
            {
                if (!seen.Contains(s) && !results.ContainsKey(s))
                    results[s] = Failure(s, "no snapshot returned");
            }
            return BatchOutcome.Ok;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Polygon snapshot fetch failed for batch [{Batch}]", string.Join(',', batch));
            foreach (var s in batch)
                if (!results.ContainsKey(s))
                    results[s] = Failure(s, ex.Message[..Math.Min(ex.Message.Length, 200)]);
            return BatchOutcome.Error;
        }
    }

    private async Task<QuoteSnapshot> FetchPrevAggregateAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-14);
            var url = $"{_options.BaseUrl.TrimEnd('/')}/v2/aggs/ticker/{Uri.EscapeDataString(symbol)}/range/1/day/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}?adjusted=true&sort=desc&limit=5&apiKey={Uri.EscapeDataString(_options.ApiKey)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Failure(symbol, $"prev http {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("results", out var arr) ||
                arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                return Failure(symbol, "prev: no results");

            var bars = arr.EnumerateArray().ToList();
            var latest = bars[0];
            var prior = bars.Count > 1 ? bars[1] : (JsonElement?)null;

            var close = ReadDecimal(latest, "c");
            var priorClose = prior.HasValue ? ReadDecimal(prior.Value, "c") : null;
            var ts = latest.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.Number && tEl.TryGetInt64(out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
                : (DateTime?)null;

            decimal? change = close is not null && priorClose is not null ? close - priorClose : null;
            decimal? changePct = close is not null && priorClose is { } p && p != 0
                ? Math.Round((decimal)((double)(close.Value - p) / (double)p * 100d), 4)
                : null;

            return new QuoteSnapshot(
                Symbol: symbol,
                DisplayName: null,
                InstrumentType: null,
                Exchange: null,
                Currency: "USD",
                Last: close,
                PreviousClose: priorClose,
                Change: change,
                ChangePercent: changePct,
                AsOf: ts,
                Status: close is not null ? "ok:eod" : "no_quote",
                Error: close is not null ? null : "prev: empty close");
        }
        catch (Exception ex)
        {
            return Failure(symbol, $"prev: {ex.Message[..Math.Min(ex.Message.Length, 180)]}");
        }
    }

    private static QuoteSnapshot? ParseSnapshot(JsonElement item)
    {
        var ticker = ReadString(item, "ticker");
        if (string.IsNullOrEmpty(ticker)) return null;

        var error = ReadString(item, "error");
        if (!string.IsNullOrEmpty(error))
            return Failure(ticker, error);

        var type = ReadString(item, "type");
        var name = ReadString(item, "name");
        var marketStatus = ReadString(item, "market_status");

        decimal? last = null;
        decimal? prevClose = null;
        decimal? change = null;
        decimal? changePct = null;
        DateTime? asOf = null;

        if (item.TryGetProperty("session", out var session) && session.ValueKind == JsonValueKind.Object)
        {
            last ??= ReadDecimal(session, "close");
            last ??= ReadDecimal(session, "last");
            last ??= ReadDecimal(session, "price");
            prevClose ??= ReadDecimal(session, "previous_close");
            change ??= ReadDecimal(session, "change");
            changePct ??= ReadDecimal(session, "change_percent");
        }

        if (item.TryGetProperty("last_quote", out var lastQuote) && lastQuote.ValueKind == JsonValueKind.Object)
        {
            last ??= ReadDecimal(lastQuote, "last");
            last ??= ReadDecimal(lastQuote, "price");
            asOf ??= ReadNanoTimestamp(lastQuote, "last_updated");
        }

        if (item.TryGetProperty("last_trade", out var lastTrade) && lastTrade.ValueKind == JsonValueKind.Object)
        {
            last ??= ReadDecimal(lastTrade, "price");
            asOf ??= ReadNanoTimestamp(lastTrade, "sip_timestamp");
            asOf ??= ReadNanoTimestamp(lastTrade, "participant_timestamp");
        }

        last ??= ReadDecimal(item, "value");
        prevClose ??= ReadDecimal(item, "previous_close");
        asOf ??= ReadNanoTimestamp(item, "last_updated");

        if (change is null && last is not null && prevClose is not null)
            change = last - prevClose;
        if (changePct is null && last is not null && prevClose is { } p && p != 0)
            changePct = Math.Round((decimal)((double)(last.Value - p) / (double)p * 100d), 4);

        var status = last is not null ? "ok" : "no_quote";

        return new QuoteSnapshot(
            Symbol: ticker,
            DisplayName: name,
            InstrumentType: NormalizeType(type),
            Exchange: ReadString(item, "primary_exchange"),
            Currency: ReadString(item, "currency"),
            Last: last,
            PreviousClose: prevClose,
            Change: change,
            ChangePercent: changePct,
            AsOf: asOf,
            Status: marketStatus is { Length: > 0 } ms ? $"{status}:{ms}" : status,
            Error: last is null ? "no value or quote in payload" : null);
    }

    private static string? NormalizeType(string? polygonType) => polygonType switch
    {
        "indices" => "INDEX",
        "indices_us" => "INDEX",
        "fc" => "FUTURE",
        "futures" => "FUTURE",
        "stocks" => "EQUITY",
        "etf" => "ETF",
        "crypto" => "CRYPTO",
        "fx" => "CURRENCY",
        _ => polygonType?.ToUpperInvariant(),
    };

    private static QuoteSnapshot UnconfiguredFailure(string symbol) => new(
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
        Status: "unconfigured",
        Error: "Polygon:ApiKey not set");

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
        Error: error[..Math.Min(error.Length, 200)]);

    private static IEnumerable<List<string>> Chunk(IReadOnlyList<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToList();
    }

    private static async Task<string> ReadPreviewAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Length <= 240 ? body : body[..240];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static decimal? ReadDecimal(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;

    private static DateTime? ReadNanoTimestamp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        if (!v.TryGetInt64(out var nanos)) return null;
        var ms = nanos / 1_000_000L;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }
}
