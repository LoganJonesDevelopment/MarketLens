using MarketLens.Api.Services;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class FundamentalsRefreshOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 75;
    public int IntervalSeconds { get; set; } = 21600;
    public int WindowDays { get; set; } = 14;
    public int CandidateCount { get; set; } = 24;
    public int MaxPerCycle { get; set; } = 8;
    public int MaxAgeHours { get; set; } = 24;
    public int DelayBetweenSymbolsMs { get; set; } = 1200;
    public int EnqueueBatchSize { get; set; } = 24;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 20;
}

public sealed record FundamentalsRefreshBatchResult(
    int Enqueued,
    int Claimed,
    int Processed,
    int Refreshed,
    int ItemFailures);

public class FundamentalsRefreshService(
    IServiceProvider services,
    IOptions<FundamentalsRefreshOptions> options,
    ILogger<FundamentalsRefreshService> logger) : BackgroundService
{
    private readonly FundamentalsRefreshOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(FundamentalsRefreshService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            FundamentalsRefreshBatchResult result;
            try
            {
                result = await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fundamentals refresh cycle failed");
                result = new FundamentalsRefreshBatchResult(0, 0, 0, 0, 1);
            }

            var delaySeconds = result.Claimed == 0 && result.Processed == 0
                ? Math.Max(300, _options.IntervalSeconds)
                : Math.Max(5, Math.Min(60, _options.IntervalSeconds));

            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<FundamentalsRefreshBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var fundamentals = scope.ServiceProvider.GetRequiredService<CompanyFundamentalsService>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.Fundamentals,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.WindowDays,
                _options.CandidateCount,
                _options.MaxPerCycle,
                _options.MaxAgeHours,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
                _options.LeaseMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var refreshed = 0;
        var failures = 0;
        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, fundamentals, queue, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.FundamentalsRefresh,
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
                    var handler = itemScope.ServiceProvider.GetRequiredService<FundamentalsRefreshHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Processed)
                        processed++;
                    if (itemResult.Refreshed)
                        refreshed++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Fundamentals work item {Symbol} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }

                if (_options.DelayBetweenSymbolsMs > 0)
                    await Task.Delay(_options.DelayBetweenSymbolsMs, cancellationToken);
            }

            var result = new FundamentalsRefreshBatchResult(enqueued, claimed.Count, processed, refreshed, failures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: refreshed, ErrorCount: failures),
                metadata: result,
                cancellationToken: cancellationToken);

            if (refreshed > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "company_fundamentals",
                    recordCount: refreshed,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, processed, refreshed, failures }, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        CompanyFundamentalsService fundamentals,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.FundamentalsRefresh &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var symbols = await fundamentals.LoadCandidateSymbolsAsync(
            _options.WindowDays,
            Math.Max(_options.CandidateCount, enqueueLimit),
            cancellationToken);

        var enqueued = 0;
        foreach (var symbol in symbols.Take(enqueueLimit))
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.FundamentalsRefresh,
                    NaturalKey: normalized,
                    PayloadJson: $$"""{"symbol":"{{normalized}}","maxAgeHours":{{Math.Max(1, _options.MaxAgeHours)}}}""",
                    Priority: 0),
                cancellationToken);
            enqueued++;
        }

        return enqueued;
    }
}
