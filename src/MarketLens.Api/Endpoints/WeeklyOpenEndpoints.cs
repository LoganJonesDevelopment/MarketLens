using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class WeeklyOpenEndpoints
{
    public static void MapWeeklyOpenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/weekly-open", async (
            MarketLensDbContext db,
            string? window,
            DateTime? from,
            int? topClusters,
            int? calendarDays,
            CancellationToken ct) =>
        {
            var nowUtc = DateTime.UtcNow;
            var presetInput = string.IsNullOrWhiteSpace(window) ? "auto" : window.Trim().ToLowerInvariant();
            var (windowStart, presetResolved, kind) = ResolveWindow(presetInput, NormalizeUtc(from), nowUtc);
            var clustersTake = Math.Clamp(topClusters ?? 10, 1, 50);
            var lookbackHours = Math.Max(1.0, (nowUtc - windowStart).TotalHours);
            var lookbackDays = Math.Max(1, (int)Math.Ceiling(lookbackHours / 24.0));
            var calendarHorizonDays = Math.Clamp(
                calendarDays ?? Math.Min(14, Math.Max(2, lookbackDays)),
                1, 14);

            var quoteRows = await db.MarketQuotes
                .AsNoTracking()
                .OrderBy(q => q.Symbol)
                .Select(q => new
                {
                    q.Provider,
                    q.Symbol,
                    q.DisplayName,
                    q.InstrumentType,
                    q.Exchange,
                    q.Currency,
                    q.Last,
                    q.PreviousClose,
                    q.Change,
                    q.ChangePercent,
                    q.AsOf,
                    q.IngestedAt,
                    q.Status,
                    q.Error,
                })
                .ToListAsync(ct);

            var quotes = quoteRows
                .Select(q =>
                {
                    var (provider, delayed) = MarketOverviewEndpoints.ResolveServing(q.Status, q.Provider, q.AsOf, nowUtc);
                    return new
                    {
                        provider,
                        q.Symbol,
                        q.DisplayName,
                        q.InstrumentType,
                        q.Exchange,
                        q.Currency,
                        q.Last,
                        q.PreviousClose,
                        q.Change,
                        q.ChangePercent,
                        q.AsOf,
                        q.IngestedAt,
                        q.Status,
                        q.Error,
                        delayed,
                    };
                })
                .ToList();

            var candidatePool = Math.Max(40, clustersTake * 4);
            var clusterCandidates = await db.Events
                .AsNoTracking()
                .Include(e => e.Cluster)
                .ThenInclude(c => c!.Articles)
                .Where(e => e.Cluster!.Articles.Any(a => a.PublishedAt >= windowStart))
                .OrderByDescending(e => e.Importance)
                .Take(candidatePool)
                .Select(e => new
                {
                    clusterId = e.ClusterId,
                    symbol = e.Cluster!.Symbol,
                    eventType = e.EventType,
                    summary = e.Summary,
                    importance = e.Importance,
                    sentiment = e.Sentiment,
                    components = new
                    {
                        sourceWeight = e.SourceWeight,
                        noveltyWeight = e.NoveltyWeight,
                        eventClassPrior = e.EventClassPrior,
                        magnitudeSignal = e.MagnitudeSignal,
                    },
                    sourceTier = e.Cluster.DominantSourceTier,
                    memberCount = e.Cluster.MemberCount,
                    articlesInWindow = e.Cluster.Articles.Count(a => a.PublishedAt >= windowStart),
                    firstSeenAt = e.Cluster.FirstSeenAt,
                    lastSeenAt = e.Cluster.Articles.Max(a => a.PublishedAt),
                    topSource = e.Cluster.Articles
                        .Where(a => a.PublishedAt >= windowStart)
                        .OrderByDescending(a => a.SourceTier == "primary")
                        .ThenByDescending(a => a.SourceTier == "wire")
                        .ThenByDescending(a => a.PublishedAt)
                        .Select(a => new { a.Source, a.SourceTier, a.Publisher, a.Headline, a.Url, a.PublishedAt })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var clusters = clusterCandidates
                .Select(c =>
                {
                    var ratio = c.memberCount > 0 ? (decimal)c.articlesInWindow / c.memberCount : 1m;
                    var firstSeenInWindow = c.firstSeenAt >= windowStart;
                    return new
                    {
                        c.clusterId,
                        c.symbol,
                        c.eventType,
                        c.summary,
                        c.importance,
                        c.sentiment,
                        c.components,
                        c.sourceTier,
                        c.memberCount,
                        c.articlesInWindow,
                        activityRatio = ratio,
                        firstSeenInWindow,
                        weightedImportance = c.importance * ratio,
                        c.firstSeenAt,
                        c.lastSeenAt,
                        c.topSource,
                    };
                })
                .OrderByDescending(c => c.weightedImportance)
                .ThenByDescending(c => c.lastSeenAt)
                .Take(clustersTake)
                .ToList();

            var theses = await db.ResearchTheses
                .AsNoTracking()
                .Where(t => t.Status == "active" || t.Status == "watching" || t.Status == "exploration")
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Status,
                    primarySymbol = t.ThesisAssets
                        .Where(ta => ta.Asset!.Symbol != null)
                        .OrderBy(ta => ta.Role == "subject" ? 0 : 1)
                        .Select(ta => ta.Asset!.Symbol)
                        .FirstOrDefault(),
                    newSupports = t.Evidence.Count(e => e.MatchedAt >= windowStart && e.Stance == "supports"),
                    newContradicts = t.Evidence.Count(e => e.MatchedAt >= windowStart && e.Stance == "contradicts"),
                    newNeutral = t.Evidence.Count(e => e.MatchedAt >= windowStart && e.Stance == "neutral"),
                    newUnknown = t.Evidence.Count(e => e.MatchedAt >= windowStart && (e.Stance == "unknown" || e.Stance == null)),
                    newPinned = t.Evidence.Count(e => e.MatchedAt >= windowStart && e.IsPinned),
                    newTotal = t.Evidence.Count(e => e.MatchedAt >= windowStart),
                    priorSupports = t.Evidence.Count(e => e.MatchedAt < windowStart && e.Stance == "supports"),
                    priorContradicts = t.Evidence.Count(e => e.MatchedAt < windowStart && e.Stance == "contradicts"),
                    topNew = t.Evidence
                        .Where(e => e.MatchedAt >= windowStart)
                        .OrderByDescending(e => e.IsPinned)
                        .ThenByDescending(e => e.Cluster != null && e.Cluster.Event != null ? e.Cluster.Event.Importance : 0m)
                        .ThenByDescending(e => e.MatchedAt)
                        .Select(e => new
                        {
                            evidenceId = e.Id,
                            stance = e.Stance,
                            stanceConfidence = e.StanceConfidence,
                            stanceRationale = e.StanceRationale,
                            isPinned = e.IsPinned,
                            matchedAt = e.MatchedAt,
                            clusterId = e.ClusterId,
                            articleId = e.ArticleId,
                            transcriptSegmentId = e.TranscriptSegmentId,
                            articleChunkId = e.ArticleChunkId,
                            headline = e.Article != null
                                ? e.Article.Headline
                                : (e.Cluster != null && e.Cluster.Event != null
                                    ? e.Cluster.Event.Summary
                                    : (e.ArticleChunk != null && e.ArticleChunk.Article != null
                                        ? e.ArticleChunk.Article.Headline
                                        : null)),
                            symbol = e.Article != null
                                ? e.Article.Symbol
                                : (e.Cluster != null ? e.Cluster.Symbol : null),
                            importance = e.Cluster != null && e.Cluster.Event != null ? (decimal?)e.Cluster.Event.Importance : null,
                            sourceTier = e.Cluster != null
                                ? e.Cluster.DominantSourceTier
                                : (e.Article != null ? e.Article.SourceTier : null),
                        })
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            var theseSorted = theses
                .Where(t => t.newTotal > 0)
                .Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.Status,
                    t.primarySymbol,
                    t.newSupports,
                    t.newContradicts,
                    t.newNeutral,
                    t.newUnknown,
                    t.newPinned,
                    t.newTotal,
                    leanDelta = t.newSupports - t.newContradicts,
                    priorLean = t.priorSupports - t.priorContradicts,
                    t.topNew,
                })
                .OrderByDescending(t => Math.Abs(t.leanDelta))
                .ThenByDescending(t => t.newPinned)
                .ThenByDescending(t => t.newSupports + t.newContradicts)
                .ThenByDescending(t => t.newTotal)
                .ToList();

            var calendarFrom = nowUtc.AddDays(-1);
            var calendarTo = nowUtc.AddDays(calendarHorizonDays);
            var calendar = await db.EconomicEvents
                .AsNoTracking()
                .Where(e => e.ScheduledAt >= calendarFrom && e.ScheduledAt <= calendarTo)
                .OrderBy(e => e.ScheduledAt)
                .Take(50)
                .Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.Symbol,
                    e.Label,
                    e.ScheduledAt,
                    e.IsTimeSpecific,
                    e.Status,
                    e.Source,
                    e.Notes,
                })
                .ToListAsync(ct);

            var pipelineFreshness = await db.PipelineRuns
                .AsNoTracking()
                .GroupBy(r => r.Stage)
                .Select(g => new
                {
                    stage = g.Key,
                    latestStartedAt = g.Max(r => r.StartedAt),
                    latestStatus = g
                        .OrderByDescending(r => r.StartedAt)
                        .Select(r => r.Status)
                        .FirstOrDefault(),
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                generatedAt = nowUtc,
                windowStart,
                windowEnd = nowUtc,
                windowPreset = presetResolved,
                windowKind = kind,
                lookbackHours,
                lookbackDays,
                calendarHorizonDays,
                sinceUtc = windowStart,
                quotes,
                clusters,
                theses = theseSorted,
                calendar,
                pipelineFreshness,
            });
        });
    }

    private static DateTime? NormalizeUtc(DateTime? value) => value is null
        ? null
        : value.Value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };

    private static (DateTime windowStart, string preset, string kind) ResolveWindow(
        string preset, DateTime? customFrom, DateTime nowUtc)
    {
        switch (preset)
        {
            case "1d":
            case "24h":
                return (nowUtc.AddHours(-24), "1d", "duration");
            case "3d":
            case "72h":
                return (nowUtc.AddDays(-3), "3d", "duration");
            case "1w":
            case "7d":
                return (nowUtc.AddDays(-7), "1w", "duration");
            case "1m":
            case "30d":
                return (nowUtc.AddDays(-30), "1m", "duration");
            case "since-friday":
            case "since-friday-close":
                return (PreviousFridayClose(nowUtc), "since-friday", "session-close");
            case "since-prior-close":
                return (PriorSessionClose(nowUtc), "since-prior-close", "session-close");
            case "custom":
                if (customFrom.HasValue && customFrom.Value < nowUtc)
                    return (customFrom.Value, "custom", "custom");
                return (PriorSessionClose(nowUtc), "auto", "session-close");
            case "auto":
            default:
                return (AutoWindowStart(nowUtc), "auto", "session-close");
        }
    }

    private static DateTime AutoWindowStart(DateTime nowUtc)
    {
        var dow = nowUtc.DayOfWeek;
        if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday || dow == DayOfWeek.Monday)
            return PreviousFridayClose(nowUtc);
        return PriorSessionClose(nowUtc);
    }

    private static DateTime PriorSessionClose(DateTime nowUtc)
    {
        for (var i = 0; i < 14; i++)
        {
            var d = nowUtc.Date.AddDays(-i);
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) continue;
            var close = DateTime.SpecifyKind(d.AddHours(21), DateTimeKind.Utc);
            if (close <= nowUtc) return close;
        }
        return DateTime.SpecifyKind(nowUtc.AddDays(-1), DateTimeKind.Utc);
    }

    private static DateTime PreviousFridayClose(DateTime nowUtc)
    {
        for (var i = 0; i < 8; i++)
        {
            var candidate = nowUtc.Date.AddDays(-i);
            if (candidate.DayOfWeek != DayOfWeek.Friday) continue;
            var close = DateTime.SpecifyKind(candidate.AddHours(21), DateTimeKind.Utc);
            if (close <= nowUtc) return close;
        }
        return DateTime.SpecifyKind(nowUtc.AddDays(-3), DateTimeKind.Utc);
    }
}
