using MarketLens.Core.Domain;
using MarketLens.Api.Services;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class StanceClassificationOptions
{
    public int BatchSize { get; set; } = 6;
    public int IntervalSeconds { get; set; } = 5;
    public int IdleIntervalSeconds { get; set; } = 60;
    public int InitialDelaySeconds { get; set; } = 25;
    public int LeaseMinutes { get; set; } = 5;
}

public sealed record StanceClassificationBatchResult(int Processed, int ErrorClassifications);

public class StanceClassificationService(
    IServiceProvider services,
    IOptions<StanceClassificationOptions> options,
    IQuietHoursPolicy quietHours,
    ILogger<StanceClassificationService> logger) : BackgroundService
{
    private readonly StanceClassificationOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(StanceClassificationService)}:{Environment.MachineName}:{Environment.ProcessId}";
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
                    logger.LogInformation("Quiet hours — stance classification paused");
                    _wasQuiet = true;
                }
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            if (_wasQuiet)
            {
                logger.LogInformation("Quiet hours ended — stance classification resumed");
                _wasQuiet = false;
            }

            StanceClassificationBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stance classification batch failed");
                result = new StanceClassificationBatchResult(0, 1);
            }

            var delay = result.Processed == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<StanceClassificationBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.StanceClassification,
            PipelineTriggers.Scheduled,
            metadata: new { _options.BatchSize },
            cancellationToken: cancellationToken);

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

            var candidates = await db.ResearchEvidence
                .AsNoTracking()
                .Where(e => e.ClassifiedAt == null && e.Thesis != null)
                .OrderByDescending(e => e.MatchedAt)
                .Take(Math.Max(_options.BatchSize * 4, _options.BatchSize))
                .Select(e => new { e.Id, e.MatchedAt })
                .ToListAsync(cancellationToken);

            foreach (var candidate in candidates)
            {
                await queue.EnqueueAsync(
                    new EnqueueWorkRequest(
                        WorkType: PipelineWorkTypes.StanceClassification,
                        NaturalKey: candidate.Id.ToString(),
                        PayloadJson: $$"""{"evidenceId":"{{candidate.Id}}"}""",
                        Priority: PriorityFromMatchedAt(candidate.MatchedAt)),
                    cancellationToken);
            }

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.StanceClassification,
                _options.BatchSize,
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            if (claimed.Count == 0)
            {
                var empty = new StanceClassificationBatchResult(0, 0);
                await recorder.SucceedAsync(runId, new PipelineRunCounts(), empty, cancellationToken);
                return empty;
            }

            var processed = 0;
            var errorClassifications = 0;
            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evidenceId = Guid.Parse(work.Item.NaturalKey);

                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<StanceClassificationHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(evidenceId, cancellationToken);
                    if (itemResult.Processed)
                        processed++;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Stance classification failed for evidence {EvidenceId} — will retry next cycle", evidenceId);
                    errorClassifications++;

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new StanceClassificationBatchResult(processed, errorClassifications);
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: claimed.Count, OutputCount: processed, ErrorCount: errorClassifications),
                result,
                cancellationToken);
            if (processed > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "research_evidence_stance",
                    recordCount: processed,
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

    private static int PriorityFromMatchedAt(DateTime matchedAt)
    {
        var minutes = (matchedAt - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        return (int)Math.Clamp(minutes, 0, int.MaxValue);
    }
}
