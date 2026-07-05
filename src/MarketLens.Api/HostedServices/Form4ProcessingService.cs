using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class Form4ProcessingOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public int IdleIntervalSeconds { get; set; } = 180;
    public int InitialDelaySeconds { get; set; } = 35;
    public int BatchSize { get; set; } = 25;
    public int EnqueueBatchSize { get; set; } = 50;
    public int CandidateScanLimit { get; set; } = 200;
    public int RequestDelayMs { get; set; } = 150;
    public int BackfillLookbackDays { get; set; } = 90;
    public int BackfillCandidateScanLimit { get; set; } = 500;
    public int LeaseMinutes { get; set; } = 10;
}

public sealed record Form4ProcessingBatchResult(
    int Claimed,
    int Parsed,
    int Skipped,
    int NoXml,
    int ParseFailed,
    int ItemFailures);

public class Form4ProcessingService(
    IServiceProvider services,
    IConfiguration configuration,
    IOptions<Form4ProcessingOptions> options,
    ILogger<Form4ProcessingService> logger) : BackgroundService
{
    private readonly Form4ProcessingOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(Form4ProcessingService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            Form4ProcessingBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Form4ProcessingService: cycle failed");
                result = new Form4ProcessingBatchResult(0, 0, 0, 0, 0, 1);
            }

            var delay = result.Parsed == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Form4ProcessingBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

        await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
        await EnqueueCandidatesAsync(db, queue, cancellationToken);

        var claimed = await queue.ClaimBatchAsync(
            PipelineWorkTypes.Form4Processing,
            _options.BatchSize,
            _workerId,
            TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
            cancellationToken);

        if (claimed.Count == 0)
            return new Form4ProcessingBatchResult(0, 0, 0, 0, 0, 0);

        var parsed = 0;
        var skipped = 0;
        var noXml = 0;
        var parseFailed = 0;
        var itemFailures = 0;

        foreach (var work in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var articleId = Guid.Parse(work.Item.NaturalKey);

            try
            {
                using var itemScope = services.CreateScope();
                var handler = itemScope.ServiceProvider.GetRequiredService<Form4ProcessingHandler>();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                var itemResult = await handler.ProcessAsync(articleId, cancellationToken);
                switch (itemResult.Outcome)
                {
                    case Form4ProcessingOutcome.Ok: parsed++; break;
                    case Form4ProcessingOutcome.Skipped: skipped++; break;
                    case Form4ProcessingOutcome.NoXml: noXml++; break;
                    case Form4ProcessingOutcome.ParseFailed: parseFailed++; break;
                }

                await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                itemFailures++;
                logger.LogWarning(ex, "Form4ProcessingService: process failed for article {Id}", articleId);

                using var itemScope = services.CreateScope();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
            }

            if (_options.RequestDelayMs > 0)
                await Task.Delay(_options.RequestDelayMs, cancellationToken);
        }

        if (parsed > 0)
            logger.LogInformation("Form4ProcessingService: parsed {Count} Form 4 filings this cycle", parsed);

        return new Form4ProcessingBatchResult(claimed.Count, parsed, skipped, noXml, parseFailed, itemFailures);
    }

    private async Task EnqueueCandidatesAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backfillEnabled = string.Equals(
            configuration["BACKFILL_FORM4"], "true", StringComparison.OrdinalIgnoreCase);

        var cutoff = DateTime.UtcNow.AddDays(-_options.BackfillLookbackDays);
        var scanLimit = backfillEnabled
            ? Math.Max(_options.BackfillCandidateScanLimit, _options.EnqueueBatchSize)
            : Math.Max(_options.CandidateScanLimit, _options.EnqueueBatchSize);

        var query = db.Articles
            .AsNoTracking()
            .Where(a => a.Source == SourceNames.Edgar &&
                a.Url != null &&
                !db.InsiderTransactions.Any(t => t.ArticleId == a.Id));

        if (backfillEnabled)
            query = query.Where(a => a.PublishedAt >= cutoff);

        query = backfillEnabled
            ? query.OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.IngestedAt).ThenBy(a => a.Id);

        var scanned = await query
            .Take(scanLimit)
            .Select(a => new
            {
                a.Id,
                a.RawPayload,
                a.Headline,
                a.PublishedAt,
                a.IngestedAt,
            })
            .ToListAsync(cancellationToken);

        if (scanned.Count == 0) return;

        var naturalKeys = scanned.Select(a => a.Id.ToString()).ToList();
        var terminalKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(w => w.WorkType == PipelineWorkTypes.Form4Processing &&
                naturalKeys.Contains(w.NaturalKey) &&
                (w.Status == PipelineWorkStatuses.Completed || w.Status == PipelineWorkStatuses.DeadLetter))
            .Select(w => w.NaturalKey)
            .ToListAsync(cancellationToken);
        var terminalSet = terminalKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = scanned
            .Where(a => !terminalSet.Contains(a.Id.ToString()))
            .Where(a => Form4ProcessingHandler.IsForm4HeadlineOrForm(a.RawPayload, a.Headline))
            .Take(_options.EnqueueBatchSize)
            .ToList();

        foreach (var candidate in candidates)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.Form4Processing,
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
