using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IMarketDataClient
{
    Task<MarketDataQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
}
