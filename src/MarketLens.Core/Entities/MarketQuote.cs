namespace MarketLens.Core.Entities;

public class MarketQuote
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? InstrumentType { get; set; }
    public string? Exchange { get; set; }
    public string? Currency { get; set; }
    public decimal? Last { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public DateTime? AsOf { get; set; }
    public DateTime IngestedAt { get; set; }
    public string Status { get; set; } = "ok";
    public string? Error { get; set; }
}
