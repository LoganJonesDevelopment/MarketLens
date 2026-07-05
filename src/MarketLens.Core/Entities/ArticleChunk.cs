using Pgvector;

namespace MarketLens.Core.Entities;

public class ArticleChunk
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public int ChunkIndex { get; set; }
    public string? Section { get; set; }
    public string Text { get; set; } = string.Empty;
    public Vector? Embedding { get; set; }
    public DateTime CreatedAt { get; set; }
}
