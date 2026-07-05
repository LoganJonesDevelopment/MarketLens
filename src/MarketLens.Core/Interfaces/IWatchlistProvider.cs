namespace MarketLens.Core.Interfaces;

public sealed record WatchedTicker(
    Guid AssetId,
    string Symbol,
    string Name,
    string? Cik,
    string? IrFeedUrl,
    IReadOnlyList<string> Aliases);

public interface IWatchlistProvider
{
    Task<IReadOnlyList<WatchedTicker>> GetWatchedTickersAsync(CancellationToken cancellationToken = default);
}
