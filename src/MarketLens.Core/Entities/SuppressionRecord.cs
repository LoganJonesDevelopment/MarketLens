namespace MarketLens.Core.Entities;

public class SuppressionRecord
{
    public Guid Id { get; set; }
    public Guid? ArticleId { get; set; }
    public Guid? ClusterId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public decimal? Confidence { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Url { get; set; }
    public string? Publisher { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime SuppressedAt { get; set; }
    public string RawPayload { get; set; } = "{}";
}
