using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface INewsSource
{
    string Name { get; }
    Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default);
}
