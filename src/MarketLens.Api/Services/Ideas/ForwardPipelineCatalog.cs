namespace MarketLens.Api.Services.Ideas;

public class ForwardPipelineCatalog
{
    public object ListPipelines(ForwardIdeasOptions options)
    {
        var specs = ResolveThesisSpecs(options);
        var moduleCatalog = Modules(options);
        var defaultKey = NormalizeKey(options.DefaultPipelineKey);
        if (string.IsNullOrWhiteSpace(defaultKey) || specs.All(s => s.Key != defaultKey))
            defaultKey = specs.FirstOrDefault()?.Key ?? "ai-infrastructure";

        return new
        {
            defaultPipelineKey = defaultKey,
            items = specs.Select(spec => new
            {
                spec.Key,
                spec.Label,
                spec.Description,
                spec.Keywords,
                spec.Aliases,
                spec.ModuleKeys,
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
            }),
            modules = moduleCatalog.Select(m => new
            {
                m.Key,
                m.Label,
                m.Description,
                m.Weight,
            }),
        };
    }

    internal ForwardThesisSpec ResolveSpec(string? thesis, ForwardIdeasOptions options)
    {
        var specs = ResolveThesisSpecs(options);
        var requestedKey = NormalizeKey(string.IsNullOrWhiteSpace(thesis) ? options.DefaultPipelineKey : thesis);
        var exact = specs.FirstOrDefault(s =>
            s.Key == requestedKey ||
            NormalizeKey(s.Label) == requestedKey ||
            s.Aliases.Any(a => NormalizeKey(a) == requestedKey));
        if (exact is not null) return exact;

        var fuzzy = specs.FirstOrDefault(s =>
            (!string.IsNullOrWhiteSpace(requestedKey) && (s.Key.Contains(requestedKey, StringComparison.OrdinalIgnoreCase) || requestedKey.Contains(s.Key, StringComparison.OrdinalIgnoreCase))) ||
            s.Aliases.Any(a =>
            {
                var alias = NormalizeKey(a);
                return !string.IsNullOrWhiteSpace(alias) &&
                    (requestedKey.Contains(alias, StringComparison.OrdinalIgnoreCase) || alias.Contains(requestedKey, StringComparison.OrdinalIgnoreCase));
            }));
        if (fuzzy is not null) return fuzzy;

        var keywords = requestedKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => k.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ForwardThesisSpec(
            Key: requestedKey.Length == 0 ? "custom" : requestedKey,
            Label: string.IsNullOrWhiteSpace(thesis) ? "Custom thesis" : thesis.Trim(),
            Description: "Generic forward pipeline driven by event quality, underreaction, crowding guards, catalysts, and valuation context.",
            Keywords: keywords.Count == 0 ? ["growth", "capacity", "pricing", "demand", "margin"] : keywords,
            Aliases: [],
            ModuleKeys: [],
            Groups: []);
    }

    internal IReadOnlyList<ForwardPipelineModule> SelectModules(
        string? modules,
        ForwardThesisSpec spec,
        ForwardIdeasOptions options)
    {
        var catalog = Modules(options);
        if (string.IsNullOrWhiteSpace(modules) && spec.ModuleKeys.Count == 0) return catalog;

        var requested = string.IsNullOrWhiteSpace(modules)
            ? spec.ModuleKeys
            : modules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = requested
            .Select(NormalizeKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selected = keys
            .Select(key => catalog.FirstOrDefault(m => m.Key == key))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();
        return selected.Count == 0 ? catalog : selected;
    }

    private static IReadOnlyList<ForwardThesisSpec> ResolveThesisSpecs(ForwardIdeasOptions options)
    {
        var configured = options.Pipelines
            .Select(BuildConfiguredThesisSpec)
            .Where(spec => spec is not null)
            .Select(spec => spec!)
            .GroupBy(spec => spec.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return configured.Count > 0 ? configured : DefaultThesisSpecs();
    }

    private static ForwardThesisSpec? BuildConfiguredThesisSpec(ForwardPipelineOptions source)
    {
        var key = NormalizeKey(source.Key);
        if (string.IsNullOrWhiteSpace(key)) return null;

        var groups = source.Groups
            .Select(group =>
            {
                var groupKey = NormalizeKey(group.Key);
                if (string.IsNullOrWhiteSpace(groupKey)) groupKey = NormalizeKey(group.Label);
                var symbols = CleanSymbols(group.Symbols);
                if (string.IsNullOrWhiteSpace(groupKey) || symbols.Count == 0) return null;

                var subcategories = group.Subcategories?
                    .Where(sc => !string.IsNullOrWhiteSpace(sc.Label) && sc.Symbols.Count > 0)
                    .Select(sc => new ForwardSubcategory(sc.Label, CleanSymbols(sc.Symbols)))
                    .ToList();

                var benchmarks = group.Benchmarks is { Count: > 0 } ? CleanSymbols(group.Benchmarks) : null;

                return new ForwardSymbolGroup(
                    Key: groupKey,
                    Label: EmptyToDefault(group.Label, groupKey),
                    SetupType: EmptyToDefault(group.SetupType, "mapped setup"),
                    Weight: IdeaScoring.Clamp01(group.Weight ?? 0.50m),
                    Symbols: symbols,
                    Subcategories: subcategories is { Count: > 0 } ? subcategories : null,
                    Benchmarks: benchmarks is { Count: > 0 } ? benchmarks : null);
            })
            .Where(group => group is not null)
            .Select(group => group!)
            .ToList();

        var keywords = CleanStrings(source.Keywords);
        if (keywords.Count == 0)
        {
            keywords = key.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(k => k.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new ForwardThesisSpec(
            Key: key,
            Label: EmptyToDefault(source.Label, key),
            Description: EmptyToDefault(source.Description, "Config-driven forward idea pipeline."),
            Keywords: keywords,
            Aliases: CleanStrings(source.Aliases),
            ModuleKeys: CleanStrings(source.Modules).Select(NormalizeKey).Where(k => !string.IsNullOrWhiteSpace(k)).ToList(),
            Groups: groups);
    }

    private static IReadOnlyList<ForwardPipelineModule> Modules(ForwardIdeasOptions options)
    {
        var modules = DefaultModules().ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var source in options.Modules)
        {
            var key = NormalizeKey(source.Key);
            if (string.IsNullOrWhiteSpace(key)) continue;

            modules.TryGetValue(key, out var current);
            modules[key] = new ForwardPipelineModule(
                Key: key,
                Label: EmptyToDefault(source.Label, current?.Label ?? key),
                Description: EmptyToDefault(source.Description, current?.Description ?? "Config-defined forward pipeline module."),
                Weight: IdeaScoring.Clamp01(source.Weight ?? current?.Weight ?? 0.10m));
        }

        return modules.Values.ToList();
    }

    private static IReadOnlyList<ForwardPipelineModule> DefaultModules() =>
    [
        new("second-order", "Second-order map", "Rewards symbols tied to the thesis but away from crowded direct-exposure groups.", 0.22m),
        new("thesis-fit", "Thesis fit", "Scores explicit symbol mapping and recent corpus language against the active thesis.", 0.18m),
        new("underreaction", "Underreaction", "Looks for source-backed signal that has not already been paid for in price.", 0.20m),
        new("crowding-guard", "Crowding guard", "Penalizes sharp recent winners, elevated hype, and thin primary-source support.", 0.18m),
        new("evidence-quality", "Evidence quality", "Rewards primary-source events, high source quality, and non-rumor evidence.", 0.12m),
        new("catalyst-path", "Catalyst path", "Rewards candidates with visible upcoming or recent catalysts to test the thesis.", 0.06m),
        new("risk-reward", "Risk/reward", "Uses valuation, growth, margin, insider, and price context as a sanity check.", 0.04m),
    ];

    private static IReadOnlyList<ForwardThesisSpec> DefaultThesisSpecs() =>
    [
        new(
            Key: "ai-infrastructure",
            Label: "AI value chain",
            Description: "Traces money backward from applications through compute, networking, power, silicon, and raw materials to find under-covered structural positions across the full AI stack.",
            Keywords:
            [
                "ai", "artificial intelligence", "accelerator", "gpu", "compute", "inference",
                "data center", "datacenter", "cluster", "networking", "interconnect", "memory",
                "hbm", "power", "cooling", "thermal", "capacity", "foundry", "semiconductor",
                "copper", "rare earth", "industrial gas", "water treatment", "optical", "fiber",
                "transceiver", "training data", "data licensing", "annotation", "vector database",
                "edge inference", "ai agent", "automation", "observability", "serving", "llm",
                "model training", "cloud compute",
            ],
            Aliases: ["ai", "compute", "semiconductor", "semiconductors", "value-chain", "ai-stack"],
            ModuleKeys: ["second-order", "thesis-fit", "underreaction", "crowding-guard", "evidence-quality", "catalyst-path", "risk-reward"],
            Groups:
            [
                new("raw-materials", "Raw materials and mining", "raw material supplier", 0.88m,
                    ["FCX", "SCCO", "MP", "LIN", "APD", "ECL", "GLW"],
                    [new("Copper", ["FCX", "SCCO"]), new("Industrial gases", ["LIN", "APD"]), new("Specialty inputs", ["MP", "ECL", "GLW"])],
                    ["COPX"]),
                new("silicon-substrate", "Silicon and substrate", "silicon and substrate", 0.22m,
                    ["NVDA", "AMD", "INTC", "AVGO", "MRVL", "ARM", "QCOM", "MPWR", "MU", "WDC", "STX", "TSM", "ASML", "AMAT", "LRCX", "KLAC", "TER", "ON"],
                    [new("Accelerators", ["NVDA", "AMD", "INTC"]), new("Platform IP", ["AVGO", "MRVL", "ARM", "QCOM", "MPWR"]), new("Memory", ["MU", "WDC", "STX"]), new("Semicap equipment", ["ASML", "AMAT", "LRCX", "KLAC", "TER"]), new("Foundry & packaging", ["TSM"]), new("Power semis", ["ON"])],
                    ["SMH"]),
                new("power-real-estate", "Power and real estate", "power and real estate", 0.82m,
                    ["VRT", "ETN", "PWR", "TT", "HUBB", "GEV", "CEG", "NRG", "NEE", "EQIX", "DLR"],
                    [new("Generation", ["GEV", "CEG", "NRG", "NEE"]), new("Electrical infrastructure", ["VRT", "ETN", "PWR", "TT", "HUBB"]), new("Data center REITs", ["EQIX", "DLR"])],
                    ["XLU"]),
                new("networking-interconnect", "Networking and interconnect", "networking bottleneck", 0.76m,
                    ["ANET", "CIEN", "COHR", "CSCO", "LITE", "GLW"],
                    [new("Optical", ["CIEN", "COHR", "LITE", "GLW"]), new("Switching", ["ANET", "CSCO"])]),
                new("data-plane", "Data plane", "data supply", 0.90m, ["SNOW", "MDB", "RDDT"]),
                new("training-compute", "Training and compute", "training compute", 0.18m,
                    ["MSFT", "AMZN", "GOOGL", "META", "ORCL", "CRWV"],
                    [new("Hyperscalers", ["MSFT", "AMZN", "GOOGL", "META"]), new("GPU cloud", ["ORCL", "CRWV"])],
                    ["QQQ"]),
                new("serving-applications", "Serving and applications", "serving and applications", 0.55m,
                    ["CRM", "NOW", "PLTR", "DDOG", "APP", "NET", "PATH"],
                    [new("Enterprise AI", ["CRM", "NOW", "PLTR"]), new("Infrastructure", ["DDOG", "NET"]), new("Automation & ads", ["PATH", "APP"])],
                    ["IGV"]),
            ]),
    ];

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var parts = value.Trim().ToLowerInvariant()
            .Replace('_', '-')
            .Split([' ', '\t', '\r', '\n', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('-', parts);
    }

    private static List<string> CleanStrings(IEnumerable<string>? values) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> CleanSymbols(IEnumerable<string>? values) =>
        CleanStrings(values)
            .Select(s => s.ToUpperInvariant())
            .Where(IdeaScoring.IsEquitySymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string EmptyToDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
