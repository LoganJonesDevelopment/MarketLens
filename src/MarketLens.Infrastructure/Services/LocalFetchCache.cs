using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketLens.Infrastructure.Services;

public class LocalFetchCache(MarketLensDbContext db) : ILocalFetchCache
{
    private const int MaxErrorLength = 2048;

    public Task<LocalFetchCacheEntry?> GetFreshAsync(
        string cacheKey,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeRequired(cacheKey, nameof(cacheKey));
        var now = utcNow ?? DateTime.UtcNow;

        return db.LocalFetchCacheEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.CacheKey == key && e.ExpiresAt > now, cancellationToken);
    }

    public async Task<LocalFetchCacheEntry> StoreAsync(
        StoreLocalFetchCacheRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeRequired(request.CacheKey, nameof(request.CacheKey));
        var url = NormalizeRequired(request.Url, nameof(request.Url));
        var source = NormalizeRequired(request.Source, nameof(request.Source));
        var now = DateTime.UtcNow;
        var fetchedAt = request.FetchedAt ?? now;
        var expiresAt = LocalFetchCachePolicy.CalculateExpiresAt(
            fetchedAt,
            request.Success,
            request.SuccessTtl,
            request.NegativeTtl);

        var entry = await db.LocalFetchCacheEntries
            .SingleOrDefaultAsync(e => e.CacheKey == key, cancellationToken);

        if (entry is null)
        {
            entry = new LocalFetchCacheEntry
            {
                Id = Guid.NewGuid(),
                CacheKey = key,
                CreatedAt = now,
            };
            db.LocalFetchCacheEntries.Add(entry);
        }

        entry.Url = url;
        entry.Source = source;
        entry.StatusCode = request.StatusCode;
        entry.Success = request.Success;
        entry.ContentType = NormalizeOptional(request.ContentType);
        entry.ResponseText = request.ResponseText;
        entry.ETag = NormalizeOptional(request.ETag);
        entry.LastModified = NormalizeOptional(request.LastModified);
        entry.FetchedAt = fetchedAt;
        entry.ExpiresAt = expiresAt;
        entry.ErrorText = request.ErrorText is null ? null : Truncate(request.ErrorText, MaxErrorLength);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return entry;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && db.Entry(entry).State == EntityState.Added)
        {
            db.Entry(entry).State = EntityState.Detached;
            return await StoreAsync(request, cancellationToken);
        }
    }

    public async Task<int> DeleteExpiredAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        return await db.LocalFetchCacheEntries
            .Where(e => e.ExpiresAt <= utcNow)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
