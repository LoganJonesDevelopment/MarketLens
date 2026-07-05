namespace MarketLens.Core.Entities;

public class ThesisPosition
{
    public int Id { get; set; }
    public Guid ThesisId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Metal { get; set; } = string.Empty;
    public decimal TargetAllocationPct { get; set; }
    public decimal DeployedPct { get; set; }
    public decimal? EntryPrice { get; set; }
    public DateTime? EntryDate { get; set; }
    public decimal? ScaleInTriggerPrice { get; set; }
    public string? ScaleInNotes { get; set; }
    public string Status { get; set; } = "planned";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ResearchThesis? Thesis { get; set; }
}
