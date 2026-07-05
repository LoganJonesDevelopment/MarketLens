using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class MarketSnapshotOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 25;
    public int WorkBatchSize { get; set; } = 5;
    public int EnqueueBatchSize { get; set; } = 25;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 5;
    public int InitialDelaySeconds { get; set; } = 30;
    public int IntervalSeconds { get; set; } = 60;
    public int IdleIntervalSeconds { get; set; } = 180;
    public int RefreshWindowHours { get; set; } = 24;
    public int RefreshIntervalMinutes { get; set; } = 15;
    public int StaleQuoteMinutes { get; set; } = 20;
    public string BenchmarkSymbol { get; set; } = "QQQ";
}

public sealed record MarketSnapshotBatchResult(
    int CandidateEvents,
    int EventsSelected,
    int Enqueued,
    int Claimed,
    int SnapshotsCreated,
    int Current,
    int MissingQuotes,
    int ItemFailures);

public class MarketSnapshotService(
    IServiceProvider services,
    IOptions<MarketSnapshotOptions> options,
    ILogger<MarketSnapshotService> logger) : BackgroundService
{
    private readonly MarketSnapshotOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(MarketSnapshotService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            MarketSnapshotBatchResult result;
            try
            {
                result = await CaptureBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Market snapshot batch failed");
                result = new MarketSnapshotBatchResult(0, 0, 0, 0, 0, 0, 0, 1);
            }

            var delay = result.SnapshotsCreated == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<MarketSnapshotBatchResult> CaptureBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.MarketSnapshots,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.BatchSize,
                _options.WorkBatchSize,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
                _options.RefreshWindowHours,
                _options.RefreshIntervalMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var captured = 0;
        var current = 0;
        var missingQuotes = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

            var now = DateTime.UtcNow;
            var eventCutoff = now.AddHours(-Math.Max(_options.RefreshWindowHours, 1));
            var snapshotCutoff = now.AddMinutes(-Math.Max(_options.RefreshIntervalMinutes, 1));

            var candidates = await db.Events
                .Include(e => e.Cluster)
                .Include(e => e.MarketSnapshots)
                .Where(e => e.Cluster != null && e.Cluster.Symbol != null && e.ExtractedAt >= eventCutoff)
                .OrderByDescending(e => e.Importance)
                .ThenByDescending(e => e.ExtractedAt)
                .Take(Math.Max(_options.BatchSize * 4, _options.BatchSize))
                .ToListAsync(cancellationToken);

            var selected = candidates
                .Where(e => ShouldSnapshot(e, snapshotCutoff))
                .Take(_options.BatchSize)
                .ToList();

            enqueued = await EnqueueWorkAsync(db, queue, selected, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.MarketSnapshot,
                Math.Max(1, _options.WorkBatchSize),
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<MarketSnapshotWorkHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Captured) captured++;
                    if (itemResult.Current) current++;
                    if (itemResult.MissingQuote) missingQuotes++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Market snapshot work item {WorkItemId} failed", work.Item.Id);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new MarketSnapshotBatchResult(
                candidates.Count,
                selected.Count,
                enqueued,
                claimed.Count,
                captured,
                current,
                missingQuotes,
                failures);

            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: captured, ErrorCount: failures),
                result,
                cancellationToken);

            if (captured > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "market_snapshots",
                    recordCount: captured,
                    metadata: result,
                    cancellationToken: cancellationToken);

                logger.LogInformation("Captured {Count} market snapshots", captured);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(
                runId,
                ex,
                metadata: new { enqueued, captured, current, missingQuotes, failures },
                cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        IReadOnlyList<Event> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
            return 0;

        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var activeKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.MarketSnapshot &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running))
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var active = activeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capacity = backlogLimit - active.Count;
        if (capacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var enqueued = 0;

        for (var i = 0; i < events.Count && enqueued < enqueueLimit; i++)
        {
            var clusterId = events[i].ClusterId;
            var naturalKey = clusterId.ToString();
            if (active.Contains(naturalKey))
                continue;

            var payload = JsonSerializer.Serialize(
                new { clusterId },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.MarketSnapshot,
                    NaturalKey: naturalKey,
                    PayloadJson: payload,
                    Priority: events.Count - i,
                    MaxAttempts: 3),
                cancellationToken);

            active.Add(naturalKey);
            enqueued++;
        }

        return enqueued;
    }

    private static bool ShouldSnapshot(Event ev, DateTime snapshotCutoff)
    {
        var latest = ev.MarketSnapshots.OrderByDescending(s => s.CapturedAt).FirstOrDefault();
        return latest is null || latest.CapturedAt <= snapshotCutoff;
    }
}
