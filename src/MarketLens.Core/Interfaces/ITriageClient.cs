using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface ITriageClient
{
    Task<TriageResult> ClassifyAsync(string text, decimal threshold, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TriageResult>> ClassifyBatchAsync(
        IReadOnlyList<string> texts,
        decimal threshold,
        CancellationToken cancellationToken = default);
}
