using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapAssetEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/assets", async (
            MarketLensDbContext db,
            string? query,
            string? kind,
            int? take,
            CancellationToken ct) =>
        {
            var q = db.ResearchAssets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(kind))
                q = q.Where(a => a.Kind == kind);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var term = query.Trim().ToLower();
                q = q.Where(a =>
                    a.Name.ToLower().Contains(term) ||
                    (a.Symbol != null && a.Symbol.ToLower().Contains(term)));
            }

            var limit = Math.Clamp(take ?? 100, 1, 500);
            var items = await q
                .OrderBy(a => a.Kind)
                .ThenBy(a => a.Name)
                .Take(limit)
                .Select(a => new
                {
                    a.Id,
                    a.Kind,
                    a.Name,
                    a.Symbol,
                    a.Keywords,
                    a.CreatedAt,
                    a.UpdatedAt,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });

        group.MapPost("/assets", async (MarketLensDbContext db, UpsertAssetRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required" });

            var now = DateTime.UtcNow;
            var asset = new ResearchAsset
            {
                Id = Guid.NewGuid(),
                Kind = EmptyToNull(request.Kind) ?? "concept",
                Name = request.Name.Trim(),
                Symbol = NormalizeSymbol(request.Symbol),
                Keywords = ToJsonArray(request.Keywords),
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.ResearchAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/assets/{asset.Id}", asset);
        });

        group.MapPatch("/assets/{id:guid}", async (
            MarketLensDbContext db,
            Guid id,
            UpsertAssetRequest request,
            CancellationToken ct) =>
        {
            var asset = await db.ResearchAssets.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(request.Kind))
                asset.Kind = request.Kind.Trim();
            if (!string.IsNullOrWhiteSpace(request.Name))
                asset.Name = request.Name.Trim();
            if (request.Symbol is not null)
                asset.Symbol = NormalizeSymbol(request.Symbol);
            if (request.Keywords is not null)
                asset.Keywords = ToJsonArray(request.Keywords);

            asset.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(asset);
        });

        group.MapPost("/theses/{id:guid}/assets", async (
            MarketLensDbContext db,
            Guid id,
            LinkAssetRequest request,
            CancellationToken ct) =>
        {
            var thesisExists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            var assetExists = await db.ResearchAssets.AnyAsync(a => a.Id == request.AssetId, ct);
            if (!thesisExists || !assetExists) return Results.NotFound();

            var existing = await db.ThesisAssets.FindAsync([id, request.AssetId], ct);
            if (existing is not null)
            {
                existing.Role = EmptyToNull(request.Role) ?? existing.Role;
                await db.SaveChangesAsync(ct);
                return Results.Ok(existing);
            }

            var link = new ThesisAsset
            {
                ThesisId = id,
                AssetId = request.AssetId,
                Role = EmptyToNull(request.Role) ?? "subject",
                CreatedAt = DateTime.UtcNow,
            };
            db.ThesisAssets.Add(link);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/theses/{id}/assets/{request.AssetId}", link);
        });

        group.MapDelete("/theses/{id:guid}/assets/{assetId:guid}", async (
            MarketLensDbContext db,
            Guid id,
            Guid assetId,
            CancellationToken ct) =>
        {
            var link = await db.ThesisAssets.FindAsync([id, assetId], ct);
            if (link is null) return Results.NotFound();

            db.ThesisAssets.Remove(link);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
