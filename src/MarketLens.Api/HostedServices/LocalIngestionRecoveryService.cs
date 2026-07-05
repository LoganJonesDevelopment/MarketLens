using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.HostedServices;

public sealed class LocalIngestionRecoveryService(
    IServiceProvider services,
    ILogger<LocalIngestionRecoveryService> logger) : IHostedService
{
    private static readonly TimeSpan StaleTranscriptAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan StaleIdeaMemoAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan StalePipelineRunAge = TimeSpan.FromHours(24);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();

        var transcriptCutoff = now.Subtract(StaleTranscriptAge);
        var memoCutoff = now.Subtract(StaleIdeaMemoAge);
        var pipelineRunCutoff = now.Subtract(StalePipelineRunAge);

        var recoveredTranscripts = await db.Transcripts
            .Where(t => t.Status == TranscriptStatus.Processing && t.IngestedAt <= transcriptCutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Status, TranscriptStatus.Queued)
                .SetProperty(t => t.CompletedAt, (DateTime?)null)
                .SetProperty(t => t.Error, "Recovered on startup: previous local transcript ingestion process stopped while this row was processing."),
                cancellationToken);

        var recoveredIdeaMemos = await db.IdeaMemos
            .Where(m => m.Status == IdeaMemoStatuses.Running &&
                ((m.StartedAt != null && m.StartedAt <= memoCutoff) ||
                 (m.StartedAt == null && m.UpdatedAt <= memoCutoff)))
            .ExecuteUpdateAsync(setters => setters
                // Local process death leaves no evidence that generation is invalid; requeue for one normal retry.
                .SetProperty(m => m.Status, IdeaMemoStatuses.Pending)
                .SetProperty(m => m.StartedAt, (DateTime?)null)
                .SetProperty(m => m.GeneratedAt, (DateTime?)null)
                .SetProperty(m => m.CompletedAt, (DateTime?)null)
                .SetProperty(m => m.UpdatedAt, now)
                .SetProperty(m => m.Error, "Recovered on startup: previous local idea memo generation process stopped while this row was running."),
                cancellationToken);

        var failedPipelineRuns = await db.PipelineRuns
            .Where(r => r.Status == PipelineRunStatuses.Running &&
                r.FinishedAt == null &&
                r.StartedAt <= pipelineRunCutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, PipelineRunStatuses.Failed)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.ErrorCount, r => Math.Max(r.ErrorCount, 1))
                .SetProperty(r => r.ErrorCategory, PipelineErrorCategories.Cancelled)
                .SetProperty(r => r.ErrorMessage, "Recovered on startup: previous local process stopped before this pipeline run finished."),
                cancellationToken);

        if (recoveredTranscripts > 0 || recoveredIdeaMemos > 0 || failedPipelineRuns > 0)
        {
            logger.LogWarning(
                "Recovered stale local ingestion rows on startup: {Transcripts} transcripts, {IdeaMemos} idea memos, {PipelineRuns} pipeline runs",
                recoveredTranscripts,
                recoveredIdeaMemos,
                failedPipelineRuns);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
