using MarketLens.Core.Entities;

namespace MarketLens.Api.Services.Ideas;

internal static class IdeaBriefBuilder
{
    public static object BuildPriceSummary(List<IdeaPriceRow> prices)
    {
        var latest = prices.OrderBy(p => p.Timestamp).LastOrDefault();
        if (latest is null)
        {
            return new
            {
                hasPrice = false,
                latestClose = (decimal?)null,
                latestDate = (DateTime?)null,
                return7d = (decimal?)null,
                return30d = (decimal?)null,
                return90d = (decimal?)null,
                return1y = (decimal?)null,
                ytdReturn = (decimal?)null,
                rangePosition = (decimal?)null,
            };
        }

        var high = prices.Max(p => p.High);
        var low = prices.Min(p => p.Low);
        var ytdStart = prices.Where(p => p.Timestamp.Year == DateTime.UtcNow.Year).OrderBy(p => p.Timestamp).FirstOrDefault();
        var rangePosition = high == low ? (decimal?)null : Math.Round((latest.Close - low) / (high - low) * 100m, 1);

        return new
        {
            hasPrice = true,
            latestClose = latest.Close,
            latestDate = latest.Timestamp,
            return7d = IdeaScoring.ReturnSince(prices, 7),
            return30d = IdeaScoring.ReturnSince(prices, 30),
            return90d = IdeaScoring.ReturnSince(prices, 90),
            return1y = IdeaScoring.ReturnSince(prices, 365),
            ytdReturn = ytdStart is null || ytdStart.Close == 0 ? null : (decimal?)Math.Round((latest.Close / ytdStart.Close - 1m) * 100m, 2),
            yearHigh = high,
            yearLow = low,
            rangePosition,
        };
    }

    public static object BuildInsiderSummary(List<IdeaInsiderRow> insiders)
    {
        var open = insiders.Where(i => i.IsOpenMarketTrade).ToList();
        var net = open.Sum(i => i.DollarValue * (i.AcquiredDisposedCode == "A" ? 1m : -1m));
        var bought = open.Where(i => i.AcquiredDisposedCode == "A").Sum(i => i.DollarValue);
        var sold = open.Where(i => i.AcquiredDisposedCode == "D").Sum(i => i.DollarValue);
        var byOwner = open
            .GroupBy(i => i.OwnerName)
            .Select(g => new
            {
                ownerName = g.Key,
                role = g.Select(i => i.OfficerTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "Insider",
                transactions = g.Count(),
                netDollars = Math.Round(g.Sum(i => i.DollarValue * (i.AcquiredDisposedCode == "A" ? 1m : -1m)), 0),
                latestTransactionAt = g.Select(i => i.TransactionDate).DefaultIfEmpty().Max(),
            })
            .OrderByDescending(x => Math.Abs(x.netDollars))
            .Take(8)
            .ToList();

        return new
        {
            totalTransactions = insiders.Count,
            openMarketTransactions = open.Count,
            bought = Math.Round(bought, 0),
            sold = Math.Round(sold, 0),
            netDollars = Math.Round(net, 0),
            latestTransactionAt = open.Select(i => i.TransactionDate).DefaultIfEmpty().Max(),
            topInsiders = byOwner,
        };
    }

    public static object BuildSourceMix(List<IdeaSourceRow> sources)
    {
        var total = Math.Max(1, sources.Sum(s => s.Count));
        var byTier = sources
            .GroupBy(s => s.SourceTier)
            .Select(g => new
            {
                tier = g.Key,
                count = g.Sum(x => x.Count),
                share = Math.Round((decimal)g.Sum(x => x.Count) / total * 100m, 1),
            })
            .OrderByDescending(x => x.count)
            .ToList();
        var bySource = sources
            .GroupBy(s => s.Source)
            .Select(g => new
            {
                source = g.Key,
                count = g.Sum(x => x.Count),
                tier = g.Select(x => x.SourceTier).FirstOrDefault(),
                latestAt = g.Max(x => x.LastPublishedAt),
            })
            .OrderByDescending(x => x.count)
            .Take(12)
            .ToList();
        return new { total = sources.Sum(s => s.Count), byTier, bySource };
    }

    public static object BuildFundamentalsSummary(CompanyFundamentals? fundamentals, decimal? ret30, decimal? ret90)
    {
        if (fundamentals is null || fundamentals.Status != "ok")
        {
            return new
            {
                hasFundamentals = false,
                source = fundamentals?.Provider,
                ingestedAt = fundamentals?.IngestedAt,
                status = fundamentals?.Status ?? "missing",
                error = fundamentals?.Error,
            };
        }

        var valuationRisk = IdeaScoring.ComputeValuationRisk(fundamentals, ret30, ret90);
        return new
        {
            hasFundamentals = true,
            source = fundamentals.Provider,
            ingestedAt = fundamentals.IngestedAt,
            status = fundamentals.Status,
            error = fundamentals.Error,
            name = fundamentals.Name,
            industry = fundamentals.Industry,
            exchange = fundamentals.Exchange,
            currency = fundamentals.Currency,
            webUrl = fundamentals.WebUrl,
            ipoDate = fundamentals.IpoDate,
            marketCap = IdeaScoring.ToDollars(fundamentals.MarketCapitalizationMillion),
            enterpriseValue = IdeaScoring.ToDollars(fundamentals.EnterpriseValueMillion),
            shareOutstanding = fundamentals.ShareOutstandingMillion,
            peTtm = fundamentals.PeTtm,
            forwardPe = fundamentals.ForwardPe,
            pegTtm = fundamentals.PegTtm,
            psTtm = fundamentals.PsTtm,
            evRevenueTtm = fundamentals.EvRevenueTtm,
            evEbitdaTtm = fundamentals.EvEbitdaTtm,
            priceToBook = fundamentals.PriceToBook,
            priceToFreeCashFlowTtm = fundamentals.PriceToFreeCashFlowTtm,
            revenueGrowthTtmYoy = fundamentals.RevenueGrowthTtmYoy,
            epsGrowthTtmYoy = fundamentals.EpsGrowthTtmYoy,
            grossMarginTtm = fundamentals.GrossMarginTtm,
            operatingMarginTtm = fundamentals.OperatingMarginTtm,
            netMarginTtm = fundamentals.NetMarginTtm,
            roeTtm = fundamentals.RoeTtm,
            debtToEquityQuarterly = fundamentals.DebtToEquityQuarterly,
            beta = fundamentals.Beta,
            week52High = fundamentals.Week52High,
            week52Low = fundamentals.Week52Low,
            week52PriceReturnDaily = fundamentals.Week52PriceReturnDaily,
            valuationRisk = valuationRisk.HasValue ? (decimal?)Math.Round(valuationRisk.Value * 100m, 1) : null,
        };
    }

    public static object BuildOverpricingSummary(IdeaRadarItem idea, CompanyFundamentals? fundamentals)
    {
        var valuationRisk = IdeaScoring.ComputeValuationRisk(fundamentals, idea.Price.Return30d, idea.Price.Return90d);
        if (valuationRisk is null || fundamentals is null || fundamentals.Status != "ok")
        {
            return new
            {
                level = "unknown",
                score = (decimal?)null,
                label = "valuation missing",
                reasons = new[]
                {
                    "Structured valuation multiples are not available yet; overpricing is still inferred from price, source quality, events, and insider flow.",
                },
                missingInputs = new[] { "fundamentals", "valuation multiples" },
            };
        }

        var score = Math.Round(valuationRisk.Value * 100m, 1);
        var reasons = BuildValuationReasons(fundamentals, idea.Price.Return30d, idea.Price.Return90d);
        var level = score >= 70m ? "high" : score >= 45m ? "moderate" : "low";
        var label = level switch
        {
            "high" => "expensive without a strong margin of safety",
            "moderate" => "valuation needs pressure-testing",
            _ => "valuation risk not the main flag",
        };

        return new
        {
            level,
            score,
            label,
            reasons,
            missingInputs = Array.Empty<string>(),
        };
    }

    public static object BuildBriefNarrative(
        IdeaRadarItem idea,
        CompanyFundamentals? fundamentals,
        object overpricing,
        List<IdeaEventRow> events,
        List<object> theses,
        List<object> transcripts,
        List<object> chunks)
    {
        var topEvent = events.OrderByDescending(e => e.Importance).FirstOrDefault();
        var bull = new List<string>();
        var bear = new List<string>();
        var next = new List<string>();

        if (topEvent is not null)
            bull.Add($"Highest-ranked event: {topEvent.Summary}");
        if (idea.Evidence.PrimaryEventCount > 0)
            bull.Add($"{idea.Evidence.PrimaryEventCount} recent primary-source event(s) reduce pure-rumor risk.");
        if ((idea.Price.Return30d ?? 0m) > 10m && idea.QualityScore >= 40m)
            bull.Add("Price momentum is being confirmed by at least moderate source quality.");
        if (idea.Insiders.NetDollars > 250_000m)
            bull.Add("Recent open-market insider activity is net positive.");
        if ((fundamentals?.RevenueGrowthTtmYoy ?? 0m) >= 20m)
            bull.Add($"TTM revenue growth is {fundamentals!.RevenueGrowthTtmYoy:0.0}%, giving the valuation a growth support check.");
        if ((fundamentals?.GrossMarginTtm ?? 0m) >= 45m)
            bull.Add($"Gross margin is {fundamentals!.GrossMarginTtm:0.0}% TTM, which supports a higher-quality business read.");

        if (idea.HypeRisk >= 65m)
            bear.Add("The hype-risk scout is elevated; price or attention may be ahead of primary-source confirmation.");
        if (idea.Evidence.PrimarySourceCount == 0 && idea.Evidence.SourceCount > 0)
            bear.Add("Recent coverage has little or no primary-source support.");
        if (idea.Insiders.NetDollars < -500_000m)
            bear.Add("Recent open-market insider activity is net selling.");
        if (chunks.Count == 0)
            bear.Add("No recent filing passages were available for deeper primary-document reading.");
        if ((fundamentals?.PeTtm ?? 0m) >= 55m || (fundamentals?.PsTtm ?? 0m) >= 12m)
            bear.Add("Valuation multiples are elevated enough to require growth and margin confirmation.");

        next.AddRange(idea.WatchNext);
        if (fundamentals is not null)
            next.Add("Compare valuation multiples against direct peers before treating the move as cheap or expensive.");
        if (transcripts.Count == 0)
            next.Add("Queue or discover the latest earnings-call replay if management tone matters.");
        if (theses.Count == 0)
            next.Add("Use this brief as the lightweight tracking surface before creating any formal thesis.");

        return new
        {
            bottomLine = BottomLine(idea),
            bullCase = bull.Take(5),
            bearCase = bear.Take(5),
            nextQuestions = next.Distinct().Take(7),
            researchMode = idea.HypeRisk >= 65m ? "hype-check" : idea.QualityScore >= 55m ? "deep-dive" : "watch",
            overpricing,
        };
    }

    public static IReadOnlyList<string> BuildDataGaps(
        CompanyFundamentals? fundamentals,
        List<object> transcripts,
        List<object> chunks,
        List<object> theses)
    {
        var gaps = new List<string>();
        if (fundamentals is null || fundamentals.Status != "ok")
            gaps.Add("Structured fundamentals and valuation multiples are not ingested yet; treat overpricing checks as price/action/source-based, not valuation-based.");
        else if (fundamentals.PeTtm is null && fundamentals.ForwardPe is null && fundamentals.PsTtm is null && fundamentals.EvRevenueTtm is null)
            gaps.Add("Fundamentals are cached, but the source did not return usable valuation multiples.");
        if (transcripts.Count == 0) gaps.Add("No completed transcript is available for this symbol.");
        if (chunks.Count == 0) gaps.Add("No recent filing chunks are available in the selected window.");
        if (theses.Count == 0) gaps.Add("No formal thesis is attached; this is discovery mode only.");
        return gaps;
    }

    private static IReadOnlyList<string> BuildValuationReasons(
        CompanyFundamentals fundamentals,
        decimal? ret30,
        decimal? ret90)
    {
        var reasons = new List<string>();
        if (fundamentals.PeTtm.HasValue) reasons.Add($"P/E TTM {fundamentals.PeTtm.Value:0.0}x");
        if (fundamentals.ForwardPe.HasValue) reasons.Add($"forward P/E {fundamentals.ForwardPe.Value:0.0}x");
        if (fundamentals.PsTtm.HasValue) reasons.Add($"P/S TTM {fundamentals.PsTtm.Value:0.0}x");
        if (fundamentals.EvRevenueTtm.HasValue) reasons.Add($"EV/Sales TTM {fundamentals.EvRevenueTtm.Value:0.0}x");
        if (fundamentals.PriceToFreeCashFlowTtm.HasValue) reasons.Add($"P/FCF TTM {fundamentals.PriceToFreeCashFlowTtm.Value:0.0}x");
        if (fundamentals.RevenueGrowthTtmYoy.HasValue) reasons.Add($"revenue growth {fundamentals.RevenueGrowthTtmYoy.Value:0.0}% YoY");
        if (fundamentals.GrossMarginTtm.HasValue) reasons.Add($"gross margin {fundamentals.GrossMarginTtm.Value:0.0}%");
        if ((ret30 ?? 0m) > 15m || (ret90 ?? 0m) > 35m) reasons.Add($"price momentum {ret30:+0.0;-0.0;0.0}% 30d / {ret90:+0.0;-0.0;0.0}% 90d");
        return reasons.Count == 0 ? ["No usable valuation multiples returned by the source."] : reasons.Take(7).ToList();
    }

    private static string BottomLine(IdeaRadarItem idea)
    {
        if (idea.Category == "hype-check")
            return $"{idea.Symbol} is interesting, but the first job is to test whether the move is already over-owned or over-narrated.";
        if (idea.Category == "deep-dive")
            return $"{idea.Symbol} has enough source-backed activity to justify a deeper read.";
        if (idea.Category == "fresh")
            return $"{idea.Symbol} has fresh signal, but the evidence stack is still forming.";
        return $"{idea.Symbol} is on the radar, but the current corpus does not yet justify strong conclusions.";
    }
}
