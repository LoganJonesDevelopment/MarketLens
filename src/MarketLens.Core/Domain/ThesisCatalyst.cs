namespace MarketLens.Core.Entities;

public class ThesisCatalyst
{
    public int Id { get; set; }
    public Guid ThesisId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CatalystDate { get; set; }
    public string Metal { get; set; } = string.Empty;
    public string CatalystType { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public string? Outcome { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ResearchThesis? Thesis { get; set; }
}
