using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class FilingChunkExtractionOptions
{
    public int BatchSize { get; set; } = 4;
    public int EnqueueBatchSize { get; set; } = 12;
    public int CandidateScanLimit { get; set; } = 80;
    public int BackfillCandidateScanLimit { get; set; } = 400;
    public int IntervalSeconds { get; set; } = 60;
    public int IdleIntervalSeconds { get; set; } = 120;
    public int InitialDelaySeconds { get; set; } = 25;
    public int TokensPerChunk { get; set; } = 500;
    public int LeaseMinutes { get; set; } = 10;
}

public sealed record FilingChunkExtractionBatchResult(int Claimed, int Processed, int ChunksCreated, int ItemFailures);

public class FilingChunkExtractionService(
    IServiceProvider services,
    IConfiguration configuration,
    IOptions<FilingChunkExtractionOptions> options,
    ILogger<FilingChunkExtractionService> logger) : BackgroundService
{
    private readonly FilingChunkExtractionOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(FilingChunkExtractionService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            FilingChunkExtractionBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FilingChunkExtractionService cycle failed");
                result = new FilingChunkExtractionBatchResult(0, 0, 0, 1);
            }

            var delay = result.Processed == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<FilingChunkExtractionBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

        await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
        await EnqueueCandidatesAsync(db, queue, cancellationToken);

        var claimed = await queue.ClaimBatchAsync(
            PipelineWorkTypes.FilingChunkExtraction,
            _options.BatchSize,
            _workerId,
            TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
            cancellationToken);

        if (claimed.Count == 0)
            return new FilingChunkExtractionBatchResult(0, 0, 0, 0);

        var processed = 0;
        var chunksCreated = 0;
        var itemFailures = 0;

        foreach (var work in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var articleId = Guid.Parse(work.Item.NaturalKey);

            try
            {
                using var itemScope = services.CreateScope();
                var handler = itemScope.ServiceProvider.GetRequiredService<FilingChunkExtractionHandler>();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                var itemResult = await handler.ProcessAsync(articleId, cancellationToken);
                if (itemResult.Processed)
                    processed++;
                chunksCreated += itemResult.ChunksCreated;

                await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                itemFailures++;
                logger.LogWarning(ex, "FilingChunkExtractionService: chunk failed for article {Id}", articleId);

                using var itemScope = services.CreateScope();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
            }
        }

        return new FilingChunkExtractionBatchResult(claimed.Count, processed, chunksCreated, itemFailures);
    }

    private async Task EnqueueCandidatesAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backfillEnabled = string.Equals(
            configuration["BACKFILL_FILING_CHUNKS"], "true", StringComparison.OrdinalIgnoreCase);

        var scanLimit = backfillEnabled
            ? Math.Max(_options.BackfillCandidateScanLimit, _options.EnqueueBatchSize)
            : Math.Max(_options.CandidateScanLimit, _options.EnqueueBatchSize);

        var query = db.Articles
            .AsNoTracking()
            .Where(a => a.Source == "edgar" &&
                a.Url != null &&
                !db.ArticleChunks.Any(c => c.ArticleId == a.Id));

        query = backfillEnabled
            ? query.OrderBy(a => a.PublishedAt).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.IngestedAt).ThenBy(a => a.Id);

        var scanned = await query
            .Take(scanLimit)
            .Select(a => new
            {
                a.Id,
                a.RawPayload,
                a.PublishedAt,
                a.IngestedAt,
            })
            .ToListAsync(cancellationToken);

        if (scanned.Count == 0) return;

        var naturalKeys = scanned.Select(a => a.Id.ToString()).ToList();
        var terminalKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(w => w.WorkType == PipelineWorkTypes.FilingChunkExtraction &&
                naturalKeys.Contains(w.NaturalKey) &&
                (w.Status == PipelineWorkStatuses.Completed || w.Status == PipelineWorkStatuses.DeadLetter))
            .Select(w => w.NaturalKey)
            .ToListAsync(cancellationToken);
        var terminalSet = terminalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = scanned
            .Where(a => !terminalSet.Contains(a.Id.ToString()))
            .Where(a => FilingChunkExtractionHandler.IsChunkableForm(
                FilingChunkExtractionHandler.ExtractForm(a.RawPayload)))
            .Take(_options.EnqueueBatchSize)
            .ToList();

        foreach (var candidate in candidates)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.FilingChunkExtraction,
                    NaturalKey: candidate.Id.ToString(),
                    PayloadJson: $$"""{"articleId":"{{candidate.Id}}"}""",
                    Priority: PriorityFromDate(backfillEnabled ? candidate.PublishedAt : candidate.IngestedAt)),
                cancellationToken);
        }
    }

    private static int PriorityFromDate(DateTime date)
    {
        var minutes = (date - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        return (int)Math.Clamp(minutes, 0, int.MaxValue);
    }
}
