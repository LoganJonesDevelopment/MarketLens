using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public static class PriceBarStore
{
    public static async Task<bool> IsDeferredAsync(
        MarketLensDbContext db,
        string symbol,
        string interval,
        string provider,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var nextAttemptAt = await db.PriceBarFetchStates
            .AsNoTracking()
            .Where(s => s.Symbol == symbol && s.Interval == interval && s.Provider == provider)
            .Select(s => s.NextAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);

        return nextAttemptAt.HasValue && nextAttemptAt.Value > nowUtc;
    }

    public static async Task<int> UpsertBarsAsync(
        MarketLensDbContext db,
        PriceBarBatch batch,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var symbol = batch.Symbol;
        var interval = PriceBarIntervals.Normalize(batch.Interval) ?? batch.Interval;

        var normalizedBars = batch.Bars
            .Select(b => (Key: PriceBarIntervals.NormalizeTimestamp(interval, b.Timestamp), Bar: b))
            .GroupBy(x => x.Key)
            .Select(g => (Timestamp: g.Key, Bar: g.OrderByDescending(x => x.Bar.Timestamp).First().Bar))
            .ToList();

        if (normalizedBars.Count == 0)
            return 0;

        var timestamps = normalizedBars.Select(x => x.Timestamp).ToList();
        var timestampSet = timestamps.ToHashSet();

        Dictionary<DateTime, PriceBar> existing;
        var bucketSpan = PriceBarIntervals.BucketSpan(interval);
        if (bucketSpan > TimeSpan.Zero)
        {
            var minTs = timestamps.Min();
            var maxExclusive = timestamps.Max().Add(bucketSpan);
            var candidates = await db.PriceBars
                .Where(b => b.Symbol == symbol && b.Interval == interval
                            && b.Timestamp >= minTs && b.Timestamp < maxExclusive)
                .ToListAsync(cancellationToken);

            existing = new Dictionary<DateTime, PriceBar>();
            foreach (var group in candidates
                         .GroupBy(b => PriceBarIntervals.NormalizeTimestamp(interval, b.Timestamp))
                         .Where(g => timestampSet.Contains(g.Key)))
            {
                var selected = group
                    .OrderByDescending(b => b.IngestedAt)
                    .ThenByDescending(b => b.Timestamp)
                    .First();
                var target = group.FirstOrDefault(b => b.Timestamp == group.Key) ?? selected;

                if (!ReferenceEquals(target, selected))
                {
                    target.Open = selected.Open;
                    target.High = selected.High;
                    target.Low = selected.Low;
                    target.Close = selected.Close;
                    target.Volume = selected.Volume;
                    target.Source = selected.Source;
                    target.IngestedAt = selected.IngestedAt;
                }

                foreach (var duplicate in group.Where(b => !ReferenceEquals(b, target)))
                    db.PriceBars.Remove(duplicate);

                target.Timestamp = group.Key;
                existing[group.Key] = target;
            }
        }
        else
        {
            existing = await db.PriceBars
                .Where(b => b.Symbol == symbol && b.Interval == interval && timestamps.Contains(b.Timestamp))
                .ToDictionaryAsync(b => b.Timestamp, cancellationToken);
        }

        foreach (var (normalizedTs, row) in normalizedBars)
        {
            if (existing.TryGetValue(normalizedTs, out var current))
            {
                current.Timestamp = normalizedTs;
                current.Open = row.Open;
                current.High = row.High;
                current.Low = row.Low;
                current.Close = row.Close;
                current.Volume = row.Volume;
                current.Source = batch.Source;
                current.IngestedAt = nowUtc;
            }
            else
            {
                db.PriceBars.Add(new PriceBar
                {
                    Symbol = symbol,
                    Interval = interval,
                    Timestamp = normalizedTs,
                    Open = row.Open,
                    High = row.High,
                    Low = row.Low,
                    Close = row.Close,
                    Volume = row.Volume,
                    Source = batch.Source,
                    IngestedAt = nowUtc,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return normalizedBars.Count;
    }

    public static async Task RecordFetchStateAsync(
        MarketLensDbContext db,
        string symbol,
        string interval,
        string provider,
        PriceBarBatch? batch,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var state = await db.PriceBarFetchStates
            .SingleOrDefaultAsync(s => s.Symbol == symbol && s.Interval == interval && s.Provider == provider, cancellationToken);

        if (state is null)
        {
            state = new PriceBarFetchState
            {
                Symbol = symbol,
                Interval = interval,
                Provider = provider,
            };
            db.PriceBarFetchStates.Add(state);
        }

        state.ProviderSymbol = batch?.ProviderSymbol ?? state.ProviderSymbol ?? symbol;
        state.LastAttemptAt = nowUtc;
        state.UpdatedAt = nowUtc;

        if (batch is null)
        {
            state.Status = "error";
            state.ConsecutiveFailureCount++;
            state.LastError = "source returned no batch";
            state.NextAttemptAt = nowUtc.Add(PriceBarIntervals.FailureBackoff(state.ConsecutiveFailureCount));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (batch.Bars.Count == 0)
        {
            state.Status = "empty";
            state.EmptyResultCount++;
            state.ConsecutiveFailureCount = 0;
            state.LastError = "empty result";
            state.NextAttemptAt = nowUtc.Add(PriceBarIntervals.EmptyBackoff(interval, state.EmptyResultCount));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var normalizedTimestamps = batch.Bars
            .Select(b => PriceBarIntervals.NormalizeTimestamp(interval, b.Timestamp))
            .ToList();

        state.Status = "ok";
        state.LastSuccessAt = nowUtc;
        state.NextAttemptAt = nowUtc.Add(PriceBarIntervals.RefreshThreshold(interval));
        state.EarliestFetchedAt = normalizedTimestamps.Min();
        state.LatestFetchedAt = normalizedTimestamps.Max();
        state.EmptyResultCount = 0;
        state.ConsecutiveFailureCount = 0;
        state.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }
}
