using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.HostedServices;

public class ThinArticleBackfillService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<ThinArticleBackfillService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] BackfillSources =
    [
        SourceNames.FedSpeeches,
        SourceNames.FedPress,
        SourceNames.Bls,
        SourceNames.Bea,
        SourceNames.SecEnforcement,
        SourceNames.Ftc,
        SourceNames.DojAntitrust,
        SourceNames.BusinessWire,
        SourceNames.GlobeNewswire,
        SourceNames.PrNewswire,
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration["BACKFILL_THIN_ARTICLES"];
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("ThinArticleBackfillService: BACKFILL_THIN_ARTICLES not set, skipping");
            return;
        }

        // Wait for embeddings sidecar and other services to come up.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        logger.LogInformation("ThinArticleBackfillService: starting backfill of thin-summary articles");

        try
        {
            await RunBackfillAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ThinArticleBackfillService: backfill failed");
        }

        logger.LogInformation("ThinArticleBackfillService: backfill complete");
    }

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var triageThreshold = configuration.GetValue<decimal>("Ingestion:TriageThreshold", 0.40m);
        var bodyFetchDelayMs = configuration.GetValue<int>("Ingestion:BodyFetchDelayMs", 0);
        var perArticleDelayMs = configuration.GetValue<int>("Ingestion:PerArticleDelayMs", 0);
        var maxQueueItems = configuration.GetValue<int>("BACKFILL_THIN_ARTICLES_MAX_QUEUE_ITEMS", 250);
        maxQueueItems = Math.Max(1, maxQueueItems);

        // Candidates: articles from thin-text sources where the cluster has no triage event type,
        // the article summary is thin (< 200 chars), and the article has a URL to fetch.
        var candidates = await db.Articles
            .Where(a => BackfillSources.Contains(a.Source)
                && a.ClusterId != null
                && a.Cluster != null
                && a.Cluster.TriageEventType == null
                && (a.Summary == null || a.Summary.Length < 200)
                && a.Url != null)
            .OrderBy(a => a.PublishedAt)
            .Take(maxQueueItems)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "ThinArticleBackfillService: queueing up to {Max} thin articles; found {Count}",
            maxQueueItems,
            candidates.Count);

        var queued = 0;
        foreach (var article in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = new ArticleBodyEnrichmentPayload(
                Article: new(
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
                },
                BodyFetchDelayMs: bodyFetchDelayMs,
                PerArticleDelayMs: perArticleDelayMs,
                TriageThreshold: triageThreshold,
                ExistingArticleId: article.Id);

            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.ArticleBodyEnrichment,
                    NaturalKey: $"existing-article:{article.Id}",
                    PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
                    Priority: PriorityFromPublishedAt(article.PublishedAt)),
                cancellationToken);
            queued++;
        }

        logger.LogInformation(
            "ThinArticleBackfillService: queued {Queued} thin article refresh jobs",
            queued);
    }

    private static int PriorityFromPublishedAt(DateTime publishedAt)
    {
        var utc = publishedAt.Kind == DateTimeKind.Utc
            ? publishedAt
            : DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
        var minutes = (utc - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        return (int)Math.Clamp(minutes, 0, int.MaxValue);
    }
}
