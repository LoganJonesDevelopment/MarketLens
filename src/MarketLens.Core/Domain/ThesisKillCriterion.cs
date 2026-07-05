namespace MarketLens.Core.Entities;

public class ThesisKillCriterion
{
    public int Id { get; set; }
    public Guid ThesisId { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public string MonitoringKeywords { get; set; } = string.Empty;
    public string ThreatLevel { get; set; } = "dormant";
    public int ContradictingEvidenceCount { get; set; }
    public string? LastTriggeredReason { get; set; }
    public DateTime? LastEscalatedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ResearchThesis? Thesis { get; set; }
}
