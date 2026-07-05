namespace MarketLens.Core.Entities;

public class IdeaMemo
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public int WindowDays { get; set; }
    public string EvidenceHash { get; set; } = string.Empty;
    public string Status { get; set; } = IdeaMemoStatuses.Pending;
    public string EvidenceJson { get; set; } = "{}";
    public string? MemoJson { get; set; }
    public string? ModelName { get; set; }
    public string? PromptVersion { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class IdeaMemoStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Superseded = "superseded";
}
