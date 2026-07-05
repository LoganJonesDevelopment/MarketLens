using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record KillCriterionEscalationItem(
    int Id,
    string Scenario,
    string ThreatLevel,
    int ContradictingEvidenceCount,
    decimal Score,
    string? LastTriggeredReason);

public sealed record KillCriterionEscalationSummary(
    Guid ThesisId,
    IReadOnlyList<KillCriterionEscalationItem> Items);

public sealed class ThesisKillCriterionEscalator(
    MarketLensDbContext db,
    ILogger<ThesisKillCriterionEscalator> logger)
{
    public async Task<KillCriterionEscalationSummary> ReconcileAsync(
        Guid thesisId,
        CancellationToken cancellationToken = default)
    {
        var criteria = await db.ThesisKillCriteria
            .Where(c => c.ThesisId == thesisId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        if (criteria.Count == 0)
            return new KillCriterionEscalationSummary(thesisId, []);

        var evidence = await db.ResearchEvidence
            .AsNoTracking()
            .Include(e => e.Article)
            .Include(e => e.Cluster)
            .ThenInclude(c => c!.Event)
            .Include(e => e.TranscriptSegment)
            .Include(e => e.ArticleChunk)
            .ThenInclude(c => c!.Article)
            .Where(e =>
                e.ThesisId == thesisId &&
                e.Stance == "contradicts" &&
                e.ReviewStatus != "rejected" &&
                (e.StanceConfidence == null || e.StanceConfidence >= 0.65m))
            .ToListAsync(cancellationToken);

        var signals = evidence.Select(ToSignal).ToArray();
        var changed = false;
        var now = DateTime.UtcNow;
        var items = new List<KillCriterionEscalationItem>();

        foreach (var criterion in criteria)
        {
            var previousThreat = criterion.ThreatLevel;
            var evaluation = ThesisKillCriterionEvaluator.Evaluate(
                criterion.Scenario,
                criterion.MonitoringKeywords,
                signals,
                now);

            if (!string.Equals(criterion.ThreatLevel, evaluation.ThreatLevel, StringComparison.Ordinal) ||
                criterion.ContradictingEvidenceCount != evaluation.ContradictingEvidenceCount ||
                !string.Equals(criterion.LastTriggeredReason, evaluation.LastTriggeredReason, StringComparison.Ordinal))
            {
                criterion.ThreatLevel = evaluation.ThreatLevel;
                criterion.ContradictingEvidenceCount = evaluation.ContradictingEvidenceCount;
                criterion.LastTriggeredReason = evaluation.LastTriggeredReason;
                if (evaluation.ThreatLevel != "dormant" &&
                    ThreatRank(evaluation.ThreatLevel) >= ThreatRank(previousThreat))
                    criterion.LastEscalatedAt = now;
                changed = true;
            }

            items.Add(new KillCriterionEscalationItem(
                criterion.Id,
                criterion.Scenario,
                evaluation.ThreatLevel,
                evaluation.ContradictingEvidenceCount,
                evaluation.Score,
                evaluation.LastTriggeredReason));
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Reconciled kill criteria for thesis {ThesisId}", thesisId);
        }

        return new KillCriterionEscalationSummary(thesisId, items);
    }

    private static KillEvidenceSignal ToSignal(ResearchEvidence evidence)
        => new(
            Text: BuildEvidenceText(evidence),
            SourceTier: SourceTier(evidence),
            StanceConfidence: evidence.StanceConfidence,
            IsPinned: evidence.IsPinned,
            MatchedAt: evidence.MatchedAt);

    private static string? SourceTier(ResearchEvidence evidence)
        => evidence.Cluster?.DominantSourceTier
            ?? evidence.Article?.SourceTier
            ?? evidence.ArticleChunk?.Article?.SourceTier
            ?? (evidence.TranscriptSegment is null ? null : "primary");

    private static string BuildEvidenceText(ResearchEvidence evidence)
        => string.Join('\n',
            evidence.MatchReason,
            evidence.StanceRationale,
            evidence.ReviewerNote,
            evidence.Article?.Symbol,
            evidence.Article?.Source,
            evidence.Article?.Headline,
            evidence.Cluster?.Symbol,
            evidence.Cluster?.Event?.EventType,
            evidence.Cluster?.Event?.Summary,
            evidence.TranscriptSegment?.Text,
            evidence.ArticleChunk?.Article?.Symbol,
            evidence.ArticleChunk?.Article?.Headline);

    private static int ThreatRank(string? threatLevel) => threatLevel switch
    {
        "critical" => 3,
        "elevated" => 2,
        "watching" => 1,
        _ => 0,
    };
}
