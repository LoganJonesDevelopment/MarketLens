namespace MarketLens.Core.Entities;

public class PriceBar
{
    public string Symbol { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long? Volume { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
}
