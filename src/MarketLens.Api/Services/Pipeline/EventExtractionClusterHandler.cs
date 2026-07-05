using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record EventExtractionClusterResult(
    bool EventCreated,
    bool SuppressionCreated)
{
    public static EventExtractionClusterResult Noop { get; } = new(false, false);
    public static EventExtractionClusterResult Event { get; } = new(true, false);
    public static EventExtractionClusterResult Suppression { get; } = new(false, true);
}

public sealed class EventExtractionClusterHandler(
    MarketLensDbContext db,
    IEventExtractor extractor,
    ImportanceCalculator importance,
    ILogger<EventExtractionClusterHandler> logger)
{
    private static readonly HashSet<string> DataReleaseSources = new(StringComparer.OrdinalIgnoreCase)
    {
        SourceNames.Fred, SourceNames.Bea, SourceNames.Bls, SourceNames.Census,
    };

    public async Task<EventExtractionClusterResult> ProcessAsync(Guid clusterId, CancellationToken cancellationToken)
    {
        var cluster = await db.Clusters
            .Include(c => c.Articles)
            .Include(c => c.Event)
            .FirstOrDefaultAsync(c => c.Id == clusterId, cancellationToken);
        if (cluster is null) return EventExtractionClusterResult.Noop;
        if (cluster.Event is not null || string.IsNullOrWhiteSpace(cluster.TriageEventType))
            return EventExtractionClusterResult.Noop;

        if (ShouldBypassExtraction(cluster, out var bypassReason))
        {
            var suppressionCreated = await AddSuppressionAsync(
                cluster,
                SuppressionReasons.MacroDataBypass,
                bypassReason,
                cancellationToken);

            cluster.TriageEventType = null;
            cluster.TriageConfidence = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Bypassed cluster {Id}: {Reason}", cluster.Id, bypassReason);
            return new EventExtractionClusterResult(false, suppressionCreated);
        }

        var orderedArticles = cluster.Articles
            .OrderByDescending(a => a.SourceTier == "primary")
            .ThenByDescending(a => a.SourceTier == "wire")
            .ThenBy(a => a.PublishedAt)
            .ToList();

        var strongestEvidence = orderedArticles
            .Where(a => a.SourceTier is "primary" or "wire")
            .Take(8)
            .Concat(orderedArticles.Where(a => a.SourceTier is not ("primary" or "wire")).Take(6))
            .Take(12)
            .ToList();

        var members = strongestEvidence
            .Select(a => new ClusterMember(
                a.Source, a.SourceTier, a.Headline, a.Summary, a.Publisher, a.PublishedAt))
            .ToList();

        var context = new ClusterContext(
            EventType: cluster.TriageEventType!,
            Symbol: cluster.Symbol,
            MemberCount: cluster.MemberCount,
            DominantSourceTier: cluster.DominantSourceTier,
            Members: members);

        var extracted = await extractor.ExtractAsync(context, cancellationToken);
        if (IsNonFinding(extracted.Summary))
        {
            var suppressionCreated = await AddSuppressionAsync(
                cluster,
                SuppressionReasons.NonFindingExtraction,
                extracted.Summary,
                cancellationToken);

            cluster.TriageEventType = null;
            cluster.TriageConfidence = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Discarded non-finding cluster {Id}", cluster.Id);
            return new EventExtractionClusterResult(false, suppressionCreated);
        }

        var dominantSource = cluster.Articles
            .OrderByDescending(a => SourceReputation.For(a.Source).Weight)
            .Select(a => a.Source)
            .FirstOrDefault();
        var evidenceTexts = cluster.Articles.Select(a => $"{a.Headline}\n{a.Summary}");
        if (!MarketMateriality.AcceptExtractedEvent(cluster.Symbol, cluster.TriageEventType!, evidenceTexts, extracted.Summary, dominantSource))
        {
            var suppressionCreated = await AddSuppressionAsync(
                cluster,
                SuppressionReasons.ImmaterialExtraction,
                extracted.Summary,
                cancellationToken);

            cluster.TriageEventType = null;
            cluster.TriageConfidence = null;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Discarded immaterial cluster {Id}", cluster.Id);
            return new EventExtractionClusterResult(false, suppressionCreated);
        }

        var (imp, srcW, novW, prior) = importance.Compute(cluster, cluster.TriageEventType!, extracted.MagnitudeSignal);

        var ev = new Event
        {
            ClusterId = cluster.Id,
            EventType = cluster.TriageEventType!,
            Summary = extracted.Summary,
            Sentiment = extracted.Sentiment,
            Slots = extracted.SlotsJson,
            MagnitudeSignal = extracted.MagnitudeSignal,
            Importance = imp,
            SourceWeight = srcW,
            NoveltyWeight = novW,
            EventClassPrior = prior,
            ModelName = extracted.ModelName,
            PromptVersion = extracted.PromptVersion,
            ExtractedAt = DateTime.UtcNow,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Extracted cluster {Id} [{Type}] importance={Imp:F2} sentiment={Sent:+0.00;-0.00;0.00} members={Members}",
            cluster.Id, cluster.TriageEventType, imp, extracted.Sentiment, cluster.MemberCount);

        return EventExtractionClusterResult.Event;
    }

    private static bool ShouldBypassExtraction(Cluster cluster, out string reason)
    {
        if (cluster.Articles.Count == 0)
        {
            reason = "Cluster has no attached articles; skipping LLM extraction to prevent fabrication.";
            return true;
        }

        var dataReleaseCount = cluster.Articles.Count(a => DataReleaseSources.Contains(a.Source));
        if (dataReleaseCount * 5 >= cluster.Articles.Count * 4)
        {
            reason = "Macro data-observation cluster (FRED/BEA/BLS/Census); LLM extraction skipped to prevent paraphrase hallucinations.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsNonFinding(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return true;

        return summary.Contains("no material", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("not reported", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("not disclosed", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("no event", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("no specific", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("no action", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("not mentioned", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> AddSuppressionAsync(
        Cluster cluster,
        string reason,
        string extractedSummary,
        CancellationToken cancellationToken)
    {
        var article = cluster.Articles
            .OrderByDescending(a => a.SourceTier == "primary")
            .ThenByDescending(a => a.SourceTier == "wire")
            .ThenByDescending(a => a.PublishedAt)
            .FirstOrDefault();
        if (article is null) return false;

        var exists = await db.Suppressions.AnyAsync(s =>
            s.Source == article.Source &&
            s.SourceId == article.SourceId &&
            s.Stage == SuppressionStages.Extraction &&
            s.Reason == reason,
            cancellationToken);
        if (exists) return false;

        db.Suppressions.Add(new SuppressionRecord
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            ClusterId = cluster.Id,
            Source = article.Source,
            SourceId = article.SourceId,
            Symbol = article.Symbol,
            Stage = SuppressionStages.Extraction,
            Reason = reason,
            EventType = cluster.TriageEventType,
            Confidence = cluster.TriageConfidence,
            Headline = article.Headline,
            Summary = string.IsNullOrWhiteSpace(extractedSummary) ? article.Summary : extractedSummary,
            Url = article.Url,
            Publisher = article.Publisher,
            PublishedAt = article.PublishedAt,
            SuppressedAt = DateTime.UtcNow,
            RawPayload = article.RawPayload,
        });
        return true;
    }
}
