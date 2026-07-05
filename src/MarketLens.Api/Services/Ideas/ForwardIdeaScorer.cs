namespace MarketLens.Api.Services.Ideas;

public class ForwardIdeaScorer
{
    internal ForwardIdeaItem Build(
        ForwardIdeaContext context,
        ForwardThesisSpec spec,
        IReadOnlyList<ForwardPipelineModule> modules,
        DateTime now)
    {
        var group = BestGroup(context.Symbol, spec);
        var moduleResults = modules
            .Select(module => EvaluateModule(module, context, spec, group, now))
            .ToList();
        var totalWeight = modules.Sum(m => m.Weight);
        var pipelineScore = totalWeight <= 0m
            ? 0m
            : Math.Round(moduleResults.Sum(r => r.Contribution) / totalWeight, 1);
        var thesisFit = Math.Round(ComputeThesisFit(context, spec, group) * 100m, 1);
        var crowdingRisk = Math.Round(ComputeCrowdingRisk(context.Radar) * 100m, 1);

        return new ForwardIdeaItem(
            Symbol: context.Symbol,
            Name: context.Radar.Name,
            SetupType: group?.SetupType ?? "event-led setup",
            Group: group?.Label,
            TradeIntent: TradeIntentFor(context, group, crowdingRisk),
            PipelineScore: pipelineScore,
            ThesisFit: thesisFit,
            Actionability: ActionabilityFor(pipelineScore, thesisFit, crowdingRisk),
            CrowdingRisk: crowdingRisk,
            LatestSignalAt: context.Radar.LatestSignalAt,
            Modules: moduleResults,
            Rationale: BuildRationale(context, group, moduleResults, crowdingRisk),
            NextChecks: BuildNextChecks(context, group, now),
            Invalidates: BuildInvalidationChecks(context, group),
            Current: context.Radar);
    }

    internal bool IsCrowdedCandidate(ForwardIdeaItem item)
    {
        var ret30 = item.Current.Price.Return30d ?? 0m;
        var ret90 = item.Current.Price.Return90d ?? 0m;
        return item.CrowdingRisk >= 78m || ret30 >= 40m || (ret90 >= 90m && item.CrowdingRisk >= 64m);
    }

    private static ForwardModuleResult EvaluateModule(
        ForwardPipelineModule module,
        ForwardIdeaContext context,
        ForwardThesisSpec spec,
        ForwardSymbolGroup? group,
        DateTime now)
    {
        return module.Key switch
        {
            "second-order" => EvaluateSecondOrder(module, group),
            "thesis-fit" => BuildModuleResult(module, ComputeThesisFit(context, spec, group),
                group is null ? "No explicit thesis map; only corpus language can carry the fit." : $"Mapped to {group.Label}.",
                group is null ? [] : [group.SetupType]),
            "underreaction" => EvaluateUnderreaction(module, context, group),
            "crowding-guard" => EvaluateCrowdingGuard(module, context),
            "evidence-quality" => EvaluateEvidenceQuality(module, context),
            "catalyst-path" => EvaluateCatalystPath(module, context, now),
            "risk-reward" => EvaluateRiskReward(module, context),
            _ => BuildModuleResult(module, 0m, "Module is not recognized by this pipeline.", []),
        };
    }

    private static ForwardModuleResult EvaluateSecondOrder(ForwardPipelineModule module, ForwardSymbolGroup? group)
    {
        if (group is null) return BuildModuleResult(module, 0.12m, "Not in the explicit second-order thesis map.", []);

        var isCrowdedGroup = group.Weight <= 0.25m;
        var score = isCrowdedGroup ? 0.08m : group.Weight;
        var rationale = isCrowdedGroup
            ? $"{group.Label} is treated as crowded proof rather than the next idea."
            : $"Second-order exposure through {group.Label}.";
        return BuildModuleResult(module, score, rationale, [group.SetupType]);
    }

    private static ForwardModuleResult EvaluateUnderreaction(ForwardPipelineModule module, ForwardIdeaContext context, ForwardSymbolGroup? group)
    {
        var radar = context.Radar;
        var signal = IdeaScoring.Clamp01(
            radar.Scouts.EventIntensity * 0.45m +
            radar.Scouts.SourceQuality * 0.25m +
            radar.Scouts.InsiderSignal * 0.15m +
            radar.Scouts.MarketReaction * 0.15m);
        var groupPrior = group is null || group.Weight <= 0.25m ? 0m : group.Weight * 0.25m;
        var ret30 = radar.Price.Return30d ?? 0m;
        var ret90 = radar.Price.Return90d ?? 0m;
        var heatPenalty = IdeaScoring.Clamp01(Math.Max(0m, ret30) / 35m * 0.75m + Math.Max(0m, ret90) / 80m * 0.25m);
        var leftBehindBonus = ret30 <= 5m ? 0.22m : ret30 <= 15m ? 0.10m : 0m;
        var pullbackBonus = ret30 < -10m ? 0.08m : 0m;
        var score = IdeaScoring.Clamp01((signal + groupPrior) * (1m - heatPenalty) + leftBehindBonus + pullbackBonus);
        var rationale = heatPenalty >= 0.60m
            ? "Recent price heat has already paid for much of the signal."
            : "Signal exists without a fully crowded recent move.";
        return BuildModuleResult(module, score, rationale, [$"30d {FormatPercent(ret30)}", $"signal {(signal * 100m):0}"]);
    }

    private static ForwardModuleResult EvaluateCrowdingGuard(ForwardPipelineModule module, ForwardIdeaContext context)
    {
        var risk = ComputeCrowdingRisk(context.Radar);
        var rationale = risk >= 0.70m
            ? "Crowding guard is flashing: price/hype/valuation are ahead of the evidence stack."
            : "Recent price and attention do not look fully crowded yet.";
        return BuildModuleResult(module, IdeaScoring.Clamp01(1m - risk), rationale, [$"crowding {(risk * 100m):0}", $"hype {context.Radar.HypeRisk:0}"]);
    }

    private static ForwardModuleResult EvaluateEvidenceQuality(ForwardPipelineModule module, ForwardIdeaContext context)
    {
        var evidence = context.Radar.Evidence;
        var sourceCount = Math.Max(1, evidence.SourceCount);
        var primaryShare = (decimal)evidence.PrimarySourceCount / sourceCount;
        var primaryEvents = IdeaScoring.Clamp01(evidence.PrimaryEventCount / 3m);
        var score = IdeaScoring.Clamp01(
            context.Radar.Scouts.SourceQuality * 0.45m +
            context.Radar.Scouts.EventIntensity * 0.28m +
            primaryShare * 0.17m +
            primaryEvents * 0.10m);
        var rationale = evidence.PrimarySourceCount > 0 || evidence.PrimaryEventCount > 0
            ? "Primary-source evidence is present."
            : "Evidence is still mostly secondary or thin.";
        return BuildModuleResult(module, score, rationale, [$"{evidence.PrimarySourceCount} primary sources", $"{evidence.EventCount} events"]);
    }

    private static ForwardModuleResult EvaluateCatalystPath(ForwardPipelineModule module, ForwardIdeaContext context, DateTime now)
    {
        var next = context.Calendar.FirstOrDefault(c => c.ScheduledAt >= now);
        var scheduleScore = 0m;
        if (next is not null)
        {
            var days = Math.Max(0m, (decimal)(next.ScheduledAt - now).TotalDays);
            scheduleScore = days <= 14m ? 0.65m : days <= 45m ? 0.50m : days <= 90m ? 0.30m : 0.15m;
        }

        var maxImportance = context.Events.Select(e => e.Importance).DefaultIfEmpty(0m).Max();
        var score = IdeaScoring.Clamp01(scheduleScore + maxImportance * 0.25m);
        var rationale = next is null
            ? "No near-term scheduled catalyst is in the calendar yet."
            : $"Next visible catalyst is {next.Label} on {next.ScheduledAt:MMM d}.";
        return BuildModuleResult(module, score, rationale, next is null ? [] : [next.Label, next.ScheduledAt.ToString("MMM d")]);
    }

    private static ForwardModuleResult EvaluateRiskReward(ForwardPipelineModule module, ForwardIdeaContext context)
    {
        var radar = context.Radar;
        var fundamentals = context.Fundamentals;
        var valuationSafety = radar.Valuation.ValuationRisk.HasValue ? IdeaScoring.Clamp01(1m - radar.Valuation.ValuationRisk.Value / 100m) : 0.45m;
        var growth = IdeaScoring.Clamp01(Math.Max(0m, fundamentals?.RevenueGrowthTtmYoy ?? 0m) / 35m * 0.65m
            + Math.Max(0m, fundamentals?.EpsGrowthTtmYoy ?? 0m) / 60m * 0.35m);
        var quality = IdeaScoring.Clamp01(Math.Max(0m, fundamentals?.GrossMarginTtm ?? 0m) / 65m * 0.60m
            + Math.Max(0m, fundamentals?.OperatingMarginTtm ?? 0m) / 35m * 0.40m);
        var insider = radar.Insiders.NetDollars > 250_000m ? 0.70m : radar.Insiders.NetDollars < -500_000m ? 0.10m : 0.35m;
        var priceHeatPenalty = IdeaScoring.Clamp01(Math.Max(0m, radar.Price.Return30d ?? 0m) / 35m);
        var score = IdeaScoring.Clamp01(valuationSafety * 0.45m + growth * 0.25m + quality * 0.20m + insider * 0.10m - priceHeatPenalty * 0.15m);
        var rationale = fundamentals is null || fundamentals.Status != "ok"
            ? "Fundamental risk/reward inputs are incomplete."
            : "Valuation and growth context are available for a sanity check.";
        return BuildModuleResult(module, score, rationale, [$"valuation safety {(valuationSafety * 100m):0}", $"growth {(growth * 100m):0}"]);
    }

    private static ForwardModuleResult BuildModuleResult(ForwardPipelineModule module, decimal score, string rationale, IReadOnlyList<string> inputs)
    {
        var clamped = IdeaScoring.Clamp01(score);
        return new ForwardModuleResult(
            Key: module.Key,
            Label: module.Label,
            Score: Math.Round(clamped * 100m, 1),
            Weight: module.Weight,
            Contribution: Math.Round(clamped * module.Weight * 100m, 1),
            Rationale: rationale,
            Inputs: inputs);
    }

    private static decimal ComputeThesisFit(ForwardIdeaContext context, ForwardThesisSpec spec, ForwardSymbolGroup? group)
    {
        var groupScore = group?.Weight ?? 0m;
        if (group is not null && group.Weight <= 0.25m) groupScore = 0.30m;
        var keywordHits = CountThesisKeywordHits(context, spec.Keywords);
        var keywordScore = IdeaScoring.Clamp01(keywordHits / 5m);
        var eventScore = context.Events.Count == 0 ? 0m : IdeaScoring.Clamp01(context.Events.Max(e => e.Importance));
        var sourceScore = context.Sources.Count == 0 ? 0m : IdeaScoring.Clamp01(context.Radar.Scouts.SourceQuality);
        return IdeaScoring.Clamp01(groupScore * 0.66m + keywordScore * 0.18m + eventScore * 0.10m + sourceScore * 0.06m);
    }

    private static decimal ComputeCrowdingRisk(IdeaRadarItem idea)
    {
        var ret7 = Math.Max(0m, idea.Price.Return7d ?? 0m);
        var ret30 = Math.Max(0m, idea.Price.Return30d ?? 0m);
        var ret90 = Math.Max(0m, idea.Price.Return90d ?? 0m);
        var sourceCount = Math.Max(1, idea.Evidence.SourceCount);
        var lowTrustShare = (decimal)idea.Evidence.LowTrustCount / sourceCount;
        var primaryShare = (decimal)idea.Evidence.PrimarySourceCount / sourceCount;
        var priceHeat = IdeaScoring.Clamp01(ret7 / 22m * 0.15m + ret30 / 40m * 0.55m + ret90 / 90m * 0.30m);
        var hype = IdeaScoring.Clamp01(idea.HypeRisk / 100m);
        var valuation = IdeaScoring.Clamp01((idea.Valuation.ValuationRisk ?? 0m) / 100m);
        var thinPrimary = IdeaScoring.Clamp01((0.25m - primaryShare) / 0.25m);
        return IdeaScoring.Clamp01(priceHeat * 0.46m + hype * 0.26m + valuation * 0.14m + lowTrustShare * 0.08m + thinPrimary * 0.06m);
    }

    private static ForwardSymbolGroup? BestGroup(string symbol, ForwardThesisSpec spec) =>
        spec.Groups
            .Where(g => g.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Weight)
            .FirstOrDefault();

    private static int CountThesisKeywordHits(ForwardIdeaContext context, IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0) return 0;
        var text = string.Join(' ', context.Events
            .SelectMany(e => new[] { e.Summary, e.TopHeadline, e.TopPublisher })
            .Concat([context.Fundamentals?.Industry, context.Fundamentals?.Name, context.Fundamentals?.WebUrl]));
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return keywords.Count(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string ActionabilityFor(decimal pipelineScore, decimal thesisFit, decimal crowdingRisk)
    {
        if (crowdingRisk >= 78m) return "wait";
        if (pipelineScore >= 64m && thesisFit >= 55m) return "research-now";
        if (pipelineScore >= 46m || thesisFit >= 62m) return "watchlist";
        return "monitor";
    }

    private static string TradeIntentFor(ForwardIdeaContext context, ForwardSymbolGroup? group, decimal crowdingRisk)
    {
        if (crowdingRisk >= 78m || (group is not null && group.Weight <= 0.25m)) return "do-not-chase";
        var ret30 = context.Radar.Price.Return30d ?? 0m;
        if (ret30 <= 5m && group is not null) return "left-behind long candidate";
        if (group is not null) return $"second-order {group.SetupType}";
        return "event-led research candidate";
    }

    private static IReadOnlyList<string> BuildRationale(
        ForwardIdeaContext context,
        ForwardSymbolGroup? group,
        IReadOnlyList<ForwardModuleResult> moduleResults,
        decimal crowdingRisk)
    {
        var lines = new List<string>();
        if (group is not null && group.Weight > 0.25m)
            lines.Add($"{context.Symbol} is mapped to {group.Label}, away from the crowded direct-exposure groups.");
        if (group is not null && group.Weight <= 0.25m)
            lines.Add($"{context.Symbol} is in {group.Label}, treated as proof of the thesis, not the next trade idea.");
        lines.Add(crowdingRisk < 55m
            ? "The crowding guard is not blocking it on recent price heat."
            : "Crowding is meaningful, so the entry question matters more than the narrative.");

        lines.AddRange(moduleResults.OrderByDescending(m => m.Contribution).Take(2).Select(m => m.Rationale));
        return lines.Distinct().Take(5).ToList();
    }

    private static IReadOnlyList<string> BuildNextChecks(ForwardIdeaContext context, ForwardSymbolGroup? group, DateTime now)
    {
        var next = new List<string>();
        if (group is not null && group.Weight > 0.25m)
            next.Add($"Confirm that {group.Label.ToLowerInvariant()} converts the thesis into revenue, margin, backlog, or pricing power.");
        var catalyst = context.Calendar.FirstOrDefault(c => c.ScheduledAt >= now);
        if (catalyst is not null)
            next.Add($"Use {catalyst.Label} on {catalyst.ScheduledAt:MMM d} as the next explicit test.");
        if (context.Radar.Evidence.PrimarySourceCount == 0)
            next.Add("Find a primary filing, transcript, or company release before upgrading from watchlist to research-now.");
        if ((context.Radar.Price.Return30d ?? 0m) > 15m)
            next.Add("Wait for a reset or a fresh primary-source catalyst before treating the setup as actionable.");
        next.AddRange(context.Radar.WatchNext.Take(2));
        return next.Distinct().Take(5).ToList();
    }

    private static IReadOnlyList<string> BuildInvalidationChecks(ForwardIdeaContext context, ForwardSymbolGroup? group)
    {
        var checks = new List<string>();
        if (group is not null)
            checks.Add($"{group.Label} fails to show order growth, utilization, pricing, backlog, or capex follow-through.");
        checks.Add("The stock rerates on sympathy while primary-source evidence remains thin.");
        checks.Add("A direct winner absorbs the economics instead of pushing demand into the adjacent bottleneck.");
        if ((context.Radar.Valuation.ValuationRisk ?? 0m) >= 65m)
            checks.Add("Valuation risk keeps rising without matching growth or margin support.");
        return checks.Distinct().Take(4).ToList();
    }

    private static string FormatPercent(decimal value) => $"{value:+0.0;-0.0;0.0}%";
}
