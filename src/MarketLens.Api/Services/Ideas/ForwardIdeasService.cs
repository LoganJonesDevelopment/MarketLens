using Microsoft.Extensions.Options;

namespace MarketLens.Api.Services.Ideas;

public class ForwardIdeasService(
    IdeaMarketDataService marketData,
    ForwardPipelineCatalog catalog,
    ForwardIdeaScorer scorer,
    IOptions<ForwardIdeasOptions> options)
{
    public object ListPipelines() => catalog.ListPipelines(options.Value);

    public async Task<object> GetForwardAsync(
        string? thesis,
        string? modules,
        int? windowDays,
        int? take,
        bool? includeCrowded,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var days = Math.Clamp(windowDays ?? 30, 7, 120);
        var limit = Math.Clamp(take ?? 30, 5, 200);
        var spec = catalog.ResolveSpec(thesis, options.Value);
        var activeModules = catalog.SelectModules(modules, spec, options.Value);
        var universe = await marketData.LoadForwardUniverseAsync(spec, days, now, ct);

        var includeHot = includeCrowded ?? false;
        var groupSymbols = new HashSet<string>(
            spec.Groups.SelectMany(g => g.Symbols),
            StringComparer.OrdinalIgnoreCase);
        var scored = universe.Contexts
            .Select(context => scorer.Build(context, spec, activeModules, now))
            .Where(item => groupSymbols.Contains(item.Symbol)
                || item.PipelineScore >= 24m
                || item.ThesisFit >= 60m)
            .ToList();
        var excludedCrowded = scored.Count(scorer.IsCrowdedCandidate);
        var filtered = scored
            .Where(item => includeHot || !scorer.IsCrowdedCandidate(item))
            .OrderByDescending(item => item.PipelineScore)
            .ThenByDescending(item => item.ThesisFit)
            .ThenBy(item => item.CrowdingRisk)
            .ToList();
        var topItems = filtered.Take(limit).ToList();
        var topSymbols = new HashSet<string>(topItems.Select(i => i.Symbol), StringComparer.OrdinalIgnoreCase);
        var missingGroupMembers = filtered
            .Where(item => groupSymbols.Contains(item.Symbol) && !topSymbols.Contains(item.Symbol))
            .ToList();
        var items = topItems.Concat(missingGroupMembers).ToList();

        return new
        {
            generatedAt = now,
            windowDays = days,
            windowStart = now.AddDays(-days),
            thesis = new
            {
                spec.Key,
                spec.Label,
                spec.Description,
                spec.Keywords,
                groups = spec.Groups.Select(g => new
                {
                    g.Key,
                    g.Label,
                    g.SetupType,
                    g.Weight,
                    g.Symbols,
                    subcategories = g.Subcategories?.Select(sc => new { sc.Label, sc.Symbols }),
                    g.Benchmarks,
                }),
            },
            pipeline = new
            {
                modules = activeModules.Select(m => new
                {
                    m.Key,
                    m.Label,
                    m.Description,
                    m.Weight,
                }),
                crowdingGuard = new
                {
                    enabled = !includeHot,
                    excluded = excludedCrowded,
                    rule = "Excludes candidates with extreme recent price heat unless includeCrowded=true.",
                },
            },
            universe = new
            {
                candidates = universe.CandidateCount,
                eventRows = universe.EventRowCount,
                symbolsWithPrices = universe.SymbolsWithPrices,
                symbolsWithFundamentals = universe.SymbolsWithFundamentals,
            },
            items,
        };
    }
}
