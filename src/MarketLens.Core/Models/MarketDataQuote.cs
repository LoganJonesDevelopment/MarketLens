namespace MarketLens.Core.Models;

public sealed record MarketDataQuote(
    string Symbol,
    string Provider,
    DateTime CapturedAt,
    DateTime? QuoteTime,
    decimal? LastPrice,
    decimal? PreviousClose,
    decimal? OpenPrice,
    decimal? HighPrice,
    decimal? LowPrice,
    long? Volume,
    long? AverageVolume,
    string RawJson);
