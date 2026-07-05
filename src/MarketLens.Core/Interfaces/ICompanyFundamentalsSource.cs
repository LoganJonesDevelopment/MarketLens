using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface ICompanyFundamentalsSource
{
    string Name { get; }

    Task<CompanyFundamentalsSnapshot?> FetchAsync(
        string symbol,
        CancellationToken cancellationToken = default);
}
