namespace MarketLens.Core.Entities;

public class EconomicEvent
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public bool IsTimeSpecific { get; set; }
    public string Status { get; set; } = "scheduled";
    public string? Notes { get; set; }
    public Guid? ClusterId { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTime IngestedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Cluster? Cluster { get; set; }
}
