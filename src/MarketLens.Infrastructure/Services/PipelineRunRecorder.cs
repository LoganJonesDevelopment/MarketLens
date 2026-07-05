using System.Net.Http;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Services;

public sealed record PipelineRunCounts(
    int InputCount = 0,
    int OutputCount = 0,
    int SkippedCount = 0,
    int ErrorCount = 0);

public class PipelineRunRecorder(
    MarketLensDbContext db,
    ILogger<PipelineRunRecorder> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid?> StartAsync(
        string stage,
        string trigger = PipelineTriggers.Scheduled,
        string? scopeType = null,
        string? scopeKey = null,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var run = new PipelineRun
            {
                Id = Guid.NewGuid(),
                Stage = stage,
                ScopeType = scopeType,
                ScopeKey = scopeKey,
                Trigger = trigger,
                Status = PipelineRunStatuses.Running,
                StartedAt = DateTime.UtcNow,
                MetadataJson = ToJson(metadata),
            };

            db.PipelineRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
            return run.Id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start pipeline run for {Stage}", stage);
            return null;
        }
    }

    public async Task SucceedAsync(
        Guid? runId,
        PipelineRunCounts counts,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (runId is null) return;

        try
        {
            var run = await db.PipelineRuns.FindAsync([runId.Value], cancellationToken);
            if (run is null) return;

            run.Status = counts.ErrorCount > 0
                ? PipelineRunStatuses.SucceededWithErrors
                : PipelineRunStatuses.Succeeded;
            run.FinishedAt = DateTime.UtcNow;
            run.InputCount = counts.InputCount;
            run.OutputCount = counts.OutputCount;
            run.SkippedCount = counts.SkippedCount;
            run.ErrorCount = counts.ErrorCount;
            run.ErrorCategory = null;
            run.ErrorMessage = null;
            run.MetadataJson = ToJson(metadata);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to complete pipeline run {RunId}", runId);
        }
    }

    public async Task FailAsync(
        Guid? runId,
        Exception exception,
        object? metadata = null,
        bool deadLetter = false,
        CancellationToken cancellationToken = default)
    {
        if (runId is null) return;

        try
        {
            var run = await db.PipelineRuns.FindAsync([runId.Value], cancellationToken);
            if (run is null) return;

            run.Status = deadLetter ? PipelineRunStatuses.DeadLetter : PipelineRunStatuses.Failed;
            run.FinishedAt = DateTime.UtcNow;
            run.ErrorCount = Math.Max(run.ErrorCount, 1);
            run.ErrorCategory = Categorize(exception);
            run.ErrorMessage = Truncate(exception.Message, 2048);
            run.MetadataJson = ToJson(metadata);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark pipeline run {RunId} failed", runId);
        }
    }

    public async Task RecordMaterializationAsync(
        Guid? runId,
        string assetType,
        string assetKey,
        int recordCount,
        string? partitionKey = null,
        string? dataVersion = null,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            db.PipelineMaterializations.Add(new PipelineMaterialization
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                AssetType = assetType,
                AssetKey = assetKey,
                PartitionKey = partitionKey,
                MaterializedAt = DateTime.UtcNow,
                RecordCount = Math.Max(recordCount, 0),
                DataVersion = dataVersion,
                MetadataJson = ToJson(metadata),
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record materialization for {AssetKey}", assetKey);
        }
    }

    public static string Categorize(Exception exception) => exception switch
    {
        TaskCanceledException => PipelineErrorCategories.Transient,
        TimeoutException => PipelineErrorCategories.Transient,
        OperationCanceledException => PipelineErrorCategories.Cancelled,
        HttpRequestException => PipelineErrorCategories.Transient,
        DbUpdateException => PipelineErrorCategories.Database,
        _ => PipelineErrorCategories.Unexpected,
    };

    private static string ToJson(object? value)
        => value is null ? "{}" : JsonSerializer.Serialize(value, JsonOptions);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
