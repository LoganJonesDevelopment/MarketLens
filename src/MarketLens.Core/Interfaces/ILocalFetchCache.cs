using MarketLens.Core.Entities;

namespace MarketLens.Core.Interfaces;

public sealed record StoreLocalFetchCacheRequest(
    string CacheKey,
    string Url,
    string Source,
    bool Success,
    TimeSpan SuccessTtl,
    TimeSpan NegativeTtl,
    int? StatusCode = null,
    string? ContentType = null,
    string? ResponseText = null,
    string? ETag = null,
    string? LastModified = null,
    string? ErrorText = null,
    DateTime? FetchedAt = null);

public interface ILocalFetchCache
{
    Task<LocalFetchCacheEntry?> GetFreshAsync(
        string cacheKey,
        DateTime? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<LocalFetchCacheEntry> StoreAsync(
        StoreLocalFetchCacheRequest request,
        CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default);
}
