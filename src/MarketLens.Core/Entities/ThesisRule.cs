namespace MarketLens.Core.Entities;

public class ThesisRule
{
    public Guid Id { get; set; }
    public Guid ThesisId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string AssetKeywords { get; set; } = "[]";
    public string ConceptKeywords { get; set; } = "[]";
    public string EventTypes { get; set; } = "[]";
    public string SourceNames { get; set; } = "[]";
    public string SourceTiers { get; set; } = "[]";
    public string ExcludeTerms { get; set; } = "[]";
    public decimal? MinArticleSimilarity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ResearchThesis? Thesis { get; set; }
    public ICollection<ResearchEvidence> Evidence { get; set; } = new List<ResearchEvidence>();
}
