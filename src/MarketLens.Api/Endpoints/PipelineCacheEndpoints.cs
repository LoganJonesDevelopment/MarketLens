using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class PipelineCacheEndpoints
{
    private const int MaxCacheTake = 250;
    private const int MaxPurgeTake = 5_000;

    public static void MapPipelineCacheEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pipeline/cache/summary", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var countRows = await db.LocalFetchCacheEntries
                .AsNoTracking()
                .GroupBy(e => new { e.Source, e.Success, Fresh = e.ExpiresAt > now })
                .Select(g => new { g.Key.Source, g.Key.Success, g.Key.Fresh, Count = g.Count() })
                .OrderBy(c => c.Source)
                .ThenByDescending(c => c.Fresh)
                .ThenByDescending(c => c.Success)
                .ToListAsync(ct);
            var counts = countRows
                .Select(c => new PipelineCacheCountDto(c.Source, c.Success, c.Fresh, c.Count))
                .ToList();

            var expired = await db.LocalFetchCacheEntries
                .AsNoTracking()
                .CountAsync(e => e.ExpiresAt <= now, ct);

            return Results.Ok(new PipelineCacheSummaryDto(counts, expired));
        });

        app.MapGet("/api/pipeline/cache/entries", async (
            MarketLensDbContext db,
            string? source,
            bool? success,
            bool? freshOnly,
            string? q,
            int? take,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var query = db.LocalFetchCacheEntries.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(source))
                query = query.Where(e => e.Source == source);
            if (success is not null)
                query = query.Where(e => e.Success == success.Value);
            if (freshOnly == true)
                query = query.Where(e => e.ExpiresAt > now);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(e => e.CacheKey.Contains(term) || e.Url.Contains(term));
            }

            var limit = Math.Clamp(take ?? 50, 1, MaxCacheTake);
            var entries = await query
                .OrderByDescending(e => e.FetchedAt)
                .Take(limit)
                .Select(e => new PipelineCacheEntryDto(
                    e.Id,
                    e.CacheKey,
                    e.Source,
                    e.Url,
                    e.Success,
                    e.StatusCode,
                    e.ContentType,
                    e.FetchedAt,
                    e.ExpiresAt,
                    e.ExpiresAt > now,
                    e.ResponseText == null ? 0 : e.ResponseText.Length,
                    e.ErrorText))
                .ToListAsync(ct);

            return Results.Ok(new PipelineCacheEntriesDto(entries));
        });

        app.MapDelete("/api/pipeline/cache/entries/{id:guid}", async (
            MarketLensDbContext db,
            Guid id,
            CancellationToken ct) =>
        {
            var deleted = await db.LocalFetchCacheEntries
                .Where(e => e.Id == id)
                .ExecuteDeleteAsync(ct);

            return deleted == 0
                ? Results.NotFound()
                : Results.Ok(new PipelineCacheDeleteResult(deleted));
        });

        app.MapDelete("/api/pipeline/cache/source/{source}", async (
            MarketLensDbContext db,
            string source,
            CancellationToken ct) =>
        {
            var deleted = await db.LocalFetchCacheEntries
                .Where(e => e.Source == source)
                .ExecuteDeleteAsync(ct);

            return Results.Ok(new PipelineCacheDeleteResult(deleted));
        });

        app.MapPost("/api/pipeline/cache/purge-expired", async (
            MarketLensDbContext db,
            int? take,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var limit = Math.Clamp(take ?? 1_000, 1, MaxPurgeTake);
            var ids = await db.LocalFetchCacheEntries
                .AsNoTracking()
                .Where(e => e.ExpiresAt <= now)
                .OrderBy(e => e.ExpiresAt)
                .Take(limit)
                .Select(e => e.Id)
                .ToListAsync(ct);

            var deleted = ids.Count == 0
                ? 0
                : await db.LocalFetchCacheEntries
                    .Where(e => ids.Contains(e.Id))
                    .ExecuteDeleteAsync(ct);

            return Results.Ok(new PipelineCachePurgeResult(deleted, now, limit));
        });
    }

    private sealed record PipelineCacheCountDto(string Source, bool Success, bool Fresh, int Count);

    private sealed record PipelineCacheSummaryDto(
        IReadOnlyList<PipelineCacheCountDto> Counts,
        int ExpiredCount);

    private sealed record PipelineCacheEntryDto(
        Guid Id,
        string CacheKey,
        string Source,
        string Url,
        bool Success,
        int? StatusCode,
        string? ContentType,
        DateTime FetchedAt,
        DateTime ExpiresAt,
        bool Fresh,
        int ResponseTextLength,
        string? ErrorText);

    private sealed record PipelineCacheEntriesDto(IReadOnlyList<PipelineCacheEntryDto> Entries);

    private sealed record PipelineCacheDeleteResult(int Deleted);

    private sealed record PipelineCachePurgeResult(int Deleted, DateTime Cutoff, int Limit);
}
