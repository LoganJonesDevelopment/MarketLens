using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class PriceBarBackfillOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 45;
    public int IntervalSeconds { get; set; } = 300;
    public int IdleIntervalSeconds { get; set; } = 900;
    public int SymbolActivityWindowDays { get; set; } = 30;
    public int DailyHistoryDays { get; set; } = 5 * 365;
    public int HourlyHistoryDays { get; set; } = 60;
    public int IntradayHistoryDays { get; set; } = 14;
    public int RefreshDailyMinutes { get; set; } = 15;
    public int RefreshHourlyMinutes { get; set; } = 30;
    public int SymbolBatchSize { get; set; } = 12;
    public int WorkBatchSize { get; set; } = 12;
    public int EnqueueBatchSize { get; set; } = 12;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 10;
    public int InterFetchDelayMs { get; set; } = 400;
    public int GapLookbackDays { get; set; } = 10;
    public int MaxGapFillsPerItem { get; set; } = 2;
    public string[] Symbols { get; set; } = [];
    public string[] Intervals { get; set; } = ["1d"];
}

public sealed record PriceBarBackfillBatchResult(
    int CandidateSymbols,
    int Enqueued,
    int Claimed,
    int BarsFetched,
    int Current,
    int GapBarsFetched,
    int ItemFailures);

public class PriceBarBackfillService(
    IServiceProvider services,
    IOptions<PriceBarBackfillOptions> options,
    ILogger<PriceBarBackfillService> logger) : BackgroundService
{
    private readonly PriceBarBackfillOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(PriceBarBackfillService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            PriceBarBackfillBatchResult result;
            try { result = await RunOnceAsync(stoppingToken); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Price bar backfill batch failed");
                result = new PriceBarBackfillBatchResult(0, 0, 0, 0, 0, 0, 1);
            }

            var delay = result.BarsFetched + result.GapBarsFetched == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<PriceBarBackfillBatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var source = scope.ServiceProvider.GetRequiredService<IPriceBarSource>();

        var symbols = await ResolveTrackedSymbolsAsync(db, cancellationToken);
        var intervals = ResolveIntervals();
        var runId = await recorder.StartAsync(
            PipelineStages.PriceBarBackfill,
            PipelineTriggers.Backfill,
            metadata: new
            {
                symbolCount = symbols.Count,
                intervals,
                _options.SymbolBatchSize,
                _options.WorkBatchSize,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var barsFetched = 0;
        var gapBarsFetched = 0;
        var current = 0;
        var failures = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, queue, symbols, intervals, source.Name, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.PriceBarBackfill,
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
                    var handler = itemScope.ServiceProvider.GetRequiredService<PriceBarBackfillWorkHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    barsFetched += itemResult.BarsFetched;
                    gapBarsFetched += itemResult.GapBarsFetched;
                    if (itemResult.Current) current++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Price bar backfill work item {WorkItemId} failed", work.Item.Id);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new PriceBarBackfillBatchResult(
                symbols.Count,
                enqueued,
                claimed.Count,
                barsFetched,
                current,
                gapBarsFetched,
                failures);

            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: barsFetched + gapBarsFetched, ErrorCount: failures),
                result,
                cancellationToken);

            if (barsFetched + gapBarsFetched > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "price_bars",
                    recordCount: barsFetched + gapBarsFetched,
                    metadata: result,
                    cancellationToken: cancellationToken);

                logger.LogInformation("Backfilled {Count} price bars", barsFetched + gapBarsFetched);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(
                runId,
                ex,
                metadata: new { enqueued, barsFetched, gapBarsFetched, failures },
                cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> intervals,
        string provider,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0 || intervals.Count == 0)
            return 0;

        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var activeKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.PriceBarBackfill &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running))
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var active = activeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capacity = backlogLimit - active.Count;
        if (capacity <= 0) return 0;

        var now = DateTime.UtcNow;
        var deferredKeys = await db.PriceBarFetchStates
            .AsNoTracking()
            .Where(s => s.Provider == provider
                        && s.NextAttemptAt != null
                        && s.NextAttemptAt > now
                        && intervals.Contains(s.Interval)
                        && symbols.Contains(s.Symbol))
            .Select(s => s.Symbol + "|" + s.Interval)
            .ToListAsync(cancellationToken);
        var deferred = deferredKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var processCapacity = Math.Max(1, _options.WorkBatchSize) - active.Count;
        if (processCapacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Min(Math.Max(1, _options.EnqueueBatchSize), processCapacity), capacity);
        var symbolLimit = Math.Min(Math.Max(1, _options.SymbolBatchSize), symbols.Count);
        var enqueued = 0;
        var priority = symbolLimit * intervals.Count;
        var symbolsEnqueued = 0;

        foreach (var symbol in symbols)
        {
            var enqueuedForSymbol = false;
            foreach (var interval in intervals)
            {
                var naturalKey = $"{symbol}|{interval}";
                if (active.Contains(naturalKey) || deferred.Contains(naturalKey))
                    continue;

                var payload = JsonSerializer.Serialize(
                    new { symbol, interval },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                await queue.EnqueueAsync(
                    new EnqueueWorkRequest(
                        WorkType: PipelineWorkTypes.PriceBarBackfill,
                        NaturalKey: naturalKey,
                        PayloadJson: payload,
                        Priority: priority--,
                        MaxAttempts: 3),
                    cancellationToken);

                active.Add(naturalKey);
                enqueued++;
                enqueuedForSymbol = true;
                if (enqueued >= enqueueLimit)
                    return enqueued;
            }

            if (enqueuedForSymbol)
            {
                symbolsEnqueued++;
                if (symbolsEnqueued >= symbolLimit)
                    return enqueued;
            }
        }

        return enqueued;
    }

    private async Task<List<string>> ResolveTrackedSymbolsAsync(MarketLensDbContext db, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(_options.SymbolActivityWindowDays, 1));

        var configured = _options.Symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var watchlist = (await db.ResearchAssets
            .Where(a => a.Symbol != null && a.Kind == "ticker")
            .Select(a => a.Symbol!)
            .ToListAsync(cancellationToken))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var fromArticles = (await db.Articles
            .Where(a => a.Symbol != null && a.PublishedAt >= cutoff)
            .Select(a => a.Symbol!)
            .Distinct()
            .ToListAsync(cancellationToken))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var allSymbols = configured.Concat(watchlist).Concat(fromArticles).Distinct(StringComparer.Ordinal).ToList();
        if (allSymbols.Count == 0) return [];

        var primaryInterval = ResolveIntervals().FirstOrDefault() ?? "1d";
        var staleness = await db.PriceBars
            .Where(b => b.Interval == primaryInterval && allSymbols.Contains(b.Symbol))
            .GroupBy(b => b.Symbol)
            .Select(g => new { Symbol = g.Key, Latest = g.Max(b => b.Timestamp) })
            .ToDictionaryAsync(x => x.Symbol, x => (DateTime?)x.Latest, cancellationToken);

        DateTime StalenessKey(string s) => staleness.TryGetValue(s, out var ts) && ts.HasValue ? ts.Value : DateTime.MinValue;

        return allSymbols
            .OrderByDescending(s => configured.Contains(s) || watchlist.Contains(s))
            .ThenBy(StalenessKey)
            .ThenBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    private IReadOnlyList<string> ResolveIntervals()
        => (_options.Intervals.Length == 0 ? ["1d"] : _options.Intervals)
            .Select(PriceBarIntervals.Normalize)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
