using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IEventExtractor
{
    Task<ExtractedEvent> ExtractAsync(ClusterContext context, CancellationToken cancellationToken = default);
}
