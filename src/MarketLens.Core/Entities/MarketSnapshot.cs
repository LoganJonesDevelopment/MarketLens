namespace MarketLens.Core.Entities;

public class MarketSnapshot
{
    public Guid Id { get; set; }
    public Guid ClusterId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
    public DateTime? QuoteTime { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? MovePercent { get; set; }
    public string? BenchmarkSymbol { get; set; }
    public decimal? BenchmarkMovePercent { get; set; }
    public decimal? RelativeMovePercent { get; set; }
    public long? Volume { get; set; }
    public long? AverageVolume { get; set; }
    public decimal? RelativeVolume { get; set; }
    public decimal ReactionScore { get; set; }
    public bool IsAfterHours { get; set; }
    public bool IsStale { get; set; }
    public string RawPayload { get; set; } = "{}";

    public Event? Event { get; set; }
}
