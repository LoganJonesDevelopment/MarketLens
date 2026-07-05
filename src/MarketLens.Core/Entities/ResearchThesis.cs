using Pgvector;

namespace MarketLens.Core.Entities;

public class ResearchThesis
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string PositionIntent { get; set; } = "none";
    public string? PositionThesis { get; set; }
    public DateTime? PositionUpdatedAt { get; set; }
    public string ThesisText { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public Vector? Embedding { get; set; }
    public string? Plan { get; set; }
    public string? PlanModel { get; set; }
    public string? PlanPromptVersion { get; set; }
    public DateTime? PlanGeneratedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastSegmentMatchedAt { get; set; }
    public DateTime? LastChunkMatchedAt { get; set; }

    public ICollection<ThesisAsset> ThesisAssets { get; set; } = new List<ThesisAsset>();
    public ICollection<ThesisRule> Rules { get; set; } = new List<ThesisRule>();
    public ICollection<ResearchEvidence> Evidence { get; set; } = new List<ResearchEvidence>();
    public ICollection<ResearchSnapshot> Snapshots { get; set; } = new List<ResearchSnapshot>();
}
