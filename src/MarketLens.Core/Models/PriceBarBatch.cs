namespace MarketLens.Core.Models;

public sealed record PriceBarRow(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long? Volume);

public sealed record PriceBarBatch(
    string Symbol,
    string Interval,
    string Source,
    IReadOnlyList<PriceBarRow> Bars,
    string? ProviderSymbol = null);
