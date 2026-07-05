using System.Text.Json;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MarketLens.Api.Services.Pipeline;

public sealed record ArticleBodyEnrichmentPayload(
    IngestedArticle Article,
    int BodyFetchDelayMs,
    int PerArticleDelayMs,
    decimal TriageThreshold,
    Guid? ExistingArticleId = null);

public sealed record ArticleBodyEnrichmentResult(
    bool ArticleInserted,
    bool EventExtractionQueued,
    bool SuppressionCreated)
{
    public static ArticleBodyEnrichmentResult Noop { get; } = new(false, false, false);
}

public sealed class ArticleBodyEnrichmentHandler(
    MarketLensDbContext db,
    IEmbeddingClient embedder,
    ITriageClient triage,
    ClusterAssigner clusterAssigner,
    ArticleBodyEnricher bodyEnricher,
    ILocalWorkQueue queue,
    ILogger<ArticleBodyEnrichmentHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ArticleBodyEnrichmentResult> ProcessAsync(
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ArticleBodyEnrichmentPayload>(payloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Article body enrichment payload is empty or invalid.");

        if (payload.ExistingArticleId is not null)
            return await ProcessExistingArticleAsync(payload, cancellationToken);

        var ingest = ApplyBodyFetchDelay(payload.Article, payload.BodyFetchDelayMs);
        if (await ArticleExistsAsync(ingest, cancellationToken))
            return ArticleBodyEnrichmentResult.Noop;

        ingest = await bodyEnricher.EnrichAsync(ingest, cancellationToken);

        var text = ArticleText(ingest);
        var embeddings = await embedder.EmbedBatchAsync([text], cancellationToken);
        var (tier, _) = SourceReputation.For(ingest.Source);

        var article = new Article
        {
            Id = Guid.NewGuid(),
            Source = ingest.Source,
            SourceId = ingest.SourceId,
            SourceTier = tier,
            Symbol = ingest.Symbol?.ToUpperInvariant(),
            Headline = ingest.Headline,
            Summary = ingest.Summary,
            Url = ingest.Url,
            Publisher = ingest.Publisher,
            PublishedAt = DateTime.SpecifyKind(ingest.PublishedAt, DateTimeKind.Utc),
            IngestedAt = DateTime.UtcNow,
            RawPayload = ingest.RawJson,
            Embedding = new Vector(embeddings[0]),
        };

        db.Articles.Add(article);
        var cluster = await clusterAssigner.AssignAsync(article, cancellationToken);

        var suppressionCreated = false;
        if (string.IsNullOrEmpty(cluster.TriageEventType))
        {
            var rule = EventTypeRules.ClassifyDeterministically(
                article.Source,
                article.Headline,
                article.Summary);

            if (rule is not null)
            {
                cluster.TriageEventType = rule.Value.EventType;
                cluster.TriageConfidence = rule.Value.Confidence;
            }
            else
            {
                suppressionCreated = await ClassifyTriageAsync(
                    cluster,
                    article,
                    payload.TriageThreshold,
                    cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var eventExtractionQueued = await EnqueueEventExtractionAsync(cluster, cancellationToken);
        await DelayAfterArticleAsync(payload.PerArticleDelayMs, cancellationToken);

        logger.LogInformation(
            "Finalized article {Source}:{SourceId} into cluster {ClusterId}",
            article.Source,
            article.SourceId,
            cluster.Id);

        return new ArticleBodyEnrichmentResult(true, eventExtractionQueued, suppressionCreated);
    }

    private async Task<ArticleBodyEnrichmentResult> ProcessExistingArticleAsync(
        ArticleBodyEnrichmentPayload payload,
        CancellationToken cancellationToken)
    {
        var article = await db.Articles
            .Include(a => a.Cluster)
            .SingleOrDefaultAsync(a => a.Id == payload.ExistingArticleId, cancellationToken);

        if (article is null)
            return ArticleBodyEnrichmentResult.Noop;

        var ingest = ApplyBodyFetchDelay(FromExistingArticle(article), payload.BodyFetchDelayMs);
        ingest = await bodyEnricher.EnrichAsync(ingest, cancellationToken);

        article.Summary = ingest.Summary;
        var embeddings = await embedder.EmbedBatchAsync([ArticleText(article)], cancellationToken);
        article.Embedding = new Vector(embeddings[0]);

        var cluster = article.Cluster;
        if (cluster is null)
            cluster = await clusterAssigner.AssignAsync(article, cancellationToken);

        var suppressionCreated = false;
        if (string.IsNullOrEmpty(cluster.TriageEventType))
        {
            var rule = EventTypeRules.ClassifyDeterministically(
                article.Source,
                article.Headline,
                article.Summary);

            if (rule is not null)
            {
                cluster.TriageEventType = rule.Value.EventType;
                cluster.TriageConfidence = rule.Value.Confidence;
            }
            else
            {
                suppressionCreated = await ClassifyTriageAsync(
                    cluster,
                    article,
                    payload.TriageThreshold,
                    cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var eventExtractionQueued = await EnqueueEventExtractionAsync(cluster, cancellationToken);
        await DelayAfterArticleAsync(payload.PerArticleDelayMs, cancellationToken);

        logger.LogInformation(
            "Refreshed existing article {ArticleId} {Source}:{SourceId} into cluster {ClusterId}",
            article.Id,
            article.Source,
            article.SourceId,
            cluster.Id);

        return new ArticleBodyEnrichmentResult(false, eventExtractionQueued, suppressionCreated);
    }

    private async Task<bool> ClassifyTriageAsync(
        Cluster cluster,
        Article article,
        decimal triageThreshold,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await triage.ClassifyBatchAsync([ArticleText(article)], triageThreshold, cancellationToken);
            var output = result[0];

            if (MarketMateriality.AcceptClassifierOutput(
                article.Source,
                article.Symbol,
                output.EventType,
                output.Confidence,
                article.Headline,
                article.Summary))
            {
                cluster.TriageEventType = output.EventType;
                cluster.TriageConfidence = output.Confidence;
                return false;
            }

            cluster.TriageEventType = null;
            cluster.TriageConfidence = null;
            return await AddSuppressionAsync(
                article,
                cluster.Id,
                SuppressionStages.Triage,
                SuppressionReasons.ClassifierRejected,
                output.EventType,
                output.Confidence,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Triage failed for article {Source}:{SourceId}", article.Source, article.SourceId);
            return false;
        }
    }

    private async Task<bool> EnqueueEventExtractionAsync(
        Cluster cluster,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cluster.TriageEventType))
            return false;

        var hasEvent = await db.Events.AnyAsync(e => e.ClusterId == cluster.Id, cancellationToken);
        if (hasEvent) return false;

        await queue.EnqueueAsync(
            new EnqueueWorkRequest(
                WorkType: PipelineWorkTypes.EventExtraction,
                NaturalKey: cluster.Id.ToString(),
                PayloadJson: JsonSerializer.Serialize(new { clusterId = cluster.Id }, JsonOptions),
                Priority: PriorityFromWeight(cluster.TopSourceWeight)),
            cancellationToken);
        return true;
    }

    private async Task<bool> AddSuppressionAsync(
        Article article,
        Guid? clusterId,
        string stage,
        string reason,
        string? eventType,
        decimal? confidence,
        CancellationToken cancellationToken)
    {
        var exists = await db.Suppressions.AnyAsync(s =>
            s.Source == article.Source &&
            s.SourceId == article.SourceId &&
            s.Stage == stage &&
            s.Reason == reason,
            cancellationToken);
        if (exists) return false;

        db.Suppressions.Add(new SuppressionRecord
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            ClusterId = clusterId,
            Source = article.Source,
            SourceId = article.SourceId,
            Symbol = article.Symbol,
            Stage = stage,
            Reason = reason,
            EventType = eventType,
            Confidence = confidence,
            Headline = article.Headline,
            Summary = article.Summary,
            Url = article.Url,
            Publisher = article.Publisher,
            PublishedAt = article.PublishedAt,
            SuppressedAt = DateTime.UtcNow,
            RawPayload = article.RawPayload,
        });
        return true;
    }

    private async Task<bool> ArticleExistsAsync(
        IngestedArticle article,
        CancellationToken cancellationToken)
        => await db.Articles.AnyAsync(a =>
            a.Source == article.Source &&
            a.SourceId == article.SourceId,
            cancellationToken);

    private static IngestedArticle ApplyBodyFetchDelay(
        IngestedArticle article,
        int bodyFetchDelayMs)
    {
        if (bodyFetchDelayMs <= 0 || !article.NeedsBodyFetch)
            return article;

        return article with { BodyFetchDelayMs = Math.Max(article.BodyFetchDelayMs, bodyFetchDelayMs) };
    }

    private static IngestedArticle FromExistingArticle(Article article)
        => new(
            Source: article.Source,
            SourceId: article.SourceId,
            Symbol: article.Symbol,
            Headline: article.Headline,
            Summary: article.Summary,
            Url: article.Url,
            Publisher: article.Publisher,
            PublishedAt: article.PublishedAt,
            RawJson: article.RawPayload)
        {
            NeedsBodyFetch = true,
        };

    private static async Task DelayAfterArticleAsync(
        int perArticleDelayMs,
        CancellationToken cancellationToken)
    {
        if (perArticleDelayMs > 0)
            await Task.Delay(perArticleDelayMs, cancellationToken);
    }

    private static string ArticleText(IngestedArticle article)
        => string.IsNullOrWhiteSpace(article.Summary)
            ? article.Headline
            : $"{article.Headline}\n{article.Summary}";

    private static string ArticleText(Article article)
        => string.IsNullOrWhiteSpace(article.Summary)
            ? article.Headline
            : $"{article.Headline}\n{article.Summary}";

    private static int PriorityFromWeight(decimal sourceWeight)
        => (int)Math.Clamp(sourceWeight * 1000m, 0m, 1000m);
}
