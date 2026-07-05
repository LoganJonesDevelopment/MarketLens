namespace MarketLens.Core.Entities;

public class PipelineRun
{
    public Guid Id { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? ScopeType { get; set; }
    public string? ScopeKey { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempt { get; set; } = 1;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ErrorMessage { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public ICollection<PipelineMaterialization> Materializations { get; set; } = new List<PipelineMaterialization>();
}
