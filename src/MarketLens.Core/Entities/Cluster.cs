namespace MarketLens.Core.Entities;

public class Cluster
{
    public Guid Id { get; set; }
    public string? Symbol { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int MemberCount { get; set; }
    public string DominantSourceTier { get; set; } = string.Empty;
    public decimal TopSourceWeight { get; set; }
    public string? TriageEventType { get; set; }
    public decimal? TriageConfidence { get; set; }

    public ICollection<Article> Articles { get; set; } = new List<Article>();
    public Event? Event { get; set; }
}
