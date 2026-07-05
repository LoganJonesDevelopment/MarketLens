using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class ResearchSnapshotOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 120;
    public int IntervalSeconds { get; set; } = 3600;
    public int MinHoursBetweenSnapshots { get; set; } = 20;
    public int MaxPerCycle { get; set; } = 20;
    public int EnqueueBatchSize { get; set; } = 50;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 15;
}

public sealed record ResearchSnapshotBatchResult(
    int Enqueued,
    int Claimed,
    int Processed,
    int Written,
    int Current,
    int ItemFailures);

public class ResearchSnapshotService(
    IServiceProvider services,
    IOptions<ResearchSnapshotOptions> options,
    ILogger<ResearchSnapshotService> logger) : BackgroundService
{
    private static readonly string[] TrackedStatuses = ["active", "watching", "exploration"];
    private readonly ResearchSnapshotOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(ResearchSnapshotService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            ResearchSnapshotBatchResult result;
            try
            {
                result = await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Snapshot cycle failed");
                result = new ResearchSnapshotBatchResult(0, 0, 0, 0, 0, 1);
            }

            var delaySeconds = result.Claimed == 0 && result.Processed == 0
                ? Math.Max(60, _options.IntervalSeconds)
                : Math.Max(5, Math.Min(60, _options.IntervalSeconds));
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<ResearchSnapshotBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();

        var runId = await recorder.StartAsync(
            PipelineStages.ResearchSnapshot,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.MinHoursBetweenSnapshots,
                _options.MaxPerCycle,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
                _options.LeaseMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var written = 0;
        var current = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, queue, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.ResearchSnapshot,
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
                    var handler = itemScope.ServiceProvider.GetRequiredService<ResearchSnapshotHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Processed)
                        processed++;
                    if (itemResult.Written)
                        written++;
                    if (itemResult.Current)
                        current++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Research snapshot work item {ThesisId} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new ResearchSnapshotBatchResult(enqueued, claimed.Count, processed, written, current, failures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(
                    InputCount: claimed.Count,
                    OutputCount: written,
                    SkippedCount: current - written,
                    ErrorCount: failures),
                result,
                cancellationToken);

            if (written > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "research_snapshots",
                    recordCount: written,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, processed, written, current, failures }, cancellationToken: cancellationToken);
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
                i.WorkType == PipelineWorkTypes.ResearchSnapshot &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var minGap = DateTime.UtcNow.AddHours(-Math.Max(1, _options.MinHoursBetweenSnapshots));
        var take = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var due = await db.ResearchTheses
            .AsNoTracking()
            .Where(t => TrackedStatuses.Contains(t.Status) &&
                !db.ResearchSnapshots.Any(s => s.ThesisId == t.Id && s.SnapshotAt >= minGap))
            .OrderBy(t => t.UpdatedAt)
            .Take(take)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        foreach (var thesisId in due)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.ResearchSnapshot,
                    NaturalKey: thesisId.ToString(),
                    PayloadJson: $$"""{"minHoursBetweenSnapshots":{{Math.Max(1, _options.MinHoursBetweenSnapshots)}}}""",
                    Priority: 0),
                cancellationToken);
            enqueued++;
        }

        return enqueued;
    }
}
