using MarketLens.Api.Services;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class ThesisPlanRefreshOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 300;
    public int IntervalSeconds { get; set; } = 1800;
    public int RefreshAfterDays { get; set; } = 7;
    public int MaxPerCycle { get; set; } = 2;
    public int EnqueueBatchSize { get; set; } = 10;
    public int QueueBacklogLimit { get; set; } = 25;
    public int LeaseMinutes { get; set; } = 60;
}

public sealed record ThesisPlanRefreshBatchResult(
    int Enqueued,
    int Claimed,
    int Processed,
    int Generated,
    int ItemFailures);

public class ThesisPlanRefreshService(
    IServiceProvider services,
    IOptions<ThesisPlanRefreshOptions> options,
    IQuietHoursPolicy quietHours,
    ILogger<ThesisPlanRefreshService> logger) : BackgroundService
{
    private readonly ThesisPlanRefreshOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(ThesisPlanRefreshService)}:{Environment.MachineName}:{Environment.ProcessId}";
    private bool _wasQuiet;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (quietHours.IsQuietNow())
            {
                if (!_wasQuiet)
                {
                    logger.LogInformation("Quiet hours — thesis plan refresh paused");
                    _wasQuiet = true;
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (_wasQuiet)
            {
                logger.LogInformation("Quiet hours ended — thesis plan refresh resumed");
                _wasQuiet = false;
            }

            ThesisPlanRefreshBatchResult result;
            try
            {
                result = await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plan refresh cycle failed");
                result = new ThesisPlanRefreshBatchResult(0, 0, 0, 0, 1);
            }

            var delaySeconds = result.Claimed == 0 && result.Processed == 0
                ? Math.Max(60, _options.IntervalSeconds)
                : Math.Max(5, Math.Min(60, _options.IntervalSeconds));
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<ThesisPlanRefreshBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();

        var runId = await recorder.StartAsync(
            PipelineStages.ThesisPlanRefresh,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.RefreshAfterDays,
                _options.MaxPerCycle,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
                _options.LeaseMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var generated = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, queue, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.ThesisPlanRefresh,
                Math.Max(1, _options.MaxPerCycle),
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<ThesisPlanRefreshHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Processed)
                        processed++;
                    if (itemResult.Generated)
                        generated++;
                    if (itemResult.Error is not null)
                        failures++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Thesis plan refresh work item {ThesisId} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new ThesisPlanRefreshBatchResult(enqueued, claimed.Count, processed, generated, failures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: generated, ErrorCount: failures),
                result,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, processed, generated, failures }, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.ThesisPlanRefresh &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var staleBefore = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RefreshAfterDays));
        var take = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var due = await db.ResearchTheses
            .AsNoTracking()
            .Where(t => t.Status == "active" &&
                (t.PlanGeneratedAt == null || t.PlanGeneratedAt < staleBefore))
            .OrderBy(t => t.PlanGeneratedAt ?? DateTime.MinValue)
            .Take(take)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        foreach (var thesisId in due)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.ThesisPlanRefresh,
                    NaturalKey: thesisId.ToString(),
                    PayloadJson: $$"""{"thesisId":"{{thesisId}}"}""",
                    Priority: 0),
                cancellationToken);
            enqueued++;
        }

        return enqueued;
    }
}
