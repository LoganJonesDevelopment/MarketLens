using MarketLens.Core.Domain;
using MarketLens.Core.Entities;

namespace MarketLens.Api.Services.Ideas;

internal static class IdeaScoring
{
    public static IdeaRadarItem BuildIdea(
        string symbol,
        DateTime now,
        List<IdeaEventRow> events,
        List<IdeaSourceRow> sources,
        List<IdeaInsiderRow> insiders,
        List<IdeaPriceRow> prices,
        List<IdeaCalendarRow> calendar,
        CompanyFundamentals? fundamentals = null)
    {
        var meta = TickerMetadata.Lookup(symbol);
        var latestSignalAt = new[]
            {
                events.Select(e => (DateTime?)e.LastSeenAt).DefaultIfEmpty().Max(),
                sources.Select(s => (DateTime?)s.LastPublishedAt).DefaultIfEmpty().Max(),
                insiders.Select(i => i.TransactionDate).DefaultIfEmpty().Max(),
            }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        var latestAgeDays = latestSignalAt == DateTime.MinValue ? 999m : (decimal)Math.Max(0, (now - latestSignalAt).TotalDays);
        var sumImportance = events.Sum(e => Math.Min(e.Importance, 0.75m));
        var maxImportance = events.Count == 0 ? 0m : events.Max(e => e.Importance);
        var eventIntensity = Clamp01(sumImportance * 0.40m + events.Count * 0.035m + maxImportance * 0.55m);
        var primaryEvents = events.Count(e => e.PrimaryMembers > 0 || e.SourceTier == SourceTiers.Primary);
        var primarySourceCount = sources.Where(s => s.SourceTier == SourceTiers.Primary).Sum(s => s.Count);
        var directCompanySourceCount = sources
            .Where(s => s.Source is SourceNames.Edgar or SourceNames.IrFeed or SourceNames.BusinessWire
                or SourceNames.GlobeNewswire or SourceNames.PrNewswire or SourceNames.Transcript)
            .Sum(s => s.Count);
        var effectivePrimarySourceCount = events.Count == 0
            ? Math.Min(primarySourceCount, directCompanySourceCount)
            : primarySourceCount;
        var wireSourceCount = sources.Where(s => s.SourceTier == SourceTiers.Wire).Sum(s => s.Count);
        var tradeCount = sources.Where(s => s.SourceTier == SourceTiers.TradePress).Sum(s => s.Count);
        var lowTrustCount = sources.Where(s => s.SourceTier is SourceTiers.Aggregator or SourceTiers.Opinion).Sum(s => s.Count);
        var totalSourceCount = Math.Max(1, sources.Sum(s => s.Count));
        var primaryRatio = (decimal)effectivePrimarySourceCount / totalSourceCount;
        var wireRatio = (decimal)wireSourceCount / totalSourceCount;
        var sourceQuality = Clamp01(primaryRatio * 0.78m + wireRatio * 0.42m + primaryEvents * 0.035m + maxImportance * 0.28m);
        var reaction = events.Select(e => e.ReactionScore ?? 0m).DefaultIfEmpty(0m).Max();

        var ret7 = ReturnSince(prices, 7);
        var ret30 = ReturnSince(prices, 30);
        var ret90 = ReturnSince(prices, 90);
        var ret1y = ReturnSince(prices, 365);
        var priceAction = Clamp01(Abs(ret7) / 18m * 0.25m + Abs(ret30) / 38m * 0.45m + Abs(ret90) / 75m * 0.30m);

        var openMarket = insiders.Where(i => i.IsOpenMarketTrade).ToList();
        var netInsiderDollars = openMarket.Sum(i => i.DollarValue * (i.AcquiredDisposedCode == "A" ? 1m : -1m));
        var grossInsiderDollars = openMarket.Sum(i => i.DollarValue);
        var insiderSignal = Clamp01((decimal)Math.Log10((double)Math.Abs(netInsiderDollars) + 10d) / 8m);
        if (grossInsiderDollars > 0 && Math.Abs(netInsiderDollars) < grossInsiderDollars * 0.20m)
            insiderSignal *= 0.65m;

        var lowTrustRatio = (decimal)lowTrustCount / totalSourceCount;
        var analystCount = events.Count(e => e.EventType == EventTypes.AnalystAction);
        var valuationRisk = ComputeValuationRisk(fundamentals, ret30, ret90);
        var hypeRisk = ComputeHypeRisk(ret30, ret90, lowTrustRatio, primaryRatio, analystCount, maxImportance, reaction, valuationRisk);
        var novelty = Clamp01(1m - latestAgeDays / 14m);
        var interestScore = Math.Round(100m * Clamp01(
            eventIntensity * 0.34m +
            sourceQuality * 0.21m +
            reaction * 0.15m +
            priceAction * 0.14m +
            insiderSignal * 0.10m +
            novelty * 0.06m), 1);

        var category = CategoryFor(interestScore, sourceQuality, hypeRisk, novelty, eventIntensity);
        var stance = DirectionFor(ret30, netInsiderDollars, events);

        return new IdeaRadarItem(
            Symbol: symbol,
            Name: meta?.CompanyName ?? symbol,
            Category: category,
            InterestScore: interestScore,
            HypeRisk: Math.Round(hypeRisk * 100m, 1),
            QualityScore: Math.Round(sourceQuality * 100m, 1),
            Stance: stance,
            LatestSignalAt: latestSignalAt == DateTime.MinValue ? null : latestSignalAt,
            Scouts: new IdeaScoutScores(
                EventIntensity: Math.Round(eventIntensity, 3),
                SourceQuality: Math.Round(sourceQuality, 3),
                PriceAction: Math.Round(priceAction, 3),
                InsiderSignal: Math.Round(insiderSignal, 3),
                HypeRisk: Math.Round(hypeRisk, 3),
                MarketReaction: Math.Round(reaction, 3)),
            Valuation: new IdeaValuationDigest(
                HasFundamentals: fundamentals?.Status == "ok",
                MarketCap: ToDollars(fundamentals?.MarketCapitalizationMillion),
                PeTtm: fundamentals?.PeTtm,
                ForwardPe: fundamentals?.ForwardPe,
                PsTtm: fundamentals?.PsTtm,
                EvRevenueTtm: fundamentals?.EvRevenueTtm,
                RevenueGrowthTtmYoy: fundamentals?.RevenueGrowthTtmYoy,
                EpsGrowthTtmYoy: fundamentals?.EpsGrowthTtmYoy,
                ValuationRisk: valuationRisk.HasValue ? Math.Round(valuationRisk.Value * 100m, 1) : null,
                UpdatedAt: fundamentals?.IngestedAt),
            Price: new IdeaPriceDigest(ret7, ret30, ret90, ret1y, LatestClose(prices), LatestPriceDate(prices)),
            Evidence: new IdeaEvidenceDigest(
                EventCount: events.Count,
                PrimaryEventCount: primaryEvents,
                SourceCount: sources.Sum(s => s.Count),
                PrimarySourceCount: primarySourceCount,
                WireSourceCount: wireSourceCount,
                TradePressCount: tradeCount,
                LowTrustCount: lowTrustCount,
                TopEventType: events.GroupBy(e => e.EventType).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
                MaxImportance: Math.Round(maxImportance, 3)),
            Insiders: new IdeaInsiderDigest(
                OpenMarketTransactions: openMarket.Count,
                NetDollars: Math.Round(netInsiderDollars, 0),
                GrossDollars: Math.Round(grossInsiderDollars, 0),
                LatestTransactionAt: openMarket.Select(i => i.TransactionDate).DefaultIfEmpty().Max()),
            WhyNow: BuildWhyNow(symbol, events, ret30, netInsiderDollars, calendar),
            HypeCheck: BuildHypeCheck(hypeRisk, ret30, ret90, lowTrustRatio, primaryRatio, analystCount, maxImportance),
            WatchNext: BuildWatchNext(events, calendar));
    }

    public static decimal? ComputeValuationRisk(CompanyFundamentals? fundamentals, decimal? ret30, decimal? ret90)
    {
        if (fundamentals is null || fundamentals.Status != "ok") return null;

        var peHeat = MultipleHeat(fundamentals.PeTtm, 18m, 45m, 90m);
        var forwardPeHeat = MultipleHeat(fundamentals.ForwardPe, 16m, 35m, 65m);
        var psHeat = MultipleHeat(fundamentals.PsTtm, 3m, 9m, 18m);
        var evSalesHeat = MultipleHeat(fundamentals.EvRevenueTtm, 3m, 9m, 18m);
        var fcfHeat = MultipleHeat(fundamentals.PriceToFreeCashFlowTtm, 18m, 45m, 90m);
        var multipleHeat = new[] { peHeat, forwardPeHeat, psHeat, evSalesHeat, fcfHeat }
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0m)
            .Average();

        var priceHeat = Clamp01(Math.Max(0m, ret30 ?? 0m) / 35m * 0.45m + Math.Max(0m, ret90 ?? 0m) / 80m * 0.55m);
        var growthSupport = Clamp01(Math.Max(0m, fundamentals.RevenueGrowthTtmYoy ?? 0m) / 40m * 0.62m
            + Math.Max(0m, fundamentals.EpsGrowthTtmYoy ?? 0m) / 65m * 0.38m);
        var qualitySupport = Clamp01(Math.Max(0m, fundamentals.GrossMarginTtm ?? 0m) / 65m * 0.55m
            + Math.Max(0m, fundamentals.OperatingMarginTtm ?? 0m) / 35m * 0.45m);
        var leveragePenalty = Clamp01(((fundamentals.DebtToEquityQuarterly ?? 0m) - 0.75m) / 2m);
        var betaPenalty = Clamp01(((fundamentals.Beta ?? 1m) - 1.2m) / 1.8m);

        return Clamp01(multipleHeat * 0.58m + priceHeat * 0.18m + leveragePenalty * 0.08m + betaPenalty * 0.06m
            - growthSupport * 0.14m - qualitySupport * 0.08m);
    }

    public static decimal? ReturnSince(List<IdeaPriceRow> rows, int days)
    {
        if (rows.Count < 2) return null;
        var latest = rows.OrderBy(p => p.Timestamp).Last();
        var target = latest.Timestamp.AddDays(-days);
        var prior = rows.Where(p => p.Timestamp <= target).OrderBy(p => p.Timestamp).LastOrDefault()
                    ?? rows.OrderBy(p => p.Timestamp).FirstOrDefault();
        if (prior is null || prior.Close == 0m || prior.Timestamp == latest.Timestamp) return null;
        return Math.Round((latest.Close / prior.Close - 1m) * 100m, 2);
    }

    public static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    public static bool IsEquitySymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var s = symbol.Trim();
        if (s.Contains(':') || s.Contains('=') || s.Contains("-USD", StringComparison.OrdinalIgnoreCase)) return false;
        return s.All(c => char.IsLetterOrDigit(c) || c == '.');
    }

    public static decimal? ToDollars(decimal? millions) =>
        millions.HasValue ? Math.Round(millions.Value * 1_000_000m, 0) : null;

    public static string FormatDollars(decimal value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);
        if (abs >= 1_000_000_000m) return $"{sign}${abs / 1_000_000_000m:0.0}B";
        if (abs >= 1_000_000m) return $"{sign}${abs / 1_000_000m:0.0}M";
        if (abs >= 1_000m) return $"{sign}${abs / 1_000m:0.0}K";
        return $"{sign}${abs:0}";
    }

    private static decimal ComputeHypeRisk(
        decimal? ret30,
        decimal? ret90,
        decimal lowTrustRatio,
        decimal primaryRatio,
        int analystCount,
        decimal maxImportance,
        decimal reaction,
        decimal? valuationRisk)
    {
        var priceHeat = Clamp01(Math.Max(0m, ret30 ?? 0m) / 45m * 0.55m + Math.Max(0m, ret90 ?? 0m) / 95m * 0.45m);
        var lowPrimaryPenalty = Clamp01((0.32m - primaryRatio) / 0.32m);
        var chatter = Clamp01(lowTrustRatio * 1.25m + analystCount * 0.08m);
        var thinEvent = Clamp01((0.35m - maxImportance) / 0.35m);
        var valuation = valuationRisk ?? 0m;
        return Clamp01(priceHeat * 0.30m + chatter * 0.20m + lowPrimaryPenalty * 0.18m + thinEvent * reaction * 0.14m + valuation * 0.18m);
    }

    private static decimal? MultipleHeat(decimal? value, decimal fair, decimal stretched, decimal extreme)
    {
        if (!value.HasValue || value.Value <= 0m) return null;
        if (value.Value <= fair) return 0m;
        if (value.Value >= extreme) return 1m;
        if (value.Value <= stretched)
            return (value.Value - fair) / (stretched - fair) * 0.65m;
        return 0.65m + (value.Value - stretched) / (extreme - stretched) * 0.35m;
    }

    private static List<string> BuildWhyNow(
        string symbol,
        List<IdeaEventRow> events,
        decimal? ret30,
        decimal netInsiderDollars,
        List<IdeaCalendarRow> calendar)
    {
        var reasons = new List<string>();
        var top = events.OrderByDescending(e => e.Importance).FirstOrDefault();
        if (top is not null) reasons.Add(top.Summary);
        if (ret30.HasValue && Math.Abs(ret30.Value) >= 8m)
            reasons.Add($"{symbol} moved {ret30.Value:+0.0;-0.0;0.0}% over roughly the last month.");
        if (Math.Abs(netInsiderDollars) >= 250_000m)
            reasons.Add($"Open-market insider flow is net {FormatDollars(netInsiderDollars)}.");
        var next = calendar.FirstOrDefault(c => c.ScheduledAt >= DateTime.UtcNow);
        if (next is not null) reasons.Add($"Next scheduled catalyst: {next.Label} on {next.ScheduledAt:MMM d}.");
        return reasons.Count == 0 ? ["No single dominant catalyst; watch for a stronger primary-source event."] : reasons.Take(4).ToList();
    }

    private static List<string> BuildHypeCheck(
        decimal hypeRisk,
        decimal? ret30,
        decimal? ret90,
        decimal lowTrustRatio,
        decimal primaryRatio,
        int analystCount,
        decimal maxImportance)
    {
        var checks = new List<string>();
        if (hypeRisk >= 0.65m) checks.Add("Hype risk is high; require primary filings, transcript evidence, or fundamentals before chasing.");
        if ((ret30 ?? 0m) > 15m || (ret90 ?? 0m) > 35m) checks.Add("Price has already moved sharply; compare the move to actual event importance.");
        if (primaryRatio < 0.20m) checks.Add("Primary-source share is thin in the current window.");
        if (lowTrustRatio > 0.45m) checks.Add("Aggregator/opinion/reddit share is high relative to source-backed material.");
        if (analystCount >= 2) checks.Add("Analyst activity is present; check whether upgrades are following the move.");
        if (maxImportance < 0.25m) checks.Add("No high-importance event anchors the narrative yet.");
        return checks.Count == 0 ? ["No obvious hype flag from price/source/event/valuation heuristics."] : checks.Take(5).ToList();
    }

    private static List<string> BuildWatchNext(List<IdeaEventRow> events, List<IdeaCalendarRow> calendar)
    {
        var next = new List<string>();
        var nextCal = calendar.FirstOrDefault(c => c.ScheduledAt >= DateTime.UtcNow);
        if (nextCal is not null) next.Add($"{nextCal.Label} on {nextCal.ScheduledAt:MMM d}");
        var topTypes = events.GroupBy(e => e.EventType)
            .OrderByDescending(g => g.Count())
            .Take(2)
            .Select(g => g.Key.Replace('_', ' '));
        foreach (var type in topTypes) next.Add($"Watch for follow-through in {type} events.");
        next.Add("Look for a primary filing, transcript passage, or insider signal that confirms or refutes the move.");
        return next.Distinct().Take(5).ToList();
    }

    private static string CategoryFor(decimal interest, decimal quality, decimal hype, decimal novelty, decimal eventIntensity)
    {
        if (interest >= 50m && hype >= 0.62m) return "hype-check";
        if (interest >= 58m && quality >= 0.42m) return "deep-dive";
        if (novelty >= 0.75m && eventIntensity >= 0.30m) return "fresh";
        if (interest >= 38m) return "watch";
        return "thin";
    }

    private static string DirectionFor(decimal? ret30, decimal netInsiderDollars, List<IdeaEventRow> events)
    {
        var avgSentiment = events.Count == 0 ? 0m : events.Average(e => e.Sentiment);
        var score = avgSentiment;
        if ((ret30 ?? 0m) > 8m) score += 0.15m;
        if ((ret30 ?? 0m) < -8m) score -= 0.15m;
        if (netInsiderDollars > 500_000m) score += 0.15m;
        if (netInsiderDollars < -500_000m) score -= 0.15m;
        return score switch
        {
            > 0.25m => "constructive",
            < -0.25m => "caution",
            _ => "mixed",
        };
    }

    private static decimal? LatestClose(List<IdeaPriceRow> rows) =>
        rows.OrderBy(p => p.Timestamp).LastOrDefault()?.Close;

    private static DateTime? LatestPriceDate(List<IdeaPriceRow> rows) =>
        rows.OrderBy(p => p.Timestamp).LastOrDefault()?.Timestamp;

    private static decimal Abs(decimal? value) => Math.Abs(value ?? 0m);
}
