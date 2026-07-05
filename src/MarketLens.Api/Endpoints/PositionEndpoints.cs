using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class PositionEndpoints
{
    public static void MapPositionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/research/positions");

        group.MapGet("/", async (
            MarketLensDbContext db,
            Guid? thesisId,
            CancellationToken ct) =>
        {
            var q = db.ThesisPositions.AsNoTracking().AsQueryable();

            if (thesisId.HasValue)
                q = q.Where(p => p.ThesisId == thesisId.Value);

            var items = await q
                .OrderBy(p => p.Metal)
                .ThenBy(p => p.Symbol)
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapGet("/portfolio", async (
            MarketLensDbContext db,
            CancellationToken ct) =>
        {
            var positions = await db.ThesisPositions.AsNoTracking().ToListAsync(ct);

            var byMetal = positions
                .GroupBy(p => p.Metal)
                .Select(g => new
                {
                    metal = g.Key,
                    totalTargetPct = g.Sum(p => p.TargetAllocationPct),
                    totalDeployedPct = g.Sum(p => p.TargetAllocationPct * p.DeployedPct / 100m),
                    positionCount = g.Count(),
                    positions = g.Select(p => new
                    {
                        p.Id,
                        p.Symbol,
                        p.TargetAllocationPct,
                        p.DeployedPct,
                        p.EntryPrice,
                        p.EntryDate,
                        p.ScaleInTriggerPrice,
                        p.Status,
                    }).ToList(),
                })
                .OrderByDescending(g => g.totalTargetPct)
                .ToList();

            var totalTarget = positions.Sum(p => p.TargetAllocationPct);
            var totalDeployed = positions.Sum(p => p.TargetAllocationPct * p.DeployedPct / 100m);

            return Results.Ok(new
            {
                totalTargetPct = totalTarget,
                totalDeployedPct = totalDeployed,
                reservedPct = totalTarget - totalDeployed,
                metals = byMetal,
            });
        });

        group.MapPost("/", async (
            MarketLensDbContext db,
            ThesisPositionRequest req,
            CancellationToken ct) =>
        {
            var position = new ThesisPosition
            {
                ThesisId = req.ThesisId,
                Symbol = req.Symbol,
                Metal = req.Metal,
                TargetAllocationPct = req.TargetAllocationPct,
                DeployedPct = req.DeployedPct ?? 0m,
                EntryPrice = req.EntryPrice,
                EntryDate = req.EntryDate,
                ScaleInTriggerPrice = req.ScaleInTriggerPrice,
                ScaleInNotes = req.ScaleInNotes,
                Status = req.Status ?? "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            db.ThesisPositions.Add(position);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/research/positions/{position.Id}", position);
        });

        group.MapPut("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            ThesisPositionUpdateRequest req,
            CancellationToken ct) =>
        {
            var position = await db.ThesisPositions.FindAsync([id], ct);
            if (position is null) return Results.NotFound();

            if (req.TargetAllocationPct.HasValue) position.TargetAllocationPct = req.TargetAllocationPct.Value;
            if (req.DeployedPct.HasValue) position.DeployedPct = req.DeployedPct.Value;
            if (req.EntryPrice.HasValue) position.EntryPrice = req.EntryPrice.Value;
            if (req.EntryDate.HasValue) position.EntryDate = req.EntryDate.Value;
            if (req.ScaleInTriggerPrice.HasValue) position.ScaleInTriggerPrice = req.ScaleInTriggerPrice.Value;
            if (req.ScaleInNotes is not null) position.ScaleInNotes = req.ScaleInNotes;
            if (req.Status is not null) position.Status = req.Status;

            position.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(position);
        });

        group.MapDelete("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            CancellationToken ct) =>
        {
            var position = await db.ThesisPositions.FindAsync([id], ct);
            if (position is null) return Results.NotFound();

            db.ThesisPositions.Remove(position);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

public record ThesisPositionRequest(
    Guid ThesisId,
    string Symbol,
    string Metal,
    decimal TargetAllocationPct,
    decimal? DeployedPct,
    decimal? EntryPrice,
    DateTime? EntryDate,
    decimal? ScaleInTriggerPrice,
    string? ScaleInNotes,
    string? Status);

public record ThesisPositionUpdateRequest(
    decimal? TargetAllocationPct,
    decimal? DeployedPct,
    decimal? EntryPrice,
    DateTime? EntryDate,
    decimal? ScaleInTriggerPrice,
    string? ScaleInNotes,
    string? Status);
