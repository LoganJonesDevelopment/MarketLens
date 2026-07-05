namespace MarketLens.Core.Entities;

public class Transcript
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? CallType { get; set; }
    public DateTime? CallDate { get; set; }
    public string AudioUrl { get; set; } = string.Empty;
    public float? DurationSeconds { get; set; }
    public int? SegmentCount { get; set; }
    public string Status { get; set; } = TranscriptStatus.Queued;
    public DateTime IngestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }

    public Guid? ArticleId { get; set; }
    public Article? Article { get; set; }

    public ICollection<TranscriptSegment> Segments { get; set; } = [];
}

public static class TranscriptStatus
{
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
