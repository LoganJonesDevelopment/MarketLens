using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class TranscriptIngestionOptions
{
    public int BatchSize { get; set; } = 2;
    public int EnqueueBatchSize { get; set; } = 10;
    public int IntervalSeconds { get; set; } = 10;
    public int IdleIntervalSeconds { get; set; } = 30;
    public int InitialDelaySeconds { get; set; } = 15;
    public int LeaseMinutes { get; set; } = 90;
}

public sealed record TranscriptIngestionBatchResult(int Claimed, int Processed, int SegmentsCreated, int ItemFailures);

public class TranscriptIngestionService(
    IServiceProvider services,
    IOptions<TranscriptIngestionOptions> options,
    ILogger<TranscriptIngestionService> logger) : BackgroundService
{
    private readonly TranscriptIngestionOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(TranscriptIngestionService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            TranscriptIngestionBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transcript ingestion cycle failed");
                result = new TranscriptIngestionBatchResult(0, 0, 0, 1);
            }

            var delay = result.Processed == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<TranscriptIngestionBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

        await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

        var candidates = await db.Transcripts
            .AsNoTracking()
            .Where(t => t.Status == TranscriptStatus.Queued)
            .OrderBy(t => t.IngestedAt)
            .Take(Math.Max(_options.EnqueueBatchSize, _options.BatchSize))
            .Select(t => new { t.Id, t.IngestedAt })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.TranscriptIngestion,
                    NaturalKey: candidate.Id.ToString(),
                    PayloadJson: $$"""{"transcriptId":"{{candidate.Id}}"}""",
                    Priority: PriorityFromAge(candidate.IngestedAt)),
                cancellationToken);
        }

        var claimed = await queue.ClaimBatchAsync(
            PipelineWorkTypes.TranscriptIngestion,
            _options.BatchSize,
            _workerId,
            TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
            cancellationToken);

        if (claimed.Count == 0)
            return new TranscriptIngestionBatchResult(0, 0, 0, 0);

        var processed = 0;
        var segmentsCreated = 0;
        var itemFailures = 0;

        foreach (var work in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transcriptId = Guid.Parse(work.Item.NaturalKey);

            try
            {
                using var itemScope = services.CreateScope();
                var handler = itemScope.ServiceProvider.GetRequiredService<TranscriptIngestionHandler>();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                var itemResult = await handler.ProcessAsync(transcriptId, cancellationToken);
                if (itemResult.Processed)
                {
                    processed++;
                    segmentsCreated += itemResult.SegmentsCreated;
                }

                await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                itemFailures++;
                logger.LogError(ex, "Transcript {Id} failed", transcriptId);

                using var itemScope = services.CreateScope();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
            }
        }

        return new TranscriptIngestionBatchResult(claimed.Count, processed, segmentsCreated, itemFailures);
    }

    private static int PriorityFromAge(DateTime ingestedAt)
    {
        var minutes = (DateTime.UtcNow - ingestedAt).TotalMinutes;
        return (int)Math.Clamp(minutes, 0, int.MaxValue);
    }
}
