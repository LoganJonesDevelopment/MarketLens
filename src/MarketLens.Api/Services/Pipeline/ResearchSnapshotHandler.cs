using System.Text.Json;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record ResearchSnapshotResult(bool Processed, bool Written, bool Current);

public sealed class ResearchSnapshotHandler(
    MarketLensDbContext db,
    ILogger<ResearchSnapshotHandler> logger,
    ThesisKillCriterionEscalator killCriteriaEscalator)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] TrackedStatuses = ["active", "watching", "exploration"];

    public async Task<ResearchSnapshotResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(naturalKey, out var thesisId))
            return new ResearchSnapshotResult(false, false, false);

        var payload = ParsePayload(payloadJson);
        var thesis = await db.ResearchTheses
            .AsNoTracking()
            .Where(t => t.Id == thesisId)
            .Select(t => new { t.Id, t.Status, t.PositionIntent })
            .FirstOrDefaultAsync(cancellationToken);

        if (thesis is null || !TrackedStatuses.Contains(thesis.Status))
            return new ResearchSnapshotResult(false, false, false);

        var minGap = DateTime.UtcNow.AddHours(-Math.Max(1, payload.MinHoursBetweenSnapshots));
        var exists = await db.ResearchSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.ThesisId == thesisId && s.SnapshotAt >= minGap, cancellationToken);

        if (exists)
            return new ResearchSnapshotResult(false, false, true);

        var snapshot = await ComputeAsync(thesis.Id, thesis.PositionIntent, cancellationToken);
        db.ResearchSnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Wrote research snapshot for thesis {ThesisId}", thesisId);
        return new ResearchSnapshotResult(true, true, true);
    }

    private async Task<ResearchSnapshot> ComputeAsync(
        Guid thesisId,
        string positionIntent,
        CancellationToken cancellationToken)
    {
        var rows = await db.ResearchEvidence
            .AsNoTracking()
            .Where(e => e.ThesisId == thesisId)
            .Select(e => new
            {
                e.Stance,
                e.ReviewStatus,
                e.IsPinned,
                e.MatchedAt,
                Tier = e.Cluster != null
                    ? e.Cluster.DominantSourceTier
                    : (e.Article != null
                        ? e.Article.SourceTier
                        : (e.ArticleChunk != null && e.ArticleChunk.Article != null
                            ? e.ArticleChunk.Article.SourceTier
                            : null)),
            })
            .ToListAsync(cancellationToken);

        var supports = rows.Count(r => r.Stance == "supports");
        var contradicts = rows.Count(r => r.Stance == "contradicts");
        var neutral = rows.Count(r => r.Stance == "neutral");
        var unknown = rows.Count(r => r.Stance == "unknown" || string.IsNullOrEmpty(r.Stance));
        var pinned = rows.Count(r => r.IsPinned);
        var pending = rows.Count(r => r.ReviewStatus == "pending");
        var accepted = rows.Count(r => r.ReviewStatus == "accepted");
        var rejected = rows.Count(r => r.ReviewStatus == "rejected");
        var latest = rows.Count == 0 ? (DateTime?)null : rows.Max(r => r.MatchedAt);

        decimal weighted = 0m;
        decimal weightedDenom = 0m;
        var byTier = new Dictionary<string, (int supports, int contradicts, int neutral)>();
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.Stance) || r.Stance == "unknown") continue;
            var weight = TierWeight(r.Tier);
            var sign = r.Stance switch { "supports" => 1m, "contradicts" => -1m, _ => 0m };
            weighted += weight * sign;
            weightedDenom += weight;

            var tierKey = r.Tier ?? "unknown";
            if (!byTier.TryGetValue(tierKey, out var agg)) agg = (0, 0, 0);
            byTier[tierKey] = r.Stance switch
            {
                "supports" => (agg.supports + 1, agg.contradicts, agg.neutral),
                "contradicts" => (agg.supports, agg.contradicts + 1, agg.neutral),
                _ => (agg.supports, agg.contradicts, agg.neutral + 1),
            };
        }

        var conviction = weightedDenom > 0 ? weighted / weightedDenom : 0m;
        var killCriteria = await killCriteriaEscalator.ReconcileAsync(thesisId, cancellationToken);

        var summaryObj = new
        {
            evidence = new
            {
                total = rows.Count,
                supports,
                contradicts,
                neutral,
                unknown,
                pinned,
                pending,
                accepted,
                rejected,
            },
            convictionScore = Math.Round(conviction, 4),
            convictionLabel = conviction switch
            {
                > 0.4m => "supports",
                > 0.1m => "leans_supports",
                < -0.4m => "contradicts",
                < -0.1m => "leans_contradicts",
                _ => "neutral",
            },
            byTier = byTier.Select(kv => new
            {
                tier = kv.Key,
                supports = kv.Value.supports,
                contradicts = kv.Value.contradicts,
                neutral = kv.Value.neutral,
            }),
            killCriteria = new
            {
                total = killCriteria.Items.Count,
                active = killCriteria.Items.Count(i => i.ThreatLevel != "dormant"),
                critical = killCriteria.Items.Count(i => i.ThreatLevel == "critical"),
                elevated = killCriteria.Items.Count(i => i.ThreatLevel == "elevated"),
                watching = killCriteria.Items.Count(i => i.ThreatLevel == "watching"),
                items = killCriteria.Items,
            },
            positionIntent,
        };

        return new ResearchSnapshot
        {
            Id = Guid.NewGuid(),
            ThesisId = thesisId,
            SnapshotAt = DateTime.UtcNow,
            EvidenceCount = rows.Count,
            LatestEvidenceAt = latest,
            Summary = JsonSerializer.Serialize(summaryObj, JsonOptions),
        };
    }

    private static ResearchSnapshotPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new ResearchSnapshotPayload();

        try
        {
            return JsonSerializer.Deserialize<ResearchSnapshotPayload>(payloadJson, JsonOptions)
                ?? new ResearchSnapshotPayload();
        }
        catch (JsonException)
        {
            return new ResearchSnapshotPayload();
        }
    }

    private static decimal TierWeight(string? tier) => tier switch
    {
        "primary" => 1.00m,
        "wire" => 0.85m,
        "industry_analyst" => 0.75m,
        "trade_press" => 0.55m,
        "aggregator" => 0.30m,
        _ => 0.40m,
    };

    private sealed class ResearchSnapshotPayload
    {
        public int MinHoursBetweenSnapshots { get; set; } = 20;
    }
}
