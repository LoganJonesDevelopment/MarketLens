using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IStanceClassifier
{
    Task<StanceVerdict> ClassifyAsync(StanceContext context, CancellationToken cancellationToken = default);
}
