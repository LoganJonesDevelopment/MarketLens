using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class EarningsCalendarOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 90;
    public int IntervalSeconds { get; set; } = 6 * 60 * 60;
    public int LookbackHours { get; set; } = 120;
    public int BatchSize { get; set; } = 4;
    public int EnqueueBatchSize { get; set; } = 100;
    public int MaxPerCycle { get; set; } = 100;
    public int QueueBacklogLimit { get; set; } = 250;
    public int LeaseMinutes { get; set; } = 20;
    public int DelayBetweenSymbolsMs { get; set; } = 200;
}

public sealed record EarningsCalendarBatchResult(
    int Enqueued,
    int Claimed,
    int Processed,
    int TranscriptQueued,
    int ManualActionsCreated,
    int Current,
    int ItemFailures);

public class EarningsCalendarService(
    IServiceProvider services,
    IOptions<EarningsCalendarOptions> options,
    ILogger<EarningsCalendarService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EarningsCalendarOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(EarningsCalendarService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Earnings calendar cycle failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<EarningsCalendarBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var watchlistProvider = scope.ServiceProvider.GetRequiredService<IWatchlistProvider>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();

        var runId = await recorder.StartAsync(
            PipelineStages.EarningsCalendar,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.LookbackHours,
                _options.BatchSize,
                _options.EnqueueBatchSize,
                _options.MaxPerCycle,
                _options.QueueBacklogLimit,
                _options.LeaseMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var transcripts = 0;
        var manualActions = 0;
        var current = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, watchlistProvider, queue, cancellationToken);

            var maxPerCycle = Math.Max(1, _options.MaxPerCycle);
            var claimedTotal = 0;
            while (!cancellationToken.IsCancellationRequested && claimedTotal < maxPerCycle)
            {
                var batchSize = Math.Min(Math.Max(1, _options.BatchSize), maxPerCycle - claimedTotal);
                var claimed = await queue.ClaimBatchAsync(
                    PipelineWorkTypes.EarningsCalendar,
                    batchSize,
                    _workerId,
                    TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                    cancellationToken);

                if (claimed.Count == 0) break;
                claimedTotal += claimed.Count;

                foreach (var work in claimed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        using var itemScope = services.CreateScope();
                        var handler = itemScope.ServiceProvider.GetRequiredService<EarningsCalendarTickerHandler>();
                        var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                        var itemResult = await handler.ProcessAsync(
                            work.Item.NaturalKey,
                            work.Item.PayloadJson,
                            cancellationToken);

                        if (itemResult.Processed)
                            processed++;
                        if (itemResult.TranscriptQueued)
                            transcripts++;
                        if (itemResult.ManualActionCreated)
                            manualActions++;
                        if (itemResult.Current)
                            current++;

                        await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        logger.LogWarning(ex, "Earnings calendar work item {Symbol} failed", work.Item.NaturalKey);

                        using var itemScope = services.CreateScope();
                        var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                        await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                    }

                    if (_options.DelayBetweenSymbolsMs > 0)
                        await Task.Delay(_options.DelayBetweenSymbolsMs, cancellationToken);
                }

                if (claimed.Count < batchSize) break;
            }

            var result = new EarningsCalendarBatchResult(
                enqueued,
                claimedTotal,
                processed,
                transcripts,
                manualActions,
                current,
                failures);

            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(
                    InputCount: claimedTotal,
                    OutputCount: transcripts + manualActions,
                    SkippedCount: current,
                    ErrorCount: failures),
                result,
                cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, processed, transcripts, manualActions, current, failures }, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        IWatchlistProvider watchlistProvider,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.EarningsCalendar &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var watched = await watchlistProvider.GetWatchedTickersAsync(cancellationToken);
        var equities = watched
            .Where(w => !string.IsNullOrWhiteSpace(w.Cik))
            .Take(enqueueLimit)
            .ToList();

        logger.LogInformation("Earnings calendar: queueing {Count} equity tickers", equities.Count);

        var enqueued = 0;
        for (var i = 0; i < equities.Count; i++)
        {
            var ticker = equities[i];
            var symbol = ticker.Symbol.Trim().ToUpperInvariant();
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.EarningsCalendar,
                    NaturalKey: symbol,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        ticker.AssetId,
                        symbol,
                        ticker.Name,
                        ticker.Cik,
                        ticker.IrFeedUrl,
                        ticker.Aliases,
                        lookbackHours = Math.Max(1, _options.LookbackHours),
                    }, JsonOptions),
                    Priority: int.MaxValue - i),
                cancellationToken);
            enqueued++;
        }

        return enqueued;
    }
}
