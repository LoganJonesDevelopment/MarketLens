using Pgvector;

namespace MarketLens.Core.Entities;

public class TranscriptSegment
{
    public Guid Id { get; set; }
    public Guid TranscriptId { get; set; }
    public Transcript? Transcript { get; set; }

    public int SegmentIndex { get; set; }
    public float StartSeconds { get; set; }
    public float EndSeconds { get; set; }
    public string? Speaker { get; set; }
    public string Text { get; set; } = string.Empty;
    public Vector? Embedding { get; set; }
}
