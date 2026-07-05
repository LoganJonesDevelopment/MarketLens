using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IIdeaMemoGenerator
{
    string PromptVersion { get; }

    Task<IdeaMemoGenerationResult> GenerateAsync(IdeaMemoContext context, CancellationToken cancellationToken = default);
}
