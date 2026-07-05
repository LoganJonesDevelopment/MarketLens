using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class ThesisBootstrapWorkOptions
{
    public int BatchSize { get; set; } = 2;
    public int IntervalSeconds { get; set; } = 5;
    public int IdleIntervalSeconds { get; set; } = 15;
    public int InitialDelaySeconds { get; set; } = 10;
    public int LeaseMinutes { get; set; } = 30;
}

public sealed record ThesisBootstrapWorkBatchResult(int Claimed, int Processed, int PlansGenerated, int ItemFailures);

public class ThesisBootstrapWorkService(
    IServiceProvider services,
    IOptions<ThesisBootstrapWorkOptions> options,
    ILogger<ThesisBootstrapWorkService> logger) : BackgroundService
{
    private readonly ThesisBootstrapWorkOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(ThesisBootstrapWorkService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            ThesisBootstrapWorkBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ThesisBootstrapWorkService cycle failed");
                result = new ThesisBootstrapWorkBatchResult(0, 0, 0, 1);
            }

            var delay = result.Processed == 0
                ? TimeSpan.FromSeconds(_options.IdleIntervalSeconds)
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<ThesisBootstrapWorkBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

        await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);

        var claimed = await queue.ClaimBatchAsync(
            PipelineWorkTypes.ThesisBootstrap,
            Math.Max(1, _options.BatchSize),
            _workerId,
            TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
            cancellationToken);

        if (claimed.Count == 0)
            return new ThesisBootstrapWorkBatchResult(0, 0, 0, 0);

        var processed = 0;
        var plansGenerated = 0;
        var itemFailures = 0;

        foreach (var work in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var itemScope = services.CreateScope();
                var handler = itemScope.ServiceProvider.GetRequiredService<ThesisBootstrapWorkHandler>();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                var itemResult = await handler.ProcessAsync(
                    work.Item.NaturalKey,
                    work.Item.PayloadJson,
                    cancellationToken);

                if (itemResult.Processed)
                    processed++;
                if (itemResult.PlanGenerated)
                    plansGenerated++;
                if (itemResult.Error is not null)
                    itemFailures++;

                await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                itemFailures++;
                logger.LogWarning(ex, "Thesis bootstrap work item {ThesisId} failed", work.Item.NaturalKey);

                using var itemScope = services.CreateScope();
                var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
            }
        }

        return new ThesisBootstrapWorkBatchResult(claimed.Count, processed, plansGenerated, itemFailures);
    }
}
