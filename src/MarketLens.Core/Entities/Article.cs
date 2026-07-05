using Pgvector;

namespace MarketLens.Core.Entities;

public class Article
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceTier { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Url { get; set; }
    public string? Publisher { get; set; }
    public DateTime PublishedAt { get; set; }
    public DateTime IngestedAt { get; set; }
    public string RawPayload { get; set; } = "{}";

    public Vector? Embedding { get; set; }

    public Guid? ClusterId { get; set; }
    public Cluster? Cluster { get; set; }
}
