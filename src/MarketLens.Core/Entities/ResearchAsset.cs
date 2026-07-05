namespace MarketLens.Core.Entities;

public class ResearchAsset
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "concept";
    public string Name { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string Keywords { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ThesisAsset> ThesisAssets { get; set; } = new List<ThesisAsset>();
}
