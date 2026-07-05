using MarketLens.Core.Domain;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class MarketOverviewEndpoints
{
    private static readonly IReadOnlyList<MarketQuoteDefinition> Pulse = new[]
    {
        new MarketQuoteDefinition("SPY", "S&P 500", "Index"),
        new MarketQuoteDefinition("I:NDX", "Nasdaq 100", "Index"),
        new MarketQuoteDefinition("IWM", "Russell 2000", "Index"),
        new MarketQuoteDefinition("DIA", "Dow", "Index"),
        new MarketQuoteDefinition("VXX", "Volatility", "Risk"),
        new MarketQuoteDefinition("TLT", "Long bonds", "Rates"),
        new MarketQuoteDefinition("GLD", "Gold", "Commodities"),
        new MarketQuoteDefinition("USO", "Oil", "Commodities"),
    };

    private static readonly IReadOnlyList<MarketQuoteDefinition> Sectors = new[]
    {
        new MarketQuoteDefinition("XLK", "Technology", "Sector"),
        new MarketQuoteDefinition("XLC", "Communication", "Sector"),
        new MarketQuoteDefinition("XLY", "Discretionary", "Sector"),
        new MarketQuoteDefinition("XLP", "Staples", "Sector"),
        new MarketQuoteDefinition("XLF", "Financials", "Sector"),
        new MarketQuoteDefinition("XLV", "Health care", "Sector"),
        new MarketQuoteDefinition("XLI", "Industrials", "Sector"),
        new MarketQuoteDefinition("XLE", "Energy", "Sector"),
        new MarketQuoteDefinition("XLB", "Materials", "Sector"),
        new MarketQuoteDefinition("XLU", "Utilities", "Sector"),
        new MarketQuoteDefinition("XLRE", "Real estate", "Sector"),
        new MarketQuoteDefinition("SMH", "Semiconductors", "Industry"),
    };

    private static readonly IReadOnlyList<WeeklyPerformanceGroupDefinition> WeeklyPerformanceGroups = new[]
    {
        new WeeklyPerformanceGroupDefinition(
            "indexes",
            "Indexes",
            new[]
            {
                new MarketQuoteDefinition("SPY", "S&P 500", "Index"),
                new MarketQuoteDefinition("QQQ", "Nasdaq 100", "Index"),
                new MarketQuoteDefinition("IWM", "Russell 2000", "Index"),
                new MarketQuoteDefinition("DIA", "Dow", "Index"),
                new MarketQuoteDefinition("VXX", "Volatility", "Risk"),
            }),
        new WeeklyPerformanceGroupDefinition("sectors", "Sectors", Sectors),
        new WeeklyPerformanceGroupDefinition(
            "cross_asset",
            "Rates & commodities",
            new[]
            {
                new MarketQuoteDefinition("TLT", "Long bonds", "Rates"),
                new MarketQuoteDefinition("IEF", "Intermediate bonds", "Rates"),
                new MarketQuoteDefinition("GLD", "Gold", "Commodities"),
                new MarketQuoteDefinition("SLV", "Silver", "Commodities"),
                new MarketQuoteDefinition("USO", "Oil", "Commodities"),
                new MarketQuoteDefinition("UNG", "Natural gas", "Commodities"),
                new MarketQuoteDefinition("CPER", "Copper", "Commodities"),
                new MarketQuoteDefinition("BTC-USD", "Bitcoin", "Crypto"),
                new MarketQuoteDefinition("ETH-USD", "Ethereum", "Crypto"),
                new MarketQuoteDefinition("DX-Y.NYB", "Dollar index", "FX"),
            }),
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> SectorMembers =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["XLK"] = SymbolSet(
                "AAPL", "MSFT", "NVDA", "AMD", "AVGO", "ORCL", "CRM", "NOW",
                "ANET", "DELL", "HPE", "SMCI", "CRWV", "NBIS", "SNOW", "PLTR",
                "INTC", "MU", "QCOM", "MRVL", "AMAT", "KLAC", "LRCX", "NXPI", "TXN", "ON"),
            ["XLC"] = SymbolSet("GOOGL", "META"),
            ["XLY"] = SymbolSet("AMZN", "CVNA", "KMX", "AN", "LAD", "GPI", "ABG", "PAG"),
            ["XLE"] = SymbolSet("XOM", "CVX", "COP", "OXY", "EOG", "SLB", "HAL", "BKR"),
            ["XLB"] = SymbolSet("FCX", "SCCO", "TECK", "RIO", "BHP", "NEM", "GOLD", "AEM", "WPM"),
            ["SMH"] = SymbolSet(
                "NVDA", "AMD", "AVGO", "TSM", "ASML", "INTC", "MU", "QCOM", "ARM",
                "MRVL", "AMAT", "KLAC", "LRCX", "NXPI", "TXN", "ON", "WOLF", "COHR"),
        };

    private static readonly IReadOnlySet<string> DriverEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EventTypes.Earnings,
        EventTypes.AcquisitionDisposition,
        EventTypes.MaterialAgreement,
        EventTypes.MaterialImpairment,
        EventTypes.Delisting,
        EventTypes.RegulationFdDisclosure,
        EventTypes.AnalystAction,
        EventTypes.ProductLaunch,
        EventTypes.RegulatoryAction,
        EventTypes.MacroRelease,
        EventTypes.OtherMaterialEvent,
    };

    public static void MapMarketOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/market/overview", async (
            MarketLensDbContext db,
            int? movers,
            int? drivers,
            CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var moverLimit = Math.Clamp(movers ?? 12, 4, 40);
            var driverLimit = Math.Clamp(drivers ?? 10, 4, 30);
            var driverCandidateLimit = Math.Max(driverLimit * 4, 160);
            var weeklyDefinitions = WeeklyPerformanceGroups.SelectMany(g => g.Items).ToList();
            var symbols = Pulse
                .Concat(Sectors)
                .Concat(weeklyDefinitions)
                .Select(d => d.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var watchlist = await db.ResearchAssets
                .AsNoTracking()
                .Where(a => a.Kind == "ticker" && a.Symbol != null)
                .OrderBy(a => a.Symbol)
                .Select(a => new
                {
                    symbol = a.Symbol!,
                    name = a.Name,
                })
                .ToListAsync(ct);

            symbols.AddRange(watchlist.Select(w => w.symbol));
            symbols = symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var quotes = await db.MarketQuotes
                .AsNoTracking()
                .Where(q => symbols.Contains(q.Symbol))
                .OrderByDescending(q => q.IngestedAt)
                .ToListAsync(ct);

            var quoteMap = quotes
                .GroupBy(q => q.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var barCutoff = now.AddDays(-45);
            var bars = await db.PriceBars
                .AsNoTracking()
                .Where(b => b.Interval == "1d" && symbols.Contains(b.Symbol) && b.Timestamp >= barCutoff)
                .OrderByDescending(b => b.Timestamp)
                .Select(b => new QuoteBar(b.Symbol, b.Timestamp, b.Close, b.Volume, b.IngestedAt))
                .ToListAsync(ct);

            var barsBySymbol = bars
                .GroupBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var pulse = Pulse
                .Select(d => BuildQuoteItem(d.Symbol, d.Label, d.Group, quoteMap, barsBySymbol))
                .ToList();

            var sectors = Sectors
                .Select(d => BuildQuoteItem(d.Symbol, d.Label, d.Group, quoteMap, barsBySymbol))
                .ToList();

            var moverItems = watchlist
                .Select(w => BuildQuoteItem(w.symbol, w.name, "Watchlist", quoteMap, barsBySymbol))
                .Where(q => q.changePercent is not null)
                .OrderByDescending(q => Math.Abs(q.changePercent!.Value))
                .Take(moverLimit)
                .ToList();

            var breadthBase = watchlist
                .Select(w => BuildQuoteItem(w.symbol, w.name, "Watchlist", quoteMap, barsBySymbol))
                .Where(q => q.changePercent is not null)
                .ToList();

            var weeklyPerformance = BuildWeeklyPerformance(
                WeeklyPerformanceGroups,
                watchlist.Select(w => new MarketQuoteDefinition(w.symbol, w.name, "Watchlist")).ToList(),
                barsBySymbol,
                now);

            var advancers = breadthBase.Count(q => q.changePercent > 0.05m);
            var decliners = breadthBase.Count(q => q.changePercent < -0.05m);
            var unchanged = breadthBase.Count - advancers - decliners;
            var averageMove = breadthBase.Count == 0 ? (decimal?)null : Math.Round(breadthBase.Average(q => q.changePercent!.Value), 2);
            var positiveShare = breadthBase.Count == 0 ? (decimal?)null : Math.Round((decimal)advancers / breadthBase.Count * 100m, 1);

            var driverCutoff = now.AddHours(-36);
            var driverCandidates = await db.Events
                .AsNoTracking()
                .Where(e => e.Cluster != null && e.Cluster.LastSeenAt >= driverCutoff && e.Importance >= 0.18m)
                .Select(e => new
                {
                    clusterId = e.ClusterId,
                    symbol = e.Cluster!.Symbol,
                    eventType = e.EventType,
                    summary = e.Summary,
                    importance = e.Importance,
                    sentiment = e.Sentiment,
                    sourceTier = e.Cluster.DominantSourceTier,
                    memberCount = e.Cluster.MemberCount,
                    lastSeenAt = e.Cluster.LastSeenAt,
                    topSource = e.Cluster.Articles
                        .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
                        .ThenByDescending(a => a.SourceTier == SourceTiers.Wire)
                        .ThenByDescending(a => a.PublishedAt)
                        .Select(a => new
                        {
                            a.Source,
                            a.SourceTier,
                            a.Publisher,
                            a.Headline,
                            a.Url,
                            a.PublishedAt,
                        })
                        .FirstOrDefault(),
                    market = e.MarketSnapshots
                        .OrderByDescending(s => s.CapturedAt)
                        .Select(s => new
                        {
                            symbol = s.Symbol,
                            movePercent = s.MovePercent,
                            relativeMovePercent = s.RelativeMovePercent,
                            relativeVolume = s.RelativeVolume,
                            reactionScore = s.ReactionScore,
                            status = s.Status,
                            isStale = s.IsStale,
                        })
                        .FirstOrDefault(),
                })
                .OrderByDescending(e => e.importance)
                .ThenByDescending(e => e.lastSeenAt)
                .Take(driverCandidateLimit)
                .ToListAsync(ct);

            var marketDrivers = driverCandidates
                .Where(e => !string.IsNullOrWhiteSpace(e.symbol)
                    ? e.market is null || string.Equals(e.market.symbol, e.symbol, StringComparison.OrdinalIgnoreCase)
                    : e.market is null && (e.eventType == EventTypes.MacroRelease || e.sourceTier == SourceTiers.Primary))
                .OrderByDescending(e => e.market?.reactionScore ?? 0m)
                .ThenByDescending(e => e.importance)
                .ThenByDescending(e => e.lastSeenAt)
                .Take(driverLimit)
                .ToList();

            var symbolDrivers = driverCandidates
                .Where(e => !string.IsNullOrWhiteSpace(e.symbol)
                    && (e.market is null || string.Equals(e.market.symbol, e.symbol, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.market?.reactionScore ?? 0m)
                .ThenByDescending(e => e.importance)
                .ThenByDescending(e => e.lastSeenAt)
                .Take(120)
                .ToList();

            var sectorDetails = Sectors
                .Select(sector =>
                {
                    SectorMembers.TryGetValue(sector.Symbol, out var memberSymbols);
                    memberSymbols ??= EmptySymbols;
                    var members = breadthBase
                        .Where(q => memberSymbols.Contains(q.symbol))
                        .OrderByDescending(q => Math.Abs(q.changePercent ?? 0m))
                        .ThenBy(q => q.symbol)
                        .ToList();
                    var sectorDrivers = symbolDrivers
                        .Where(e => e.symbol is not null && memberSymbols.Contains(e.symbol))
                        .OrderByDescending(e => e.market?.reactionScore ?? 0m)
                        .ThenByDescending(e => e.importance)
                        .ThenByDescending(e => e.lastSeenAt)
                        .Take(12)
                        .ToList();

                    return new
                    {
                        symbol = sector.Symbol,
                        label = sector.Label,
                        group = sector.Group,
                        members,
                        drivers = sectorDrivers,
                    };
                })
                .ToList();

            var calendarFrom = now.AddHours(-12);
            var calendarTo = now.AddDays(7);
            var calendar = await db.EconomicEvents
                .AsNoTracking()
                .Where(e => e.ScheduledAt >= calendarFrom && e.ScheduledAt <= calendarTo)
                .OrderBy(e => e.ScheduledAt)
                .Take(20)
                .Select(e => new
                {
                    e.Id,
                    e.EventType,
                    e.Symbol,
                    e.Label,
                    e.ScheduledAt,
                    e.IsTimeSpecific,
                    e.Status,
                    e.Source,
                })
                .ToListAsync(ct);

            var latestQuoteAt = quotes.Count == 0 ? (DateTime?)null : quotes.Max(q => q.IngestedAt);
            var quoteStatuses = quotes
                .GroupBy(q => q.Status)
                .OrderByDescending(g => g.Count())
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToList();

            return Results.Ok(new
            {
                generatedAt = now,
                pulse,
                sectors,
                movers = moverItems,
                watchlist = breadthBase
                    .OrderByDescending(q => Math.Abs(q.changePercent ?? 0m))
                    .ThenBy(q => q.symbol)
                    .ToList(),
                sectorDetails,
                breadth = new
                {
                    total = breadthBase.Count,
                    advancers,
                    decliners,
                    unchanged,
                    averageMove,
                    positiveShare,
                },
                drivers = marketDrivers,
                calendar,
                freshness = new
                {
                    latestQuoteAt,
                    quoteStatuses,
                },
                weeklyPerformance,
            });
        });
    }

    private static WeeklyPerformanceDto BuildWeeklyPerformance(
        IReadOnlyList<WeeklyPerformanceGroupDefinition> groupDefinitions,
        IReadOnlyList<MarketQuoteDefinition> watchlistDefinitions,
        IReadOnlyDictionary<string, List<QuoteBar>> barsBySymbol,
        DateTime now)
    {
        var groups = groupDefinitions
            .Select(group => new WeeklyPerformanceGroup(
                group.Key,
                group.Label,
                group.Items
                    .Select(item => BuildWeeklyPerformanceItem(item, barsBySymbol, now))
                    .ToList()))
            .ToList();

        var groupItems = groups.SelectMany(g => g.items).ToList();
        var watchlistItems = watchlistDefinitions
            .Select(item => BuildWeeklyPerformanceItem(item, barsBySymbol, now))
            .Where(item => item.returnPct is not null)
            .OrderByDescending(item => Math.Abs(item.returnPct!.Value))
            .ThenBy(item => item.symbol)
            .Take(8)
            .ToList();

        var topMovers = watchlistItems.Count > 0
            ? watchlistItems
            : groupItems
                .Where(item => item.returnPct is not null)
                .OrderByDescending(item => Math.Abs(item.returnPct!.Value))
                .ThenBy(item => item.symbol)
                .Take(8)
                .ToList();

        var windowEnd = groupItems
            .Where(item => item.asOf is not null)
            .Select(item => item.asOf!.Value.Date)
            .DefaultIfEmpty(now.Date)
            .Max();
        var windowStart = windowEnd.AddDays(-7);

        var warnings = groupItems
            .Where(item => !string.IsNullOrWhiteSpace(item.warning))
            .Select(item => $"{item.symbol}: {item.warning}")
            .Take(12)
            .ToList();

        return new WeeklyPerformanceDto(windowStart, windowEnd, groups, topMovers, warnings);
    }

    private static WeeklyPerformanceItem BuildWeeklyPerformanceItem(
        MarketQuoteDefinition definition,
        IReadOnlyDictionary<string, List<QuoteBar>> barsBySymbol,
        DateTime now)
    {
        if (!barsBySymbol.TryGetValue(definition.Symbol, out var bars) || bars.Count == 0)
        {
            return new WeeklyPerformanceItem(
                definition.Symbol,
                definition.Label,
                returnPct: null,
                lastClose: null,
                startClose: null,
                status: "missing",
                warning: "No daily bars",
                stale: false,
                asOf: null);
        }

        var ordered = bars
            .OrderByDescending(b => b.Timestamp)
            .ToList();
        var end = ordered.First();
        var startTarget = end.Timestamp.Date.AddDays(-7);
        var start = ordered.FirstOrDefault(b => b.Timestamp.Date <= startTarget);

        var stale = end.Timestamp.Date < now.Date.AddDays(-3);
        string status;
        string? warning = null;
        decimal? returnPct = null;
        decimal? startClose = null;

        if (start is null)
        {
            status = "missing_start";
            warning = "No start bar for weekly comparison";
        }
        else if (start.Close == 0)
        {
            status = "missing_start";
            warning = "Start bar close is zero";
        }
        else
        {
            status = "ok";
            startClose = start.Close;
            returnPct = Math.Round((end.Close / start.Close - 1m) * 100m, 2);
        }

        if (stale)
        {
            status = "stale";
            warning = warning is null
                ? $"Latest daily bar is {end.Timestamp:yyyy-MM-dd}"
                : $"{warning}; latest daily bar is {end.Timestamp:yyyy-MM-dd}";
        }

        return new WeeklyPerformanceItem(
            definition.Symbol,
            definition.Label,
            returnPct,
            lastClose: end.Close,
            startClose,
            status,
            warning,
            stale,
            asOf: end.Timestamp);
    }

    private static MarketQuoteItem BuildQuoteItem(
        string symbol,
        string label,
        string group,
        IReadOnlyDictionary<string, MarketLens.Core.Entities.MarketQuote> quoteMap,
        IReadOnlyDictionary<string, List<QuoteBar>> barsBySymbol)
    {
        quoteMap.TryGetValue(symbol, out var quote);
        barsBySymbol.TryGetValue(symbol, out var bars);

        decimal? last = quote?.Last;
        decimal? previousClose = quote?.PreviousClose;
        decimal? change = quote?.Change;
        decimal? changePercent = quote?.ChangePercent;
        DateTime? asOf = quote?.AsOf;
        DateTime? ingestedAt = quote?.IngestedAt;
        string status = quote?.Status ?? "missing";
        var (provider, delayed) = ResolveServing(quote?.Status, quote?.Provider, quote?.AsOf, DateTime.UtcNow);
        string? source = quote is not null ? "quote" : null;

        if ((last is null || changePercent is null) && bars is { Count: >= 1 })
        {
            var latest = bars[0];
            var prior = bars.Count > 1 ? bars[1] : null;
            last ??= latest.Close;
            previousClose ??= prior?.Close;
            if (last is not null && previousClose is { } prev && prev != 0)
            {
                change ??= last - prev;
                changePercent ??= Math.Round((last.Value - prev) / prev * 100m, 4);
            }
            asOf ??= latest.Timestamp;
            ingestedAt ??= latest.IngestedAt;
            if (quote is null) { status = "ok:price_bar"; provider = "yahoo"; delayed = true; }
            source ??= "price_bar";
        }

        var weight = changePercent is null ? 1m : Math.Clamp(Math.Abs(changePercent.Value), 0.35m, 5m);

        return new MarketQuoteItem(
            symbol,
            label,
            group,
            last,
            previousClose,
            change,
            changePercent,
            asOf,
            ingestedAt,
            status,
            provider,
            source,
            weight,
            delayed);
    }

    private static readonly IReadOnlySet<string> EmptySymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> SymbolSet(params string[] symbols)
        => new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);

    // Status encodes who actually served the value; the stored Provider column is the
    // ingest channel (always "polygon") and lies on fallback. Derive the truth here.
    internal static (string? provider, bool delayed) ResolveServing(
        string? status, string? storedProvider, DateTime? asOf, DateTime now)
    {
        var s = status ?? string.Empty;
        var live = s == "ok" || s.StartsWith("ok:", StringComparison.Ordinal);
        if (!live)
            return (storedProvider, false);
        // Yahoo intraday is near-real-time during market hours; off-hours it returns the prior close.
        var delayed = asOf is null || asOf.Value < now.AddMinutes(-20);
        return (storedProvider ?? "yahoo", delayed);
    }
}

internal sealed record MarketQuoteDefinition(string Symbol, string Label, string Group);

internal sealed record QuoteBar(string Symbol, DateTime Timestamp, decimal Close, long? Volume, DateTime IngestedAt);

internal sealed record WeeklyPerformanceGroupDefinition(
    string Key,
    string Label,
    IReadOnlyList<MarketQuoteDefinition> Items);

internal sealed record WeeklyPerformanceDto(
    DateTime windowStart,
    DateTime windowEnd,
    IReadOnlyList<WeeklyPerformanceGroup> groups,
    IReadOnlyList<WeeklyPerformanceItem> topMovers,
    IReadOnlyList<string> warnings);

internal sealed record WeeklyPerformanceGroup(
    string key,
    string label,
    IReadOnlyList<WeeklyPerformanceItem> items);

internal sealed record WeeklyPerformanceItem(
    string symbol,
    string label,
    decimal? returnPct,
    decimal? lastClose,
    decimal? startClose,
    string status,
    string? warning,
    bool stale,
    DateTime? asOf);

internal sealed record MarketQuoteItem(
    string symbol,
    string label,
    string group,
    decimal? last,
    decimal? previousClose,
    decimal? change,
    decimal? changePercent,
    DateTime? asOf,
    DateTime? ingestedAt,
    string status,
    string? provider,
    string? source,
    decimal weight,
    bool delayed);
