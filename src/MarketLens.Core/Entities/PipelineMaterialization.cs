namespace MarketLens.Core.Entities;

public class PipelineMaterialization
{
    public Guid Id { get; set; }
    public Guid? RunId { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string AssetKey { get; set; } = string.Empty;
    public string? PartitionKey { get; set; }
    public DateTime MaterializedAt { get; set; }
    public int RecordCount { get; set; }
    public string? DataVersion { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public PipelineRun? Run { get; set; }
}
