using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IPriceBarSource
{
    string Name { get; }

    Task<PriceBarBatch?> FetchAsync(
        string symbol,
        string interval,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
