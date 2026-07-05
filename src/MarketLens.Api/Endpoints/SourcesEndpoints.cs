using MarketLens.Core.Domain;
using MarketLens.Infrastructure.Data;
using MarketLens.Api.Services.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class SourcesEndpoints
{
    private static readonly IReadOnlyList<string> AllSources = new[]
    {
        SourceNames.Edgar,
        SourceNames.BusinessWire,
        SourceNames.GlobeNewswire,
        SourceNames.PrNewswire,
        SourceNames.IrFeed,
        SourceNames.Fred,
        SourceNames.Census,
        SourceNames.Finnhub,
        SourceNames.MiningCom,
        SourceNames.FedSpeeches,
        SourceNames.FedPress,
        SourceNames.Bls,
        SourceNames.Bea,
        SourceNames.CourtListener,
        SourceNames.SecEnforcement,
        SourceNames.Ftc,
        SourceNames.DojAntitrust,
        SourceNames.Transcript,
        SourceNames.Cnbc,
        SourceNames.NbcNews,
        SourceNames.Cnn,
        SourceNames.CbsNews,
        SourceNames.FoxBusiness,
        SourceNames.SeekingAlpha,
        SourceNames.Npr,
        SourceNames.PewResearch,
        SourceNames.WhiteHouse,
        SourceNames.CryptoPress,
        SourceNames.AiAnalyst,
        SourceNames.Bbc,
        SourceNames.Upi,
        SourceNames.Reddit,
        SourceNames.TechPress,
        SourceNames.IndustryAnalyst,
        SourceNames.EarningsCall,
        SourceNames.Bis,
        SourceNames.Eia,
        SourceNames.Usgs,
        SourceNames.NuclearPress,
        SourceNames.EvPress,
    };

    public static void MapSourcesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sources/health", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var cutoff1h  = now.AddHours(-1);
            var cutoff24h = now.AddHours(-24);
            var cutoff7d  = now.AddDays(-7);

            var rows = await db.Articles
                .AsNoTracking()
                .Where(a => a.IngestedAt >= cutoff7d)
                .GroupBy(a => a.Source)
                .Select(g => new
                {
                    Source       = g.Key,
                    Count1h      = g.Count(a => a.IngestedAt >= cutoff1h),
                    Count24h     = g.Count(a => a.IngestedAt >= cutoff24h),
                    Count7d      = g.Count(),
                    LastIngested = g.Max(a => a.IngestedAt),
                })
                .ToListAsync(ct);

            var lastSeenAll = await db.Articles
                .AsNoTracking()
                .GroupBy(a => a.Source)
                .Select(g => new { Source = g.Key, LastIngested = g.Max(a => a.IngestedAt) })
                .ToListAsync(ct);

            var lastSeenMap = lastSeenAll.ToDictionary(r => r.Source, r => r.LastIngested);
            var windowMap   = rows.ToDictionary(r => r.Source);

            var pollRows = await db.SourceCursorStates
                .AsNoTracking()
                .Where(s => s.SourceName == NewsSourcePollHandler.CursorSourceName)
                .Select(s => new
                {
                    s.SourceKey,
                    s.LastStartedAt,
                    s.LastSucceededAt,
                    s.LastFailedAt,
                    s.ConsecutiveFailures,
                    s.NextEligibleRunAt,
                    s.LastError,
                })
                .ToListAsync(ct);

            var pollMap = pollRows.ToDictionary(r => r.SourceKey, StringComparer.OrdinalIgnoreCase);

            var result = AllSources.Select(source =>
            {
                var (tier, weight) = SourceReputation.For(source);
                var inWindow = windowMap.TryGetValue(source, out var w) ? w : null;
                lastSeenMap.TryGetValue(source, out var lastSeen);
                pollMap.TryGetValue(source, out var poll);

                var lastIngestedAt = inWindow?.LastIngested ?? (lastSeen == default ? (DateTime?)null : lastSeen);

                var articleStatus = ArticleStatus(lastIngestedAt, cutoff24h, cutoff7d);
                var currentFailure = poll is not null &&
                    poll.ConsecutiveFailures > 0 &&
                    poll.LastFailedAt is not null &&
                    (poll.LastSucceededAt is null || poll.LastFailedAt > poll.LastSucceededAt);

                var status = articleStatus;
                if (currentFailure)
                    status = "degraded";
                else if (lastIngestedAt is not null && poll?.LastSucceededAt >= cutoff24h)
                    status = "healthy";
                else if (lastIngestedAt is not null && poll?.LastSucceededAt >= cutoff7d && status == "silent")
                    status = "stale";

                DateTime? lastPolledAt = null;
                if (poll is not null)
                {
                    var pollDates = new[]
                    {
                        poll.LastStartedAt,
                        poll.LastSucceededAt,
                        poll.LastFailedAt,
                    }.Where(d => d.HasValue).Select(d => d!.Value).ToList();
                    if (pollDates.Count > 0) lastPolledAt = pollDates.Max();
                }

                string? lastError = null;
                if (currentFailure)
                    lastError = poll?.LastError;
                else
                    lastError = null;

                return new
                {
                    name           = source,
                    tier,
                    weight,
                    count1h        = inWindow?.Count1h  ?? 0,
                    count24h       = inWindow?.Count24h ?? 0,
                    count7d        = inWindow?.Count7d  ?? 0,
                    lastIngestedAt,
                    lastPolledAt,
                    lastStartedAt = poll?.LastStartedAt,
                    lastSucceededAt = poll?.LastSucceededAt,
                    lastFailedAt = poll?.LastFailedAt,
                    nextEligibleRunAt = poll?.NextEligibleRunAt,
                    consecutiveFailures = poll?.ConsecutiveFailures ?? 0,
                    lastError,
                    articleStatus,
                    status,
                };
            }).ToList();

            var summary = new
            {
                healthy = result.Count(r => r.status == "healthy"),
                degraded = result.Count(r => r.status == "degraded"),
                stale   = result.Count(r => r.status == "stale"),
                silent  = result.Count(r => r.status == "silent"),
                total24h = result.Sum(r => r.count24h),
            };

            return Results.Ok(new { summary, sources = result });
        });
    }

    private static string ArticleStatus(DateTime? lastIngestedAt, DateTime cutoff24h, DateTime cutoff7d)
    {
        if (lastIngestedAt is null) return "silent";
        if (lastIngestedAt >= cutoff24h) return "healthy";
        if (lastIngestedAt >= cutoff7d) return "stale";
        return "silent";
    }
}
