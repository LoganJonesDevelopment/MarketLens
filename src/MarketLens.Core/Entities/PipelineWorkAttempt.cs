namespace MarketLens.Core.Entities;

public class PipelineWorkAttempt
{
    public Guid Id { get; set; }
    public Guid WorkItemId { get; set; }
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LeaseExpiresAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public PipelineWorkItem WorkItem { get; set; } = null!;
}
