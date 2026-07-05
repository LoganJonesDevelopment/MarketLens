using MarketLens.Api.Services;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class IdeaMemoOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 45;
    public int IntervalSeconds { get; set; } = 300;
    public int WindowDays { get; set; } = 90;
    public int CandidateCount { get; set; } = 10;
    public int MaxPerCycle { get; set; } = 2;
    public int EnqueueBatchSize { get; set; } = 25;
    public int QueueBacklogLimit { get; set; } = 50;
    public int LeaseMinutes { get; set; } = 20;
    public int CandidateCooldownMinutes { get; set; } = 60;
}

public sealed record IdeaMemoBatchResult(
    int Enqueued,
    int Claimed,
    int Processed,
    int Generated,
    int Current,
    int ItemFailures);

public class IdeaMemoRefreshService(
    IServiceProvider services,
    IOptions<IdeaMemoOptions> options,
    IQuietHoursPolicy quietHours,
    ILogger<IdeaMemoRefreshService> logger) : BackgroundService
{
    private readonly IdeaMemoOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(IdeaMemoRefreshService)}:{Environment.MachineName}:{Environment.ProcessId}";
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
                    logger.LogInformation("Quiet hours — idea memo refresh paused");
                    _wasQuiet = true;
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (_wasQuiet)
            {
                logger.LogInformation("Quiet hours ended — idea memo refresh resumed");
                _wasQuiet = false;
            }

            IdeaMemoBatchResult result;
            try
            {
                result = await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Idea memo refresh cycle failed");
                result = new IdeaMemoBatchResult(0, 0, 0, 0, 0, 1);
            }

            var delaySeconds = result.Claimed == 0 && result.Processed == 0
                ? Math.Max(30, _options.IntervalSeconds)
                : Math.Max(5, Math.Min(30, _options.IntervalSeconds));
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<IdeaMemoBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var memoService = scope.ServiceProvider.GetRequiredService<IdeaMemoService>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.IdeaMemo,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.WindowDays,
                _options.CandidateCount,
                _options.MaxPerCycle,
                _options.EnqueueBatchSize,
                _options.CandidateCooldownMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var generated = 0;
        var current = 0;
        var failures = 0;
        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, memoService, queue, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.IdeaMemo,
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
                    var handler = itemScope.ServiceProvider.GetRequiredService<IdeaMemoWorkHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Processed)
                        processed++;
                    if (itemResult.Generated)
                        generated++;
                    if (itemResult.Current)
                        current++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Idea memo work item {WorkItemId} failed", work.Item.Id);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new IdeaMemoBatchResult(enqueued, claimed.Count, processed, generated, current, failures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: generated, ErrorCount: failures),
                metadata: result,
                cancellationToken: cancellationToken);

            if (generated > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "idea_memos",
                    recordCount: generated,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

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
        IdeaMemoService memoService,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.IdeaMemo &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var pendingLimit = Math.Min(enqueueLimit, Math.Max(1, _options.MaxPerCycle * 2));
        var pendingMemos = await db.IdeaMemos
            .AsNoTracking()
            .Where(m => m.Status == IdeaMemoStatuses.Pending)
            .OrderBy(m => m.RequestedAt)
            .Take(pendingLimit)
            .Select(m => new { m.Id, m.Symbol, m.WindowDays, m.RequestedAt })
            .ToListAsync(cancellationToken);

        var enqueued = 0;
        foreach (var memo in pendingMemos)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.IdeaMemo,
                    NaturalKey: memo.Id.ToString(),
                    PayloadJson: $$"""{"memoId":"{{memo.Id}}"}""",
                    Priority: PriorityFromRequestedAt(memo.RequestedAt)),
                cancellationToken);
            enqueued++;
        }

        var remaining = enqueueLimit - enqueued;
        if (remaining <= 0) return enqueued;

        var windowDays = Math.Clamp(_options.WindowDays, 7, 365);
        var activeKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.IdeaMemo &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running))
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var cooldownCutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.CandidateCooldownMinutes));
        var recentCompletedKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.IdeaMemo &&
                i.Status == PipelineWorkStatuses.Completed &&
                i.CompletedAt != null &&
                i.CompletedAt >= cooldownCutoff)
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var blockedKeys = activeKeys
            .Concat(recentCompletedKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var symbols = await memoService.LoadMemoCandidateSymbolsAsync(
            windowDays,
            Math.Max(_options.CandidateCount, remaining),
            cancellationToken);

        foreach (var symbol in symbols)
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            var naturalKey = $"{normalized}:{windowDays}";
            if (blockedKeys.Contains(naturalKey)) continue;

            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.IdeaMemo,
                    NaturalKey: naturalKey,
                    PayloadJson: $$"""{"symbol":"{{normalized}}","windowDays":{{windowDays}}}""",
                    Priority: 0),
                cancellationToken);
            enqueued++;
            if (enqueued >= enqueueLimit) break;
        }

        return enqueued;
    }

    private static int PriorityFromRequestedAt(DateTime requestedAt)
    {
        var minutes = (requestedAt - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        return 1_000_000 + (int)Math.Clamp(minutes, 0, int.MaxValue - 1_000_000);
    }
}
