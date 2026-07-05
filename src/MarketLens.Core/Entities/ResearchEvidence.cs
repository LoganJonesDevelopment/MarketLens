namespace MarketLens.Core.Entities;

public class ResearchEvidence
{
    public Guid Id { get; set; }
    public Guid ThesisId { get; set; }
    public Guid? ThesisRuleId { get; set; }
    public Guid? ArticleId { get; set; }
    public Guid? ClusterId { get; set; }
    public string EvidenceType { get; set; } = "article";
    public string MatchKind { get; set; } = "manual";
    public string? MatchReason { get; set; }
    public decimal? Similarity { get; set; }
    public string Stance { get; set; } = "unknown";
    public decimal? StanceConfidence { get; set; }
    public string? StanceRationale { get; set; }
    public string? OriginalStance { get; set; }
    public decimal? OriginalStanceConfidence { get; set; }
    public string? StanceModel { get; set; }
    public string? StancePromptVersion { get; set; }
    public DateTime? ClassifiedAt { get; set; }
    public string ReviewStatus { get; set; } = "pending";
    public bool IsPinned { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? ReviewerNote { get; set; }
    public DateTime MatchedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public Guid? TranscriptSegmentId { get; set; }
    public Guid? ArticleChunkId { get; set; }

    public ResearchThesis? Thesis { get; set; }
    public ThesisRule? ThesisRule { get; set; }
    public Article? Article { get; set; }
    public Cluster? Cluster { get; set; }
    public TranscriptSegment? TranscriptSegment { get; set; }
    public ArticleChunk? ArticleChunk { get; set; }
}
