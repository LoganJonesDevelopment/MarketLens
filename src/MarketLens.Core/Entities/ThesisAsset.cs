namespace MarketLens.Core.Entities;

public class ThesisAsset
{
    public Guid ThesisId { get; set; }
    public Guid AssetId { get; set; }
    public string Role { get; set; } = "subject";
    public DateTime CreatedAt { get; set; }

    public ResearchThesis? Thesis { get; set; }
    public ResearchAsset? Asset { get; set; }
}
