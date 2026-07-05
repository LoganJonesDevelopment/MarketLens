namespace MarketLens.Core.Entities;

public class ResearchSnapshot
{
    public Guid Id { get; set; }
    public Guid ThesisId { get; set; }
    public DateTime SnapshotAt { get; set; }
    public int EvidenceCount { get; set; }
    public DateTime? LatestEvidenceAt { get; set; }
    public string Summary { get; set; } = "{}";

    public ResearchThesis? Thesis { get; set; }
}
