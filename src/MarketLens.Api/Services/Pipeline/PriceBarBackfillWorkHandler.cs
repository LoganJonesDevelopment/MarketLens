using System.Text.Json;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.Services.Pipeline;

public sealed record PriceBarBackfillWorkResult(
    string Symbol,
    string Interval,
    bool Current,
    int BarsFetched,
    int GapBarsFetched);

public sealed class PriceBarBackfillWorkHandler(
    MarketLensDbContext db,
    IPriceBarSource source,
    IOptions<PriceBarBackfillOptions> options,
    ILogger<PriceBarBackfillWorkHandler> logger)
{
    private readonly PriceBarBackfillOptions _options = options.Value;

    public async Task<PriceBarBackfillWorkResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var (naturalSymbol, naturalInterval) = ParseNaturalKey(naturalKey);
        var symbol = NormalizeSymbol(payload.Symbol) ?? naturalSymbol;
        var interval = NormalizeInterval(payload.Interval) ?? naturalInterval ?? "1d";

        if (string.IsNullOrWhiteSpace(symbol))
            throw new InvalidOperationException($"Unsupported price bar backfill work item '{naturalKey}'.");

        var now = DateTime.UtcNow;
        if (await PriceBarStore.IsDeferredAsync(db, symbol, interval, source.Name, now, cancellationToken))
            return new PriceBarBackfillWorkResult(symbol, interval, Current: true, BarsFetched: 0, GapBarsFetched: 0);

        var (fromUtc, toUtc) = await DetermineRangeAsync(symbol, interval, now, cancellationToken);
        var fetched = 0;
        var current = fromUtc >= toUtc;

        if (!current)
        {
            var batch = await source.FetchAsync(symbol, interval, fromUtc, toUtc, cancellationToken);
            await DelayBetweenFetchesAsync(cancellationToken);

            if (batch?.Bars.Count > 0)
            {
                await PriceBarStore.UpsertBarsAsync(db, batch, now, cancellationToken);
                fetched += batch.Bars.Count;
            }

            await PriceBarStore.RecordFetchStateAsync(db, symbol, interval, source.Name, batch, now, cancellationToken);
        }

        var gapFetched = await FillRecentGapsAsync(symbol, interval, now, cancellationToken);
        return new PriceBarBackfillWorkResult(symbol, interval, current, fetched, gapFetched);
    }

    private async Task<int> FillRecentGapsAsync(
        string symbol,
        string interval,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!PriceBarIntervals.IsDaily(interval))
            return 0;

        var maxGaps = Math.Max(0, _options.MaxGapFillsPerItem);
        if (maxGaps == 0)
            return 0;

        var lookback = nowUtc.AddDays(-Math.Max(1, _options.GapLookbackDays));
        var bars = await db.PriceBars
            .AsNoTracking()
            .Where(b => b.Symbol == symbol && b.Interval == interval && b.Timestamp >= lookback)
            .OrderBy(b => b.Timestamp)
            .Select(b => b.Timestamp)
            .ToListAsync(cancellationToken);

        if (bars.Count < 2)
            return 0;

        var filled = 0;
        var gapsChecked = 0;
        for (var i = 1; i < bars.Count && gapsChecked < maxGaps; i++)
        {
            var gap = (bars[i] - bars[i - 1]).TotalDays;
            if (gap <= 3) continue;

            gapsChecked++;
            var gapFrom = bars[i - 1].AddSeconds(-1);
            var gapTo = bars[i].AddDays(1);

            logger.LogInformation(
                "Filling price bar gap for {Symbol}: {From:yyyy-MM-dd} to {To:yyyy-MM-dd} ({Days}d)",
                symbol,
                gapFrom,
                gapTo,
                gap);

            var batch = await source.FetchAsync(symbol, interval, gapFrom, gapTo, cancellationToken);
            await DelayBetweenFetchesAsync(cancellationToken);

            if (batch?.Bars.Count > 0)
            {
                await PriceBarStore.UpsertBarsAsync(db, batch, nowUtc, cancellationToken);
                filled += batch.Bars.Count;
            }
        }

        return filled;
    }

    private async Task<(DateTime from, DateTime to)> DetermineRangeAsync(
        string symbol,
        string interval,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var historyDays = interval switch
        {
            "1d" => _options.DailyHistoryDays,
            "1h" => _options.HourlyHistoryDays,
            _ => _options.IntradayHistoryDays,
        };

        var refreshMinutes = interval switch
        {
            "1d" => _options.RefreshDailyMinutes,
            _ => _options.RefreshHourlyMinutes,
        };

        var latest = await db.PriceBars
            .Where(b => b.Symbol == symbol && b.Interval == interval)
            .OrderByDescending(b => b.Timestamp)
            .Select(b => new { Timestamp = (DateTime?)b.Timestamp, b.IngestedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var requestedFrom = nowUtc.AddDays(-historyDays);

        if (latest is null)
            return (requestedFrom, nowUtc);

        var state = await db.PriceBarFetchStates
            .AsNoTracking()
            .Where(s => s.Symbol == symbol && s.Interval == interval && s.Provider == source.Name)
            .Select(s => new { s.LastSuccessAt })
            .FirstOrDefaultAsync(cancellationToken);

        var lastSuccessfulRefresh = state?.LastSuccessAt ?? latest.IngestedAt;
        if (nowUtc - lastSuccessfulRefresh < TimeSpan.FromMinutes(Math.Max(refreshMinutes, 1)))
            return (nowUtc, nowUtc);

        return (latest.Timestamp!.Value.AddSeconds(-1), nowUtc);
    }

    private async Task DelayBetweenFetchesAsync(CancellationToken cancellationToken)
    {
        if (_options.InterFetchDelayMs <= 0)
            return;

        await Task.Delay(_options.InterFetchDelayMs, cancellationToken);
    }

    private static (string? Symbol, string? Interval) ParseNaturalKey(string naturalKey)
    {
        var parts = naturalKey.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
            return (NormalizeSymbol(parts[0]), NormalizeInterval(parts[1]));

        return (NormalizeSymbol(naturalKey), null);
    }

    private static string? NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static string? NormalizeInterval(string? interval)
        => PriceBarIntervals.Normalize(interval);

    private static PriceBarBackfillPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new PriceBarBackfillPayload();

        try
        {
            return JsonSerializer.Deserialize<PriceBarBackfillPayload>(
                payloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new PriceBarBackfillPayload();
        }
        catch (JsonException)
        {
            return new PriceBarBackfillPayload();
        }
    }

    private sealed class PriceBarBackfillPayload
    {
        public string? Symbol { get; set; }
        public string? Interval { get; set; }
    }
}
