using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class PipelineEndpoints
{
    public static void MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pipeline/runs", async (
            MarketLensDbContext db,
            string? stage,
            string? status,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.PipelineRuns.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(stage))
                q = q.Where(r => r.Stage == stage);
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(r => r.Status == status);

            var limit = Math.Clamp(take ?? 50, 1, 250);
            var runs = await q
                .OrderByDescending(r => r.StartedAt)
                .Take(limit)
                .Select(r => new
                {
                    r.Id,
                    r.Stage,
                    r.ScopeType,
                    r.ScopeKey,
                    r.Trigger,
                    r.Status,
                    r.Attempt,
                    r.StartedAt,
                    r.FinishedAt,
                    durationMs = r.FinishedAt == null ? (double?)null : (r.FinishedAt.Value - r.StartedAt).TotalMilliseconds,
                    r.InputCount,
                    r.OutputCount,
                    r.SkippedCount,
                    r.ErrorCount,
                    r.ErrorCategory,
                    r.ErrorMessage,
                    metadata = r.MetadataJson,
                })
                .ToListAsync(ct);

            return Results.Ok(new { runs });
        });

        app.MapGet("/api/pipeline/stages", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var recent = await db.PipelineRuns
                .AsNoTracking()
                .OrderByDescending(r => r.StartedAt)
                .Take(500)
                .Select(r => new
                {
                    r.Stage,
                    r.Status,
                    r.StartedAt,
                    r.FinishedAt,
                    r.InputCount,
                    r.OutputCount,
                    r.ErrorCount,
                })
                .ToListAsync(ct);

            var stages = recent
                .GroupBy(r => r.Stage)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(r => r.StartedAt).First();
                    return new
                    {
                        stage = g.Key,
                        latestStatus = latest.Status,
                        latestStartedAt = latest.StartedAt,
                        latestFinishedAt = latest.FinishedAt,
                        runs = g.Count(),
                        failures = g.Count(r => r.Status is "failed" or "dead_letter"),
                        errorCount = g.Sum(r => r.ErrorCount),
                        inputCount = g.Sum(r => r.InputCount),
                        outputCount = g.Sum(r => r.OutputCount),
                    };
                })
                .OrderBy(s => s.stage)
                .ToList();

            return Results.Ok(new { stages });
        });

        app.MapGet("/api/pipeline/materializations", async (
            MarketLensDbContext db,
            string? assetKey,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.PipelineMaterializations.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(assetKey))
                q = q.Where(m => m.AssetKey == assetKey);

            var limit = Math.Clamp(take ?? 50, 1, 250);
            var materializations = await q
                .OrderByDescending(m => m.MaterializedAt)
                .Take(limit)
                .Select(m => new
                {
                    m.Id,
                    m.RunId,
                    m.AssetType,
                    m.AssetKey,
                    m.PartitionKey,
                    m.MaterializedAt,
                    m.RecordCount,
                    m.DataVersion,
                    metadata = m.MetadataJson,
                })
                .ToListAsync(ct);

            return Results.Ok(new { materializations });
        });

        app.MapPipelineQueueEndpoints();
        app.MapPipelineCacheEndpoints();
    }
}
