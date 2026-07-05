using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class ArticlesEndpoints
{
    public static void MapArticlesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/articles/search", async (
            MarketLensDbContext db,
            string q,
            string? sourceTier,
            string? symbol,
            DateTime? from,
            DateTime? to,
            int? limit,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { error = "Query parameter 'q' is required." });

            var query = db.Articles.AsNoTracking().AsQueryable();

            var pattern = $"%{q}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Headline, pattern) ||
                (a.Summary != null && EF.Functions.ILike(a.Summary, pattern)));

            if (!string.IsNullOrWhiteSpace(sourceTier))
                query = query.Where(a => a.SourceTier == sourceTier);
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                var s = symbol.ToUpperInvariant();
                query = query.Where(a => a.Symbol == s);
            }
            if (from.HasValue)
                query = query.Where(a => a.PublishedAt >= from.Value);
            if (to.HasValue)
                query = query.Where(a => a.PublishedAt <= to.Value);

            var take = Math.Clamp(limit ?? 50, 1, 200);

            var items = await query
                .OrderByDescending(a => a.PublishedAt)
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.Headline,
                    a.Source,
                    a.SourceTier,
                    a.Symbol,
                    a.PublishedAt,
                    a.Url,
                    a.ClusterId,
                })
                .ToListAsync(ct);

            return Results.Ok(items);
        });
    }
}
