using MarketLens.Api.Services;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class ExtractionOptions
{
    public int BatchSize { get; set; } = 5;
    public int IntervalSeconds { get; set; } = 5;
    public int IdleIntervalSeconds { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 20;
    public int LeaseMinutes { get; set; } = 10;
}

public sealed record EventExtractionBatchResult(
    int PendingClusters,
    int EventsCreated,
    int SuppressionsCreated,
    int ItemFailures);

public class EventExtractionService(
    IServiceProvider services,
    IOptions<ExtractionOptions> options,
    IQuietHoursPolicy quietHours,
    ILogger<EventExtractionService> logger) : BackgroundService
{
    private readonly ExtractionOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(EventExtractionService)}:{Environment.MachineName}:{Environment.ProcessId}";
    private bool _wasQuiet;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (quietHours.IsQuietNow())
            {
                if (!_wasQuiet)
                {
                    logger.LogInformation("Quiet hours — event extraction paused");
                    _wasQuiet = true;
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (_wasQuiet)
            {
                logger.LogInformation("Quiet hours ended — event extraction resumed");
                _wasQuiet = false;
            }

            EventExtractionBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Extraction batch failed");
                result = new EventExtractionBatchResult(0, 0, 0, 1);
            }

            var delay = result.EventsCreated == 0 && result.SuppressionsCreated == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<EventExtractionBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.EventExtraction,
            PipelineTriggers.Scheduled,
            metadata: new { _options.BatchSize },
            cancellationToken: cancellationToken);

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

            var candidates = await db.Clusters
                .AsNoTracking()
                .Where(c => c.TriageEventType != null && c.Event == null)
                .OrderByDescending(c => c.TopSourceWeight)
                .ThenByDescending(c => c.LastSeenAt)
                .Take(Math.Max(_options.BatchSize * 4, _options.BatchSize))
                .Select(c => new { c.Id, c.TopSourceWeight })
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                await queue.EnqueueAsync(
                    new EnqueueWorkRequest(
                        WorkType: PipelineWorkTypes.EventExtraction,
                        NaturalKey: candidate.Id.ToString(),
                        PayloadJson: $$"""{"clusterId":"{{candidate.Id}}"}""",
                        Priority: PriorityFromWeight(candidate.TopSourceWeight)),
                    cancellationToken);
            }

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.EventExtraction,
                _options.BatchSize,
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            if (claimed.Count == 0)
            {
                var empty = new EventExtractionBatchResult(0, 0, 0, 0);
                await recorder.SucceedAsync(runId, new PipelineRunCounts(), empty, cancellationToken);
                return empty;
            }

            var eventsCreated = 0;
            var suppressionsCreated = 0;
            var itemFailures = 0;
            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clusterId = Guid.Parse(work.Item.NaturalKey);
                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<EventExtractionClusterHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(clusterId, cancellationToken);
                    if (itemResult.EventCreated) eventsCreated++;
                    if (itemResult.SuppressionCreated) suppressionsCreated++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    itemFailures++;
                    logger.LogWarning(ex, "Failed to extract cluster {Id}", clusterId);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new EventExtractionBatchResult(claimed.Count, eventsCreated, suppressionsCreated, itemFailures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(
                    InputCount: claimed.Count,
                    OutputCount: eventsCreated + suppressionsCreated,
                    ErrorCount: itemFailures),
                result,
                cancellationToken);
            if (eventsCreated > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "events",
                    recordCount: eventsCreated,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }
            if (suppressionsCreated > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "suppressions",
                    recordCount: suppressionsCreated,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, cancellationToken: cancellationToken);
            throw;
        }
    }

    private static int PriorityFromWeight(decimal sourceWeight)
        => (int)Math.Clamp(sourceWeight * 1000m, 0m, 1000m);
}
