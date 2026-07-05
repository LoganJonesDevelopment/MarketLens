using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IEconomicCalendarSource
{
    string Name { get; }

    Task<IReadOnlyList<EconomicEventRecord>> FetchAsync(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<string>? symbols,
        CancellationToken cancellationToken = default);
}
