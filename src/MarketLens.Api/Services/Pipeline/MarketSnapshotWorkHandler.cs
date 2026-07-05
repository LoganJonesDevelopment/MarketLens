using System.Text.Json;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.Services.Pipeline;

public sealed record MarketSnapshotWorkResult(Guid ClusterId, bool Captured, bool Current, bool MissingQuote);

public sealed class MarketSnapshotWorkHandler(
    MarketLensDbContext db,
    IMarketDataClient marketData,
    MarketReactionCalculator calculator,
    IOptions<MarketSnapshotOptions> options)
{
    private readonly MarketSnapshotOptions _options = options.Value;

    public async Task<MarketSnapshotWorkResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var clusterId = payload.ClusterId ?? ParseClusterId(naturalKey);
        if (clusterId is null)
            throw new InvalidOperationException($"Unsupported market snapshot work item '{naturalKey}'.");

        return await CaptureAsync(clusterId.Value, cancellationToken);
    }

    private async Task<MarketSnapshotWorkResult> CaptureAsync(
        Guid clusterId,
        CancellationToken cancellationToken)
    {
        var ev = await db.Events
            .Include(e => e.Cluster)
            .SingleOrDefaultAsync(e => e.ClusterId == clusterId, cancellationToken);

        if (ev?.Cluster is null)
            return new MarketSnapshotWorkResult(clusterId, Captured: false, Current: false, MissingQuote: false);

        var symbol = NormalizeSymbol(ev.Cluster.Symbol);
        if (string.IsNullOrWhiteSpace(symbol))
            return new MarketSnapshotWorkResult(clusterId, Captured: false, Current: false, MissingQuote: false);

        var now = DateTime.UtcNow;
        var snapshotCutoff = now.AddMinutes(-Math.Max(_options.RefreshIntervalMinutes, 1));
        var latestSnapshot = await db.MarketSnapshots
            .AsNoTracking()
            .Where(s => s.ClusterId == clusterId)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestSnapshot is not null && latestSnapshot.CapturedAt > snapshotCutoff)
            return new MarketSnapshotWorkResult(clusterId, Captured: false, Current: true, MissingQuote: false);

        var benchmarkSymbol = NormalizeSymbol(_options.BenchmarkSymbol);
        var benchmark = string.IsNullOrWhiteSpace(benchmarkSymbol)
            ? null
            : await marketData.GetQuoteAsync(benchmarkSymbol, cancellationToken);
        var benchmarkMove = calculator.ComputeMovePercent(benchmark);

        var quote = await marketData.GetQuoteAsync(symbol, cancellationToken);
        if (quote is null)
            return new MarketSnapshotWorkResult(clusterId, Captured: false, Current: false, MissingQuote: true);

        var move = calculator.ComputeMovePercent(quote);
        var relativeMove = calculator.ComputeRelativeMovePercent(move, benchmarkMove);

        var todayVolume = quote.Volume;
        var avgVolume = quote.AverageVolume;
        if (todayVolume is null || avgVolume is null)
        {
            var barCutoff = DateTime.UtcNow.AddDays(-30);
            var recentBars = await db.PriceBars
                .AsNoTracking()
                .Where(b => b.Symbol == symbol && b.Interval == "1d" && b.Timestamp >= barCutoff)
                .OrderByDescending(b => b.Timestamp)
                .Take(21)
                .Select(b => b.Volume)
                .ToListAsync(cancellationToken);

            var volumeBars = recentBars.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            if (volumeBars.Count >= 2)
            {
                todayVolume ??= volumeBars[0];
                avgVolume ??= (long)volumeBars.Skip(1).Average(v => (double)v);
            }
        }

        var relativeVolume = calculator.ComputeRelativeVolume(todayVolume, avgVolume);
        db.MarketSnapshots.Add(new MarketSnapshot
        {
            Id = Guid.NewGuid(),
            ClusterId = clusterId,
            Symbol = symbol,
            Provider = quote.Provider,
            Status = latestSnapshot is null ? "initial" : "refresh",
            CapturedAt = DateTime.UtcNow,
            QuoteTime = quote.QuoteTime,
            LastPrice = quote.LastPrice,
            PreviousClose = quote.PreviousClose,
            OpenPrice = quote.OpenPrice,
            HighPrice = quote.HighPrice,
            LowPrice = quote.LowPrice,
            MovePercent = move,
            BenchmarkSymbol = benchmark?.Symbol,
            BenchmarkMovePercent = benchmarkMove,
            RelativeMovePercent = relativeMove,
            Volume = todayVolume,
            AverageVolume = avgVolume,
            RelativeVolume = relativeVolume,
            ReactionScore = calculator.ComputeReactionScore(relativeMove, move, relativeVolume),
            IsAfterHours = IsAfterHours(now),
            IsStale = quote.QuoteTime is null || quote.QuoteTime.Value < now.AddMinutes(-Math.Max(_options.StaleQuoteMinutes, 1)),
            RawPayload = quote.RawJson,
        });

        await db.SaveChangesAsync(cancellationToken);
        return new MarketSnapshotWorkResult(clusterId, Captured: true, Current: false, MissingQuote: false);
    }

    private static Guid? ParseClusterId(string naturalKey)
        => Guid.TryParse(naturalKey, out var id) ? id : null;

    private static string? NormalizeSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static bool IsAfterHours(DateTime utcNow)
    {
        var eastern = TimeZoneInfo.ConvertTimeFromUtc(utcNow, EasternTimeZone);
        if (eastern.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return true;

        var marketOpen = new TimeOnly(9, 30);
        var marketClose = new TimeOnly(16, 0);
        var current = TimeOnly.FromDateTime(eastern);

        return current < marketOpen || current > marketClose;
    }

    private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private static MarketSnapshotPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new MarketSnapshotPayload();

        try
        {
            return JsonSerializer.Deserialize<MarketSnapshotPayload>(
                payloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new MarketSnapshotPayload();
        }
        catch (JsonException)
        {
            return new MarketSnapshotPayload();
        }
    }

    private sealed class MarketSnapshotPayload
    {
        public Guid? ClusterId { get; set; }
    }
}
