using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class IngestionOptions
{
    public int IntervalMinutes { get; set; } = 15;
    public int InitialDelaySeconds { get; set; } = 10;
    public decimal TriageThreshold { get; set; } = 0.40m;
    public int SourcePollBatchSize { get; set; } = 4;
    public int ArticleBodyEnrichmentBatchSize { get; set; } = 8;
    public int MaxArticleBodyEnrichmentsPerCycle { get; set; } = 40;
    public int LeaseMinutes { get; set; } = 20;
    public int MaxArticlesPerSourcePerCycle { get; set; } = 40;
    public int PerArticleDelayMs { get; set; } = 0;
    public int BodyFetchDelayMs { get; set; } = 0;
}

public sealed record NewsIngestionBatchResult(
    int SourcesClaimed,
    int SourcesProcessed,
    int ArticlesFetched,
    int ArticleBodyEnrichmentClaimed,
    int ArticleBodyEnrichmentQueued,
    int ArticlesIngested,
    int ArticlesSkipped,
    int EventExtractionQueued,
    int SourceFailures,
    int ArticleFailures);

public class NewsIngestionOrchestrator(
    IServiceProvider services,
    IOptions<IngestionOptions> options,
    ILogger<NewsIngestionOrchestrator> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IngestionOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(NewsIngestionOrchestrator)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingestion run failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<NewsIngestionBatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<INewsSource>().ToList();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var cursorStore = scope.ServiceProvider.GetRequiredService<ISourceCursorStore>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();

        logger.LogInformation("Ingestion cycle start: {Count} sources registered: {Names}",
            sources.Count, string.Join(", ", sources.Select(s => s.Name)));

        var batchSize = Math.Max(1, _options.SourcePollBatchSize);
        var runId = await recorder.StartAsync(
            PipelineStages.ArticleIngestion,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.SourcePollBatchSize,
                _options.ArticleBodyEnrichmentBatchSize,
                _options.MaxArticleBodyEnrichmentsPerCycle,
                _options.LeaseMinutes,
                _options.MaxArticlesPerSourcePerCycle,
                _options.PerArticleDelayMs,
                _options.BodyFetchDelayMs,
            },
            cancellationToken: cancellationToken);

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

            var now = DateTime.UtcNow;
            var targets = NewsSourcePollTarget.CreateTargets(sources);
            foreach (var target in targets)
            {
                var cursor = await cursorStore.GetAsync(NewsSourcePollHandler.CursorSourceName, target.Name, cancellationToken);
                if (cursor?.NextEligibleRunAt is { } nextEligible && nextEligible > now)
                    continue;

                await queue.EnqueueAsync(
                    new EnqueueWorkRequest(
                        WorkType: PipelineWorkTypes.ArticleIngestion,
                        NaturalKey: target.Name,
                        PayloadJson: JsonSerializer.Serialize(new
                        {
                            sourceName = target.Name,
                            firstSourceIndex = target.FirstIndex,
                        }, JsonOptions),
                        Priority: PriorityFromSourceIndex(target.FirstIndex)),
                    cancellationToken);
            }

            var aggregate = new MutableNewsIngestionBatchResult();
            await DrainSourcePollsAsync(aggregate, batchSize, cancellationToken);
            await DrainArticleBodyEnrichmentsAsync(aggregate, cancellationToken);

            var result = aggregate.ToResult();
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(
                    InputCount: result.SourcesClaimed + result.ArticleBodyEnrichmentClaimed,
                    OutputCount: result.ArticlesIngested + result.EventExtractionQueued,
                    SkippedCount: result.ArticlesSkipped,
                    ErrorCount: result.SourceFailures + result.ArticleFailures),
                result,
                cancellationToken);

            if (result.ArticlesIngested > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "articles",
                    recordCount: result.ArticlesIngested,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            logger.LogInformation(
                "Ingestion run complete: {Articles} new articles across {Sources} sources; {Queued} body items queued; {Events} event extraction items queued",
                result.ArticlesIngested,
                result.SourcesProcessed,
                result.ArticleBodyEnrichmentQueued,
                result.EventExtractionQueued);

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task DrainSourcePollsAsync(
        MutableNewsIngestionBatchResult aggregate,
        int batchSize,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = services.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.ArticleIngestion,
                batchSize,
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            if (claimed.Count == 0) break;
            aggregate.SourcesClaimed += claimed.Count;

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<NewsSourcePollHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(work.Item.NaturalKey, cancellationToken);
                    aggregate.Add(itemResult);

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    aggregate.SourceFailures++;
                    logger.LogWarning(ex, "Source poll {SourceKey} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            if (claimed.Count < batchSize) break;
        }
    }

    private async Task DrainArticleBodyEnrichmentsAsync(
        MutableNewsIngestionBatchResult aggregate,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, _options.ArticleBodyEnrichmentBatchSize);
        var maxPerCycle = Math.Max(1, _options.MaxArticleBodyEnrichmentsPerCycle);

        while (!cancellationToken.IsCancellationRequested && aggregate.ArticleBodyEnrichmentClaimed < maxPerCycle)
        {
            using var scope = services.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
            var remaining = maxPerCycle - aggregate.ArticleBodyEnrichmentClaimed;
            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.ArticleBodyEnrichment,
                Math.Min(batchSize, remaining),
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            if (claimed.Count == 0) break;
            aggregate.ArticleBodyEnrichmentClaimed += claimed.Count;

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<ArticleBodyEnrichmentHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(work.Item.PayloadJson, cancellationToken);
                    aggregate.Add(itemResult);

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    aggregate.ArticleFailures++;
                    logger.LogWarning(ex, "Article body enrichment {NaturalKey} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            if (claimed.Count < batchSize) break;
        }
    }

    private static int PriorityFromSourceIndex(int sourceIndex)
        => int.MaxValue - Math.Max(0, sourceIndex);

    private sealed class MutableNewsIngestionBatchResult
    {
        public int SourcesClaimed { get; set; }
        public int SourcesProcessed { get; private set; }
        public int ArticlesFetched { get; private set; }
        public int ArticleBodyEnrichmentClaimed { get; set; }
        public int ArticleBodyEnrichmentQueued { get; private set; }
        public int ArticlesIngested { get; private set; }
        public int ArticlesSkipped { get; private set; }
        public int EventExtractionQueued { get; private set; }
        public int SourceFailures { get; set; }
        public int ArticleFailures { get; set; }

        public void Add(NewsSourcePollResult result)
        {
            if (result.SourceFound) SourcesProcessed++;
            ArticlesFetched += result.ArticlesFetched;
            ArticleBodyEnrichmentQueued += result.ArticlesQueued;
            ArticlesSkipped += result.ArticlesSkipped;
        }

        public void Add(ArticleBodyEnrichmentResult result)
        {
            if (result.ArticleInserted) ArticlesIngested++;
            if (result.EventExtractionQueued) EventExtractionQueued++;
            if (result.SuppressionCreated) ArticlesSkipped++;
        }

        public NewsIngestionBatchResult ToResult() => new(
            SourcesClaimed,
            SourcesProcessed,
            ArticlesFetched,
            ArticleBodyEnrichmentClaimed,
            ArticleBodyEnrichmentQueued,
            ArticlesIngested,
            ArticlesSkipped,
            EventExtractionQueued,
            SourceFailures,
            ArticleFailures);
    }
}
