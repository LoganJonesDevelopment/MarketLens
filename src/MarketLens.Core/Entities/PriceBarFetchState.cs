namespace MarketLens.Core.Entities;

public class PriceBarFetchState
{
    public string Symbol { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderSymbol { get; set; }
    public string Status { get; set; } = "unknown";
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? EarliestFetchedAt { get; set; }
    public DateTime? LatestFetchedAt { get; set; }
    public int EmptyResultCount { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
}
