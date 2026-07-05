using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class EconomicCalendarOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 60;
    public int RefreshIntervalSeconds { get; set; } = 6 * 60 * 60;
    public int LookbackDays { get; set; } = 30;
    public int LookaheadDays { get; set; } = 90;
    public int SymbolLookbackDays { get; set; } = 30;
    public int SourceBatchSize { get; set; } = 2;
    public int QueueBacklogLimit { get; set; } = 20;
    public int LeaseMinutes { get; set; } = 30;
}

public sealed record EconomicCalendarBatchResult(
    int Enqueued,
    int Claimed,
    int SourcesProcessed,
    int RecordsFetched,
    int Upserts,
    int ItemFailures);

public class EconomicCalendarService(
    IServiceProvider services,
    IOptions<EconomicCalendarOptions> options,
    ILogger<EconomicCalendarService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EconomicCalendarOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(EconomicCalendarService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunCycleAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Economic calendar refresh failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(_options.RefreshIntervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<EconomicCalendarBatchResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var sources = scope.ServiceProvider.GetServices<IEconomicCalendarSource>().ToList();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        if (sources.Count == 0)
            return new EconomicCalendarBatchResult(0, 0, 0, 0, 0, 0);

        var now = DateTime.UtcNow;
        var fromUtc = now.AddDays(-Math.Max(_options.LookbackDays, 1));
        var toUtc = now.AddDays(Math.Max(_options.LookaheadDays, 1));

        var runId = await recorder.StartAsync(
            PipelineStages.EconomicCalendar,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.LookbackDays,
                _options.LookaheadDays,
                _options.SourceBatchSize,
                _options.QueueBacklogLimit,
                _options.LeaseMinutes,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var processed = 0;
        var fetched = 0;
        var upserts = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueSourceWorkAsync(db, queue, sources, fromUtc, toUtc, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.EconomicCalendar,
                Math.Max(1, _options.SourceBatchSize),
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<EconomicCalendarSourceHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.SourceFound)
                        processed++;
                    fetched += itemResult.RecordsFetched;
                    upserts += itemResult.Upserts;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Economic calendar work item {Source} failed", work.Item.NaturalKey);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new EconomicCalendarBatchResult(enqueued, claimed.Count, processed, fetched, upserts, failures);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: upserts, ErrorCount: failures),
                result,
                cancellationToken);

            if (upserts > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "economic_events",
                    recordCount: upserts,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, processed, fetched, upserts, failures }, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueSourceWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        IReadOnlyList<IEconomicCalendarSource> sources,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.EconomicCalendar &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var enqueued = 0;
        foreach (var source in sources.Take(capacity))
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.EconomicCalendar,
                    NaturalKey: source.Name,
                    PayloadJson: JsonSerializer.Serialize(new
                    {
                        fromUtc,
                        toUtc,
                        symbolLookbackDays = Math.Max(1, _options.SymbolLookbackDays),
                    }, JsonOptions),
                    Priority: 0),
                cancellationToken);
            enqueued++;
        }

        return enqueued;
    }
}
