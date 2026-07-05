using MarketLens.Core.Models;

namespace MarketLens.Core.Interfaces;

public interface IThesisPlanner
{
    Task<ThesisPlanResult> PlanAsync(ThesisPlanContext context, CancellationToken cancellationToken = default);
}

public sealed record ThesisPlanResult(
    ThesisPlan Plan,
    string ModelName,
    string PromptVersion);
