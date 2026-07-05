namespace MarketLens.Core.Entities;

public class Event
{
    public Guid ClusterId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public decimal Sentiment { get; set; }
    public string Slots { get; set; } = "{}";
    public decimal Importance { get; set; }
    public decimal SourceWeight { get; set; }
    public decimal NoveltyWeight { get; set; }
    public decimal EventClassPrior { get; set; }
    public decimal MagnitudeSignal { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public DateTime ExtractedAt { get; set; }

    public Cluster? Cluster { get; set; }
    public ICollection<MarketSnapshot> MarketSnapshots { get; set; } = new List<MarketSnapshot>();
}
