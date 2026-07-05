namespace MarketLens.Core.Interfaces;

public sealed record QuoteSnapshot(
    string Symbol,
    string? DisplayName,
    string? InstrumentType,
    string? Exchange,
    string? Currency,
    decimal? Last,
    decimal? PreviousClose,
    decimal? Change,
    decimal? ChangePercent,
    DateTime? AsOf,
    string Status,
    string? Error);

public interface IQuoteSource
{
    string Name { get; }

    Task<IReadOnlyList<QuoteSnapshot>> FetchAsync(
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default);
}
