using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class CatalystEndpoints
{
    public static void MapCatalystEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/research/catalysts");

        group.MapGet("/", async (
            MarketLensDbContext db,
            Guid? thesisId,
            CancellationToken ct) =>
        {
            var q = db.ThesisCatalysts.AsNoTracking()
                .Where(c => !c.Resolved && c.CatalystDate >= DateTime.UtcNow.Date);

            if (thesisId.HasValue)
                q = q.Where(c => c.ThesisId == thesisId.Value);

            var items = await q
                .OrderBy(c => c.CatalystDate)
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapPost("/", async (
            MarketLensDbContext db,
            ThesisCatalystRequest req,
            CancellationToken ct) =>
        {
            var catalyst = new ThesisCatalyst
            {
                ThesisId = req.ThesisId,
                Title = req.Title,
                Description = req.Description,
                CatalystDate = req.CatalystDate,
                Metal = req.Metal,
                CatalystType = req.CatalystType,
                CreatedAt = DateTime.UtcNow,
            };

            db.ThesisCatalysts.Add(catalyst);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/research/catalysts/{catalyst.Id}", catalyst);
        });

        group.MapPut("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            ThesisCatalystUpdateRequest req,
            CancellationToken ct) =>
        {
            var catalyst = await db.ThesisCatalysts.FindAsync([id], ct);
            if (catalyst is null) return Results.NotFound();

            if (req.Title is not null) catalyst.Title = req.Title;
            if (req.Description is not null) catalyst.Description = req.Description;
            if (req.CatalystDate.HasValue) catalyst.CatalystDate = req.CatalystDate.Value;
            if (req.Metal is not null) catalyst.Metal = req.Metal;
            if (req.CatalystType is not null) catalyst.CatalystType = req.CatalystType;
            if (req.Resolved.HasValue) catalyst.Resolved = req.Resolved.Value;
            if (req.Outcome is not null) catalyst.Outcome = req.Outcome;

            await db.SaveChangesAsync(ct);
            return Results.Ok(catalyst);
        });

        group.MapDelete("/{id:int}", async (
            MarketLensDbContext db,
            int id,
            CancellationToken ct) =>
        {
            var catalyst = await db.ThesisCatalysts.FindAsync([id], ct);
            if (catalyst is null) return Results.NotFound();

            db.ThesisCatalysts.Remove(catalyst);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

public record ThesisCatalystRequest(
    Guid ThesisId,
    string Title,
    string? Description,
    DateTime CatalystDate,
    string Metal,
    string CatalystType);

public record ThesisCatalystUpdateRequest(
    string? Title,
    string? Description,
    DateTime? CatalystDate,
    string? Metal,
    string? CatalystType,
    bool? Resolved,
    string? Outcome);
