using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record StanceClassificationItemResult(Guid EvidenceId, bool Processed);

public class StanceClassificationHandler(
    MarketLensDbContext db,
    IStanceClassifier classifier,
    ILogger<StanceClassificationHandler> logger,
    ThesisKillCriterionEscalator killCriteriaEscalator)
{
    public async Task<StanceClassificationItemResult> ProcessAsync(
        Guid evidenceId,
        CancellationToken cancellationToken)
    {
        var evidence = await db.ResearchEvidence
            .Include(e => e.Thesis)
            .Include(e => e.Cluster)
            .ThenInclude(c => c!.Articles)
            .Include(e => e.Cluster)
            .ThenInclude(c => c!.Event)
            .Include(e => e.Article)
            .Include(e => e.TranscriptSegment)
            .ThenInclude(s => s!.Transcript)
            .Include(e => e.ArticleChunk)
            .ThenInclude(c => c!.Article)
            .SingleOrDefaultAsync(e => e.Id == evidenceId, cancellationToken);

        if (evidence is null)
            return new StanceClassificationItemResult(evidenceId, Processed: false);
        if (evidence.ClassifiedAt is not null || evidence.Thesis is null)
            return new StanceClassificationItemResult(evidenceId, Processed: false);

        var context = BuildContext(evidence);
        if (context is null)
        {
            evidence.Stance = "unknown";
            evidence.StanceConfidence = 0m;
            evidence.StanceRationale = "insufficient cluster context for classification";
            evidence.ClassifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return new StanceClassificationItemResult(evidenceId, Processed: true);
        }

        var verdict = await classifier.ClassifyAsync(context, cancellationToken);

        evidence.Stance = verdict.Stance;
        evidence.StanceConfidence = verdict.Confidence;
        evidence.StanceRationale = verdict.Rationale;
        evidence.StanceModel = verdict.ModelName;
        evidence.StancePromptVersion = verdict.PromptVersion;
        evidence.ClassifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        if (evidence.Stance == "contradicts")
            await killCriteriaEscalator.ReconcileAsync(evidence.ThesisId, cancellationToken);

        logger.LogInformation(
            "Classified evidence {EvidenceId} as {Stance} ({Confidence:F2}) for thesis {ThesisName}",
            evidence.Id, verdict.Stance, verdict.Confidence, evidence.Thesis!.Name);

        return new StanceClassificationItemResult(evidenceId, Processed: true);
    }

    private static StanceContext? BuildContext(ResearchEvidence evidence)
    {
        var thesis = evidence.Thesis!;
        var cluster = evidence.Cluster;
        var article = evidence.Article;
        var segment = evidence.TranscriptSegment;
        var chunk = evidence.ArticleChunk;

        if (segment is not null)
        {
            var transcript = segment.Transcript;
            var symbol = transcript?.Symbol;
            var member = new ClusterMember(
                "transcript", "primary",
                $"{symbol ?? "?"} earnings call segment",
                segment.Text,
                transcript?.Symbol,
                transcript?.CallDate ?? transcript?.CompletedAt ?? DateTime.UtcNow);
            return new StanceContext(
                ThesisName: thesis.Name,
                ThesisStatement: thesis.ThesisText,
                EventType: "transcript_segment",
                Summary: segment.Text,
                Symbol: symbol,
                MemberCount: 1,
                DominantSourceTier: "primary",
                Members: new[] { member });
        }

        if (chunk is not null)
        {
            var parent = chunk.Article;
            var member = new ClusterMember(
                parent?.Source ?? "edgar",
                parent?.SourceTier ?? "primary",
                parent?.Headline ?? "filing chunk",
                chunk.Text,
                parent?.Publisher,
                parent?.PublishedAt ?? chunk.CreatedAt);
            return new StanceContext(
                ThesisName: thesis.Name,
                ThesisStatement: thesis.ThesisText,
                EventType: chunk.Section ?? "filing_chunk",
                Summary: chunk.Text,
                Symbol: parent?.Symbol,
                MemberCount: 1,
                DominantSourceTier: parent?.SourceTier ?? "primary",
                Members: new[] { member });
        }

        if (cluster is not null)
        {
            var ev = cluster.Event;
            var members = cluster.Articles
                .OrderBy(a => TierRank(a.SourceTier))
                .ThenByDescending(a => a.PublishedAt)
                .Take(8)
                .Select(a => new ClusterMember(
                    a.Source, a.SourceTier, a.Headline, a.Summary, a.Publisher, a.PublishedAt))
                .ToList();

            if (members.Count == 0 && ev is null) return null;

            return new StanceContext(
                ThesisName: thesis.Name,
                ThesisStatement: thesis.ThesisText,
                EventType: ev?.EventType ?? cluster.TriageEventType ?? "unclassified",
                Summary: ev?.Summary ?? cluster.Articles.FirstOrDefault()?.Headline ?? string.Empty,
                Symbol: cluster.Symbol,
                MemberCount: cluster.MemberCount,
                DominantSourceTier: cluster.DominantSourceTier,
                Members: members);
        }

        if (article is not null)
        {
            var member = new ClusterMember(
                article.Source, article.SourceTier, article.Headline, article.Summary, article.Publisher, article.PublishedAt);
            return new StanceContext(
                ThesisName: thesis.Name,
                ThesisStatement: thesis.ThesisText,
                EventType: "article",
                Summary: article.Summary ?? article.Headline,
                Symbol: article.Symbol,
                MemberCount: 1,
                DominantSourceTier: article.SourceTier,
                Members: new[] { member });
        }

        return null;
    }

    private static int TierRank(string tier) => tier switch
    {
        "primary" => 0,
        "wire" => 1,
        "trade_press" => 2,
        "aggregator" => 3,
        "opinion" => 4,
        _ => 5,
    };
}
