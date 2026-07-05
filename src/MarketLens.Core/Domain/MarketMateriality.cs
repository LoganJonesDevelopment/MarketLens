namespace MarketLens.Core.Domain;

public static class MarketMateriality
{
    private static readonly string[] ConsumerMediaNoise =
    [
        "prime video", "amazon music", "audible", "twitch", "livestream", "streaming",
        "movie", "movies", "series", "season", "episode", "show", "trailer",
        "nascar", "route 66", "sports broadcast", "games in may", "game pass",
        "geforce now", "video game", "gaming", "play now", "coming to",
        "app store awards", "playlist", "podcast", "kindle books"
    ];

    private static readonly string[] GenericCorporateNoise =
    [
        "ceo of worldwide stores", "team efficiency", "without compromising quality",
        "shopping tips", "customer obsession", "behind the scenes", "celebrates",
        "announces lineup", "things to know", "how to watch"
    ];

    private static readonly string[] OpinionNoise =
    [
        "should you buy", "best ai stock", "best stock to buy", "prediction:",
        "billionaire", "jim cramer", "on sale", "undervalued", "stock vs.",
        "stock vs", "my allocation", "what to know", "after overbuying",
        "sure thing", "mr. market", "thesis is intact", "next leg", "hidden",
        "is it time", "is still the best", "is the growth story real",
        "the market is ignoring", "forget nvidia", "forget tesla",
        "still attractive", "investment story is shifting", "top analyst forecasts",
        "analysts see", "upside for", "after rally", "after latest", "stock is a buy",
        "stock a buy", "buy now or stay out", "dominated in", "which stock"
    ];

    private static readonly string[] HardMarketSignals =
    [
        "earnings", "results", "quarter", "quarterly", "annual", "revenue", "profit",
        "loss", "margin", "operating income", "net income", "cash flow", "free cash flow",
        "guidance", "forecast", "outlook", "capex", "capital expenditure",
        "dividend", "buyback", "repurchase", "shareholder", "sec", "8-k", "10-q",
        "10-k", "filing", "material agreement", "definitive agreement", "contract",
        "acquisition", "merger", "divestiture", "spin-off", "investment", "stake",
        "lawsuit", "investigation", "regulator", "regulatory", "antitrust", "ftc",
        "doj", "tariff", "export restriction", "impairment", "restructuring",
        "layoff", "delisting", "restatement", "bankruptcy"
    ];

    private static readonly string[] StrategicTechnologySignals =
    [
        "data center", "cloud revenue", "aws revenue", "azure revenue",
        "semiconductor", "chip revenue", "gpu revenue", "blackwell", "cuda",
        "export restriction", "capacity expansion", "multibillion", "multi-billion"
    ];

    private static readonly string[] AnalystSignals =
    [
        "analyst", "upgrade", "downgrade", "price target", "rating", "initiates",
        "reiterates", "maintains", "raises", "cuts", "lowered", "boosts",
        "overweight", "underweight", "buy rating", "sell rating"
    ];

    private static readonly string[] AnalystActionPhrases =
    [
        "raises price target", "raised price target", "lifts price target",
        "boosts price target", "bumps price target", "bumped", "cuts price target",
        "lowers price target", "trimmed price target", "price target increased",
        "price target raised", "price target cut", "price target lowered",
        "maintains buy", "maintains hold", "maintains sell", "maintains overweight",
        "maintains underweight", "reiterates buy", "reiterates outperform",
        "upgrades", "downgrades", "initiates", "revamps"
    ];

    private static readonly string[] AnalystAttributionSignals =
    [
        "goldman", "morgan stanley", "barclays", "stifel", "wedbush", "wells fargo",
        "raymond james", "benchmark", "bofa", "bank of america", "td cowen",
        "jpmorgan", "jefferies", "evercore", "ubs", "citi", "citigroup",
        "deutsche bank", "bernstein", "mizuho", "piper sandler", "loop capital",
        "analyst"
    ];

    private static readonly string[] OfficerChangeSignals =
    [
        "appoints", "appointed", "names", "named", "resigns", "resigned", "retires",
        "retired", "steps down", "stepping down", "joins", "leaves", "departing",
        "chief executive officer", "chief financial officer", "chief operating officer",
        "board of directors", "director election", "elects", "promotes"
    ];

    private static readonly string[] MacroSignals =
    [
        "cpi", "ppi", "inflation", "unemployment", "jobs report", "payrolls",
        "gdp", "fomc", "federal reserve", "fed rate", "interest rate",
        "ism", "pmi", "durable goods", "retail sales", "treasury yield",
        "tariff", "oil prices",
        "consumer price index", "producer price index",
        "job openings", "jolts", "hires", "labor turnover", "separations",
        "nonfarm payroll", "personal income", "personal outlays", "personal consumption",
        "gross domestic product", "trade deficit", "trade balance",
        "housing starts", "building permits", "industrial production",
        "beige book", "minutes of the federal open market committee",
        "minutes of the board", "discount rate", "monetary policy",
        "economic outlook", "labor market"
    ];

    private static readonly string[] UpcomingEarningsNoise =
    [
        "earnings loom", "earnings expected", "earnings preview", "earnings release date",
        "sets conference call", "will report", "scheduled to report", "reports after",
        "reports before", "when it reports", "earnings call set", "conference call for",
        "earnings estimates"
    ];

    private static readonly string[] EarningsResultSignals =
    [
        "reports", "reported", "results", "earnings", "revenue", "eps", "profit",
        "margin", "operating income", "net income", "guidance", "outlook",
        "beats", "beat estimates", "misses", "missed estimates"
    ];

    private static readonly string[] PortfolioStakeNoise =
    [
        "warren buffett", "berkshire", "billionaire", "portfolio", "sold stake",
        "selling stake", "trimmed stake", "piling into", "went out with a bang"
    ];

    private static readonly string[] ThirdPartyLeadSignals =
    [
        "rubrik", "marvell", "intel ceo", "psi quantum", "psiquantum",
        "sps commerce", "rivian", "sandisk", "western digital"
    ];

    public static bool IsCompanyFeedMaterial(string headline, string? summary)
    {
        var text = Combine(headline, summary);
        if (IsClearlyNoise(text)) return false;

        return ContainsAny(text, HardMarketSignals) ||
               ContainsAny(text, StrategicTechnologySignals) ||
               ContainsAny(text, OfficerChangeSignals);
    }

    public static bool IsAggregatorMaterial(string headline, string? summary)
    {
        var text = Combine(headline, summary);
        if (IsClearlyNoise(text)) return false;

        return ContainsAny(text, HardMarketSignals) ||
               ContainsAny(text, StrategicTechnologySignals) ||
               ContainsAny(text, AnalystSignals) ||
               ContainsAny(text, OfficerChangeSignals);
    }

    public static bool AcceptClassifierOutput(
        string source,
        string? symbol,
        string? eventType,
        decimal confidence,
        string headline,
        string? summary)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return false;

        var text = Combine(headline, summary);
        if (IsClearlyNoise(text)) return false;

        if (source == SourceNames.Edgar) return true;

        if (eventType == EventTypes.Earnings)
        {
            return HasEarningsResultSignal(text);
        }

        if (eventType == EventTypes.ProductLaunch)
        {
            return HasMaterialProductSignal(text) &&
                   IsEntityLikelyOwner(symbol, eventType, headline, summary);
        }

        if (eventType == EventTypes.AnalystAction)
        {
            return HasAnalystActionSignal(text) &&
                   (source != SourceNames.Finnhub || confidence >= 0.60m);
        }

        if (eventType == EventTypes.MacroRelease)
        {
            // Primary macro sources are authoritative — accept regardless of keyword match.
            if (source is SourceNames.Bls or SourceNames.Bea or SourceNames.Census
                or SourceNames.FedPress or SourceNames.FedSpeeches)
                return !IsClearlyNoise(text);
            return ContainsAny(text, MacroSignals) && !ContainsAny(text, OpinionNoise);
        }

        if (eventType == EventTypes.RegulationFdDisclosure)
        {
            return source is SourceNames.Edgar or SourceNames.IrFeed;
        }

        if (eventType == EventTypes.OfficerChange)
        {
            return ContainsAny(text, OfficerChangeSignals) &&
                   IsEntityLikelyOwner(symbol, eventType, headline, summary);
        }

        if (eventType == EventTypes.AcquisitionDisposition)
        {
            return !ContainsAny(text, PortfolioStakeNoise) &&
                   IsEntityLikelyOwner(symbol, eventType, headline, summary);
        }

        if (eventType == EventTypes.MaterialAgreement)
        {
            return IsEntityLikelyOwner(symbol, eventType, headline, summary);
        }

        if (source == SourceNames.IrFeed)
        {
            return IsCompanyFeedMaterial(headline, summary);
        }

        if (source == SourceNames.Finnhub && confidence < 0.55m)
        {
            return false;
        }

        return true;
    }

    public static bool AcceptExtractedEvent(
        string? symbol,
        string eventType,
        IEnumerable<string> evidenceTexts,
        string extractedSummary,
        string? source = null)
    {
        var text = string.Join('\n', evidenceTexts.Append(extractedSummary));

        // Primary authoritative sources bypass noise and keyword filters entirely.
        // Their content is definitionally material; only suppress if the LLM extraction was empty.
        if (source is SourceNames.Bls or SourceNames.Bea or SourceNames.Census
            or SourceNames.FedPress or SourceNames.FedSpeeches
            or SourceNames.SecEnforcement or SourceNames.Ftc or SourceNames.DojAntitrust or SourceNames.Edgar)
        {
            return !string.IsNullOrWhiteSpace(extractedSummary);
        }

        if (IsClearlyNoise(text)) return false;

        if (eventType == EventTypes.ProductLaunch)
        {
            return HasMaterialProductSignal(text) &&
                   IsEntityLikelyOwner(symbol, eventType, text, null);
        }

        if (eventType == EventTypes.Earnings)
        {
            return HasEarningsResultSignal(text);
        }

        if (eventType == EventTypes.AnalystAction)
        {
            return HasAnalystActionSignal(text);
        }

        if (eventType == EventTypes.MacroRelease)
        {
            return ContainsAny(text, MacroSignals) && !ContainsAny(text, OpinionNoise);
        }

        if (eventType == EventTypes.OfficerChange)
        {
            return ContainsAny(text, OfficerChangeSignals) &&
                   IsEntityLikelyOwner(symbol, eventType, text, null);
        }

        if (eventType == EventTypes.AcquisitionDisposition)
        {
            return !ContainsAny(text, PortfolioStakeNoise) &&
                   IsEntityLikelyOwner(symbol, eventType, text, null);
        }

        if (eventType == EventTypes.MaterialAgreement)
        {
            return IsEntityLikelyOwner(symbol, eventType, text, null);
        }

        return true;
    }

    public static bool IsClearlyNoise(string text) =>
        ContainsAny(text, ConsumerMediaNoise) || ContainsAny(text, GenericCorporateNoise);

    private static bool HasMaterialProductSignal(string text) =>
        ContainsAny(text, HardMarketSignals) ||
        ContainsAny(text, StrategicTechnologySignals) ||
        (ContainsAny(text, "launch", "unveils", "announces", "introduces") &&
         ContainsAny(text, "revenue", "contract", "customer", "enterprise", "data center",
             "chip", "semiconductor", "gpu", "capex", "multibillion", "multi-billion"));

    private static bool HasAnalystActionSignal(string text) =>
        !ContainsAny(text, OpinionNoise) &&
        ContainsAny(text, AnalystSignals) &&
        ContainsAny(text, AnalystActionPhrases) &&
        ContainsAny(text, AnalystAttributionSignals);

    private static bool HasEarningsResultSignal(string text) =>
        !ContainsAny(text, UpcomingEarningsNoise) && ContainsAny(text, EarningsResultSignals);

    private static bool IsEntityLikelyOwner(
        string? symbol,
        string eventType,
        string headline,
        string? summary)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return true;
        var text = Combine(headline, summary);
        var aliases = AliasesFor(symbol);
        if (aliases.Length == 0) return true;

        if (eventType == EventTypes.MaterialAgreement)
        {
            return aliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase));
        }

        if (LooksLikeThirdPartySpillover(symbol, text)) return false;

        return aliases.Any(alias =>
            headline.StartsWith(alias, StringComparison.OrdinalIgnoreCase) ||
            headline.Contains($" {alias} ", StringComparison.OrdinalIgnoreCase) ||
            headline.Contains($"{alias}:", StringComparison.OrdinalIgnoreCase) ||
            headline.Contains($"{alias} (", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeThirdPartySpillover(string symbol, string text)
    {
        var lower = text.ToLowerInvariant();
        if (ContainsAny(lower, ThirdPartyLeadSignals) &&
            !lower.StartsWith(PrimaryAliasFor(symbol).ToLowerInvariant()))
        {
            return true;
        }

        return symbol.ToUpperInvariant() switch
        {
            "NVDA" => lower.Contains("nvidia-backed") || lower.Contains("nvidia backed"),
            "GOOGL" => lower.Contains("google cloud sql") ||
                       lower.Contains("to google cloud") ||
                       lower.Contains("for google cloud") ||
                       lower.Contains("with google cloud"),
            "AMZN" => lower.Contains("amazon headwinds") ||
                      lower.Contains("amazon accounted for"),
            _ => false,
        };
    }

    private static string PrimaryAliasFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "AAPL" => "Apple",
        "MSFT" => "Microsoft",
        "NVDA" => "Nvidia",
        "GOOGL" => "Alphabet",
        "AMZN" => "Amazon",
        _ => symbol,
    };

    private static string[] AliasesFor(string symbol) => symbol.ToUpperInvariant() switch
    {
        "AAPL" => ["AAPL", "Apple"],
        "MSFT" => ["MSFT", "Microsoft"],
        "NVDA" => ["NVDA", "Nvidia", "NVIDIA"],
        "GOOGL" => ["GOOGL", "GOOG", "Alphabet", "Google"],
        "AMZN" => ["AMZN", "Amazon"],
        _ => [symbol],
    };

    private static string Combine(string headline, string? summary) => $"{headline}\n{summary}";

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, IEnumerable<string> needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
