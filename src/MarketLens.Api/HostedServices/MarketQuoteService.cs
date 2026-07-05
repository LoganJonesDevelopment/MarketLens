using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class MarketQuoteOptions
{
    public bool Enabled { get; set; } = true;
    public int InitialDelaySeconds { get; set; } = 20;
    public int IntervalSeconds { get; set; } = 60;
    public int WorkBatchSize { get; set; } = 8;
    public int EnqueueBatchSize { get; set; } = 25;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 5;
    public List<MarketQuoteSymbol> Symbols { get; set; } = new()
    {
        new() { Symbol = "SPY",   DisplayName = "S&P 500" },
        new() { Symbol = "I:NDX", DisplayName = "Nasdaq 100" },
        new() { Symbol = "IWM",   DisplayName = "Russell 2000" },
        new() { Symbol = "DIA",   DisplayName = "Dow Jones" },
        new() { Symbol = "VXX",   DisplayName = "VIX (ETF proxy)" },
        new() { Symbol = "TLT",   DisplayName = "Long bonds" },
        new() { Symbol = "GLD",   DisplayName = "Gold" },
        new() { Symbol = "USO",   DisplayName = "Oil" },
        new() { Symbol = "XLK",   DisplayName = "Technology" },
        new() { Symbol = "XLC",   DisplayName = "Communication" },
        new() { Symbol = "XLY",   DisplayName = "Discretionary" },
        new() { Symbol = "XLP",   DisplayName = "Staples" },
        new() { Symbol = "XLF",   DisplayName = "Financials" },
        new() { Symbol = "XLV",   DisplayName = "Health care" },
        new() { Symbol = "XLI",   DisplayName = "Industrials" },
        new() { Symbol = "XLE",   DisplayName = "Energy" },
        new() { Symbol = "XLB",   DisplayName = "Materials" },
        new() { Symbol = "XLU",   DisplayName = "Utilities" },
        new() { Symbol = "XLRE",  DisplayName = "Real estate" },
        new() { Symbol = "SMH",   DisplayName = "Semiconductors" },
    };
}

public class MarketQuoteSymbol
{
    public string Symbol { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public sealed record MarketQuoteBatchResult(
    int CandidateSymbols,
    int Enqueued,
    int Claimed,
    int Written,
    int Errors);

public class MarketQuoteService(
    IServiceProvider services,
    IOptions<MarketQuoteOptions> options,
    ILogger<MarketQuoteService> logger) : BackgroundService
{
    private readonly MarketQuoteOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(MarketQuoteService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.Symbols.Count == 0)
        {
            logger.LogInformation("MarketQuoteService disabled or no symbols configured");
            return;
        }

        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Market quote refresh failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(15, _options.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<MarketQuoteBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var symbolDefs = ResolveSymbols();

        var runId = await recorder.StartAsync(
            PipelineStages.MarketQuotes,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                symbolCount = symbolDefs.Count,
                _options.WorkBatchSize,
                _options.EnqueueBatchSize,
                _options.QueueBacklogLimit,
            },
            cancellationToken: cancellationToken);

        var enqueued = 0;
        var written = 0;
        var errors = 0;

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            enqueued = await EnqueueWorkAsync(db, queue, symbolDefs, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.MarketQuote,
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
                    var handler = itemScope.ServiceProvider.GetRequiredService<MarketQuoteWorkHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        work.Item.NaturalKey,
                        work.Item.PayloadJson,
                        cancellationToken);

                    if (itemResult.Written) written++;
                    if (itemResult.Error) errors++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    errors++;
                    logger.LogWarning(ex, "Market quote work item {WorkItemId} failed", work.Item.Id);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new MarketQuoteBatchResult(symbolDefs.Count, enqueued, claimed.Count, written, errors);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: written, ErrorCount: errors),
                result,
                cancellationToken);

            if (written > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "market_quotes",
                    recordCount: written,
                    metadata: result,
                    cancellationToken: cancellationToken);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, metadata: new { enqueued, written, errors }, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        IReadOnlyList<MarketQuoteSymbol> symbolDefs,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var activeKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.MarketQuote &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running))
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var active = activeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var capacity = backlogLimit - active.Count;
        if (capacity <= 0) return 0;

        var enqueueLimit = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var enqueued = 0;
        for (var i = 0; i < symbolDefs.Count && enqueued < enqueueLimit; i++)
        {
            var symbol = NormalizeSymbol(symbolDefs[i].Symbol);
            if (string.IsNullOrWhiteSpace(symbol) || active.Contains(symbol))
                continue;

            var payload = JsonSerializer.Serialize(
                new { symbol, symbolDefs[i].DisplayName },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.MarketQuote,
                    NaturalKey: symbol,
                    PayloadJson: payload,
                    Priority: symbolDefs.Count - i,
                    MaxAttempts: 3),
                cancellationToken);

            active.Add(symbol);
            enqueued++;
        }

        return enqueued;
    }

    private List<MarketQuoteSymbol> ResolveSymbols()
        => _options.Symbols
            .Where(s => !string.IsNullOrWhiteSpace(s.Symbol))
            .GroupBy(s => NormalizeSymbol(s.Symbol), StringComparer.OrdinalIgnoreCase)
            .Select(g => new MarketQuoteSymbol
            {
                Symbol = g.Key,
                DisplayName = g.First().DisplayName,
            })
            .ToList();

    private static string NormalizeSymbol(string symbol)
        => symbol.Trim().ToUpperInvariant();
}
