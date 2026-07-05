namespace MarketLens.Core.Entities;

public class PipelineWorkItem
{
    public Guid Id { get; set; }
    public string WorkType { get; set; } = string.Empty;
    public string NaturalKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime AvailableAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public Guid? CurrentAttemptId { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeadLetteredAt { get; set; }
    public string? LastError { get; set; }

    public ICollection<PipelineWorkAttempt> Attempts { get; set; } = new List<PipelineWorkAttempt>();
}
