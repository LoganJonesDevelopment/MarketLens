namespace MarketLens.Core.Entities;

public class SourceCursorState
{
    public Guid Id { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string CursorJson { get; set; } = "{}";
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastSucceededAt { get; set; }
    public DateTime? LastFailedAt { get; set; }
    public DateTime? LastItemTimestamp { get; set; }
    public string? LastItemId { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? NextEligibleRunAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
