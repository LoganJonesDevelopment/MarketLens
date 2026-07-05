namespace MarketLens.Core.Entities;

public class LocalFetchCacheEntry
{
    public Guid Id { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public bool Success { get; set; }
    public string? ContentType { get; set; }
    public string? ResponseText { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public DateTime FetchedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? ErrorText { get; set; }
}
