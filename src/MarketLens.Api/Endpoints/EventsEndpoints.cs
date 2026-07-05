using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class EventsEndpoints
{
    public static void MapEventsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (
            MarketLensDbContext db,
            string? symbol,
            string? eventType,
            decimal? minImportance,
            int? take,
            DateTime? from,
            DateTime? to,
            CancellationToken ct) =>
        {
            var q = db.Events
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                q = q.Where(e => e.Cluster!.Symbol == s);
            }
            if (!string.IsNullOrWhiteSpace(eventType))
                q = q.Where(e => e.EventType == eventType);
            if (minImportance.HasValue)
                q = q.Where(e => e.Importance >= minImportance.Value);
            if (from.HasValue)
                q = q.Where(e => e.ExtractedAt >= from.Value);
            if (to.HasValue)
                q = q.Where(e => e.ExtractedAt <= to.Value);

            var limit = Math.Clamp(take ?? 50, 1, 500);

            var items = await q
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.ExtractedAt)
                .Take(limit)
                .Select(e => new
                {
                    clusterId = e.ClusterId,
                    eventType = e.EventType,
                    symbol = e.Cluster!.Symbol,
                    summary = e.Summary,
                    sentiment = e.Sentiment,
                    importance = e.Importance,
                    components = new
                    {
                        sourceWeight = e.SourceWeight,
                        noveltyWeight = e.NoveltyWeight,
                        eventClassPrior = e.EventClassPrior,
                        magnitudeSignal = e.MagnitudeSignal,
                    },
                    slots = e.Slots,
                    cluster = new
                    {
                        memberCount = e.Cluster.MemberCount,
                        dominantSourceTier = e.Cluster.DominantSourceTier,
                        firstSeenAt = e.Cluster.FirstSeenAt,
                        lastSeenAt = e.Cluster.LastSeenAt,
                        triageConfidence = e.Cluster.TriageConfidence,
                    },
                    members = e.Cluster.Articles.Select(a => new
                    {
                        a.Source, a.SourceTier, a.Publisher, a.Headline, a.Url, a.PublishedAt,
                    }),
                    model = new { e.ModelName, e.PromptVersion, e.ExtractedAt },
                    market = e.MarketSnapshots
                        .OrderByDescending(s => s.CapturedAt)
                        .Select(s => new
                        {
                            s.Symbol,
                            s.Status,
                            s.CapturedAt,
                            s.QuoteTime,
                            s.LastPrice,
                            s.PreviousClose,
                            s.MovePercent,
                            s.BenchmarkSymbol,
                            s.BenchmarkMovePercent,
                            s.RelativeMovePercent,
                            s.RelativeVolume,
                            s.ReactionScore,
                            s.IsAfterHours,
                            s.IsStale,
                        })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        app.MapGet("/api/clusters/{id:guid}", async (MarketLensDbContext db, Guid id, CancellationToken ct) =>
        {
            var cluster = await db.Clusters
                .AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new
                {
                    c.Id,
                    c.Symbol,
                    c.MemberCount,
                    c.DominantSourceTier,
                    c.TopSourceWeight,
                    c.TriageEventType,
                    c.TriageConfidence,
                    c.FirstSeenAt,
                    c.LastSeenAt,
                    members = c.Articles
                        .OrderByDescending(a => a.PublishedAt)
                        .Select(a => new
                        {
                            a.Id,
                            a.Source,
                            a.SourceTier,
                            a.Symbol,
                            a.Headline,
                            a.Summary,
                            a.Url,
                            a.Publisher,
                            a.PublishedAt,
                        }),
                    extractedEvent = c.Event == null ? null : new
                    {
                        c.Event.EventType,
                        c.Event.Summary,
                        c.Event.Sentiment,
                        c.Event.Importance,
                        components = new
                        {
                            sourceWeight = c.Event.SourceWeight,
                            noveltyWeight = c.Event.NoveltyWeight,
                            eventClassPrior = c.Event.EventClassPrior,
                            magnitudeSignal = c.Event.MagnitudeSignal,
                        },
                        slots = c.Event.Slots,
                        model = new
                        {
                            c.Event.ModelName,
                            c.Event.PromptVersion,
                            c.Event.ExtractedAt,
                        },
                        market = c.Event.MarketSnapshots
                            .OrderByDescending(s => s.CapturedAt)
                            .Select(s => new
                            {
                                s.Symbol,
                                s.Status,
                                s.CapturedAt,
                                s.QuoteTime,
                                s.LastPrice,
                                s.PreviousClose,
                                s.MovePercent,
                                s.BenchmarkSymbol,
                                s.BenchmarkMovePercent,
                                s.RelativeMovePercent,
                                s.RelativeVolume,
                                s.ReactionScore,
                                s.IsAfterHours,
                                s.IsStale,
                            })
                            .FirstOrDefault(),
                    },
                })
                .FirstOrDefaultAsync(ct);

            return cluster is null ? Results.NotFound() : Results.Ok(cluster);
        });

        app.MapGet("/api/clusters/pending", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var pending = await db.Clusters
                .AsNoTracking()
                .Where(c => c.Event == null)
                .OrderByDescending(c => c.LastSeenAt)
                .Take(50)
                .Select(c => new
                {
                    c.Id, c.Symbol, c.MemberCount, c.DominantSourceTier,
                    c.TriageEventType, c.TriageConfidence,
                    c.FirstSeenAt, c.LastSeenAt,
                })
                .ToListAsync(ct);
            return Results.Ok(pending);
        });

        app.MapGet("/api/stats", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var articles = await db.Articles.CountAsync(ct);
            var clusters = await db.Clusters.CountAsync(ct);
            var events = await db.Events.CountAsync(ct);
            var triaged = await db.Clusters.CountAsync(c => c.TriageEventType != null, ct);
            var suppressions = await db.Suppressions.CountAsync(ct);
            var marketSnapshots = await db.MarketSnapshots.CountAsync(ct);

            var byType = await db.Events
                .GroupBy(e => e.EventType)
                .Select(g => new
                {
                    eventType = g.Key,
                    count = g.Count(),
                    avgImportance = g.Average(e => e.Importance),
                    avgSentiment = g.Average(e => e.Sentiment),
                })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var bySource = await db.Articles
                .GroupBy(a => a.Source)
                .Select(g => new { source = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                articles, clusters, events, triaged,
                suppressions,
                marketSnapshots,
                pendingExtraction = triaged - events,
                clusterCompressionRatio = articles == 0 ? 0 : (double)clusters / articles,
                byEventType = byType,
                bySource,
            });
        });

        app.MapGet("/api/suppressions", async (
            MarketLensDbContext db,
            string? reason,
            string? stage,
            string? symbol,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.Suppressions.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(reason))
                q = q.Where(s => s.Reason == reason);
            if (!string.IsNullOrWhiteSpace(stage))
                q = q.Where(s => s.Stage == stage);
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                q = q.Where(x => x.Symbol == s);
            }

            var limit = Math.Clamp(take ?? 100, 1, 500);
            var items = await q
                .OrderByDescending(s => s.SuppressedAt)
                .Take(limit)
                .Select(s => new
                {
                    s.Id,
                    s.Stage,
                    s.Reason,
                    s.Symbol,
                    s.Source,
                    s.EventType,
                    s.Confidence,
                    s.Headline,
                    s.Summary,
                    s.Url,
                    s.Publisher,
                    s.PublishedAt,
                    s.SuppressedAt,
                    s.ClusterId,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        app.MapGet("/api/suppressions/summary", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var byReason = await db.Suppressions
                .AsNoTracking()
                .GroupBy(s => new { s.Stage, s.Reason })
                .Select(g => new
                {
                    g.Key.Stage,
                    g.Key.Reason,
                    count = g.Count(),
                    latest = g.Max(s => s.SuppressedAt),
                })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            var bySymbol = await db.Suppressions
                .AsNoTracking()
                .Where(s => s.Symbol != null)
                .GroupBy(s => s.Symbol)
                .Select(g => new { symbol = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .ToListAsync(ct);

            return Results.Ok(new { byReason, bySymbol });
        });

        app.MapGet("/api/inbox", async (
            MarketLensDbContext db,
            string? symbol,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.Events
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                q = q.Where(e => e.Cluster!.Symbol == s);
            }

            var limit = Math.Clamp(take ?? 50, 1, 100);
            var now = DateTime.UtcNow;

            var items = await q
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.Cluster!.LastSeenAt)
                .Take(limit)
                .Select(e => new
                {
                    id = e.ClusterId,
                    symbol = e.Cluster!.Symbol,
                    eventType = e.EventType,
                    headline = e.Summary,
                    importance = e.Importance,
                    sentiment = e.Sentiment,
                    firstSeenAt = e.Cluster.FirstSeenAt,
                    lastSeenAt = e.Cluster.LastSeenAt,
                    ageMinutes = (int)(now - e.Cluster.LastSeenAt).TotalMinutes,
                    confidence = e.Cluster.TriageConfidence,
                    sourceTier = e.Cluster.DominantSourceTier,
                    sourceCount = e.Cluster.MemberCount,
                    components = new
                    {
                        source = e.SourceWeight,
                        novelty = e.NoveltyWeight,
                        eventClass = e.EventClassPrior,
                        magnitude = e.MagnitudeSignal,
                    },
                    evidence = e.Cluster.Articles
                        .OrderByDescending(a => a.SourceTier == "primary")
                        .ThenByDescending(a => a.SourceTier == "wire")
                        .ThenBy(a => a.PublishedAt)
                        .Take(4)
                        .Select(a => new
                        {
                            source = a.Source,
                            tier = a.SourceTier,
                            publisher = a.Publisher,
                            headline = a.Headline,
                            url = a.Url,
                            publishedAt = a.PublishedAt,
                        }),
                    market = e.MarketSnapshots
                        .OrderByDescending(s => s.CapturedAt)
                        .Select(s => new
                        {
                            s.Status,
                            s.CapturedAt,
                            s.QuoteTime,
                            s.LastPrice,
                            s.MovePercent,
                            s.BenchmarkSymbol,
                            s.BenchmarkMovePercent,
                            s.RelativeMovePercent,
                            s.RelativeVolume,
                            s.ReactionScore,
                            s.IsAfterHours,
                            s.IsStale,
                        })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        app.MapGet("/api/market-snapshots", async (
            MarketLensDbContext db,
            string? symbol,
            Guid? clusterId,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.MarketSnapshots.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                q = q.Where(x => x.Symbol == s);
            }

            if (clusterId.HasValue)
                q = q.Where(x => x.ClusterId == clusterId.Value);

            var limit = Math.Clamp(take ?? 100, 1, 500);
            var items = await q
                .OrderByDescending(s => s.CapturedAt)
                .Take(limit)
                .Select(s => new
                {
                    s.Id,
                    s.ClusterId,
                    s.Symbol,
                    s.Provider,
                    s.Status,
                    s.CapturedAt,
                    s.QuoteTime,
                    s.LastPrice,
                    s.PreviousClose,
                    s.OpenPrice,
                    s.HighPrice,
                    s.LowPrice,
                    s.MovePercent,
                    s.BenchmarkSymbol,
                    s.BenchmarkMovePercent,
                    s.RelativeMovePercent,
                    s.Volume,
                    s.AverageVolume,
                    s.RelativeVolume,
                    s.ReactionScore,
                    s.IsAfterHours,
                    s.IsStale,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });
    }
}
