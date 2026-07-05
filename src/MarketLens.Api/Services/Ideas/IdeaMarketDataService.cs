using MarketLens.Core.Domain;

namespace MarketLens.Api.Services.Ideas;

public class IdeaMarketDataService(
    IdeaMarketDataLoader loader,
    CompanyFundamentalsService fundamentalsService)
{
    public async Task<object> GetRadarAsync(int? windowDays, int? take, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var days = Math.Clamp(windowDays ?? 14, 1, 120);
        var limit = Math.Clamp(take ?? 30, 5, 100);
        var windowStart = now.AddDays(-days);
        var insiderStart = now.AddDays(-Math.Max(90, days));

        var eventRows = await loader.LoadEventRowsAsync(windowStart, ct);
        var sourceRows = await loader.LoadArticleSourceRowsAsync(windowStart, ct);
        var insiderRows = await loader.LoadInsiderRowsAsync(insiderStart, ct);
        var candidateSymbols = BuildCandidateSymbols(eventRows, sourceRows, insiderRows, 350);

        var priceRows = await loader.LoadPriceRowsAsync(candidateSymbols, now.AddDays(-390), ct);
        var calendarRows = await loader.LoadCalendarRowsAsync(candidateSymbols, now.AddDays(-2), now.AddDays(90), ct);
        var fundamentalRows = await loader.LoadFundamentalsAsync(candidateSymbols, ct);
        var grouped = GroupMarketInputs(eventRows, sourceRows, insiderRows, priceRows, calendarRows, fundamentalRows);

        var items = candidateSymbols
            .Select(symbol => IdeaScoring.BuildIdea(symbol, now,
                grouped.Events.GetValueOrDefault(symbol) ?? [],
                grouped.Sources.GetValueOrDefault(symbol) ?? [],
                grouped.Insiders.GetValueOrDefault(symbol) ?? [],
                grouped.Prices.GetValueOrDefault(symbol) ?? [],
                grouped.Calendar.GetValueOrDefault(symbol) ?? [],
                grouped.Fundamentals.GetValueOrDefault(symbol)))
            .Where(i => i.InterestScore >= 18m || i.Scouts.EventIntensity >= 0.18m || i.Scouts.InsiderSignal >= 0.25m)
            .OrderByDescending(i => i.InterestScore)
            .ThenByDescending(i => i.Scouts.SourceQuality)
            .ThenByDescending(i => i.LatestSignalAt)
            .Take(limit)
            .ToList();

        return new
        {
            generatedAt = now,
            windowDays = days,
            windowStart,
            universe = new
            {
                candidates = candidateSymbols.Count,
                eventRows = eventRows.Count,
                symbolsWithPrices = grouped.Prices.Count,
                symbolsWithInsiders = grouped.Insiders.Count,
                symbolsWithFundamentals = grouped.Fundamentals.Count,
            },
            items,
        };
    }

    public async Task<object> GetSymbolBriefAsync(string symbol, int? windowDays, CancellationToken ct)
    {
        var s = symbol.Trim().ToUpperInvariant();
        if (!IdeaScoring.IsEquitySymbol(s)) throw new ArgumentException("equity symbol required");

        var now = DateTime.UtcNow;
        var days = Math.Clamp(windowDays ?? 60, 7, 365);
        var windowStart = now.AddDays(-days);
        var insiderStart = now.AddDays(-180);

        var meta = TickerMetadata.Lookup(s);
        var eventRows = await loader.LoadEventRowsAsync(windowStart, ct, s);
        var sourceRows = await loader.LoadArticleSourceRowsAsync(windowStart, ct, s);
        var insiderRows = await loader.LoadInsiderRowsAsync(insiderStart, ct, s);
        var priceRows = await loader.LoadPriceRowsAsync([s], now.AddDays(-390), ct);
        var calendarRows = await loader.LoadCalendarRowsAsync([s], now.AddDays(-7), now.AddDays(120), ct);
        var thesisRows = await loader.LoadThesisRowsAsync(s, ct);
        var transcriptRows = await loader.LoadTranscriptRowsAsync(s, ct);
        var chunkRows = await loader.LoadFilingChunkRowsAsync(s, windowStart, ct);
        var fundamentals = await fundamentalsService.GetOrRefreshAsync(s, TimeSpan.FromHours(24), ct);

        var signalEventRows = IdeaCompanyContextFilter.Filter(s, eventRows);
        var idea = IdeaScoring.BuildIdea(s, now, signalEventRows, sourceRows, insiderRows, priceRows, calendarRows, fundamentals);
        var overpricing = IdeaBriefBuilder.BuildOverpricingSummary(idea, fundamentals);

        return new
        {
            generatedAt = now,
            windowDays = days,
            symbol = s,
            metadata = meta is null ? null : new
            {
                meta.CompanyName,
                meta.Cik,
                meta.IrFeedUrl,
                meta.Aliases,
            },
            idea,
            price = IdeaBriefBuilder.BuildPriceSummary(priceRows),
            fundamentals = IdeaBriefBuilder.BuildFundamentalsSummary(fundamentals, idea.Price.Return30d, idea.Price.Return90d),
            overpricing,
            sourceMix = IdeaBriefBuilder.BuildSourceMix(sourceRows),
            eventMix = BuildEventMix(signalEventRows),
            topEvents = BuildTopEvents(signalEventRows),
            insiders = IdeaBriefBuilder.BuildInsiderSummary(insiderRows),
            theses = thesisRows,
            transcripts = transcriptRows,
            filingChunks = chunkRows,
            calendar = calendarRows.OrderBy(c => c.ScheduledAt).Take(12),
            brief = IdeaBriefBuilder.BuildBriefNarrative(idea, fundamentals, overpricing, signalEventRows, thesisRows, transcriptRows, chunkRows),
            dataGaps = IdeaBriefBuilder.BuildDataGaps(fundamentals, transcriptRows, chunkRows, thesisRows),
        };
    }

    internal async Task<ForwardIdeaUniverse> LoadForwardUniverseAsync(ForwardThesisSpec spec, int days, DateTime now, CancellationToken ct)
    {
        var windowStart = now.AddDays(-days);
        var insiderStart = now.AddDays(-Math.Max(120, days));

        var eventRows = await loader.LoadEventRowsAsync(windowStart, ct);
        var sourceRows = await loader.LoadArticleSourceRowsAsync(windowStart, ct);
        var insiderRows = await loader.LoadInsiderRowsAsync(insiderStart, ct);

        var signalSymbols = BuildCandidateSymbols(eventRows, sourceRows, insiderRows, 350);
        var candidateSymbols = spec.Groups.SelectMany(g => g.Symbols)
            .Concat(signalSymbols)
            .Where(IdeaScoring.IsEquitySymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();

        var priceRows = await loader.LoadPriceRowsAsync(candidateSymbols, now.AddDays(-390), ct);
        var calendarRows = await loader.LoadCalendarRowsAsync(candidateSymbols, now.AddDays(-2), now.AddDays(120), ct);
        var fundamentalRows = await loader.LoadFundamentalsAsync(candidateSymbols, ct);
        var grouped = GroupMarketInputs(eventRows, sourceRows, insiderRows, priceRows, calendarRows, fundamentalRows);

        var contexts = candidateSymbols
            .Select(symbol =>
            {
                var events = grouped.Events.GetValueOrDefault(symbol) ?? [];
                var sources = grouped.Sources.GetValueOrDefault(symbol) ?? [];
                var insiders = grouped.Insiders.GetValueOrDefault(symbol) ?? [];
                var prices = grouped.Prices.GetValueOrDefault(symbol) ?? [];
                var calendar = grouped.Calendar.GetValueOrDefault(symbol) ?? [];
                var fundamentals = grouped.Fundamentals.GetValueOrDefault(symbol);
                var radar = IdeaScoring.BuildIdea(symbol, now, events, sources, insiders, prices, calendar, fundamentals);
                return new ForwardIdeaContext(symbol, radar, events, sources, insiders, prices, calendar, fundamentals);
            })
            .ToList();

        return new ForwardIdeaUniverse(
            Contexts: contexts,
            CandidateCount: candidateSymbols.Count,
            EventRowCount: eventRows.Count,
            SymbolsWithPrices: grouped.Prices.Count,
            SymbolsWithFundamentals: grouped.Fundamentals.Count);
    }

    private static List<string> BuildCandidateSymbols(
        IEnumerable<IdeaEventRow> eventRows,
        IEnumerable<IdeaSourceRow> sourceRows,
        IEnumerable<IdeaInsiderRow> insiderRows,
        int limit) =>
        eventRows.Select(e => e.Symbol)
            .Concat(sourceRows.Select(s => s.Symbol))
            .Concat(insiderRows.Select(i => i.Symbol))
            .Where(IdeaScoring.IsEquitySymbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();

    private static IdeaMarketInputGroups GroupMarketInputs(
        IReadOnlyList<IdeaEventRow> eventRows,
        IReadOnlyList<IdeaSourceRow> sourceRows,
        IReadOnlyList<IdeaInsiderRow> insiderRows,
        IReadOnlyList<IdeaPriceRow> priceRows,
        IReadOnlyList<IdeaCalendarRow> calendarRows,
        IReadOnlyList<MarketLens.Core.Entities.CompanyFundamentals> fundamentalRows) =>
        new(
            Events: eventRows.GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => IdeaCompanyContextFilter.Filter(g.Key, g.ToList()), StringComparer.OrdinalIgnoreCase),
            Sources: sourceRows.GroupBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase),
            Insiders: insiderRows.GroupBy(i => i.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase),
            Prices: priceRows.GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Timestamp).ToList(), StringComparer.OrdinalIgnoreCase),
            Calendar: calendarRows.GroupBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(c => c.ScheduledAt).ToList(), StringComparer.OrdinalIgnoreCase),
            Fundamentals: fundamentalRows.GroupBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.IngestedAt).First(), StringComparer.OrdinalIgnoreCase));

    private static object BuildEventMix(IEnumerable<IdeaEventRow> rows) =>
        rows.GroupBy(e => e.EventType)
            .Select(g => new { eventType = g.Key, count = g.Count(), maxImportance = g.Max(x => x.Importance) })
            .OrderByDescending(x => x.maxImportance)
            .ThenByDescending(x => x.count)
            .ToList();

    private static object BuildTopEvents(IEnumerable<IdeaEventRow> rows) =>
        rows.OrderByDescending(e => e.Importance)
            .ThenByDescending(e => e.LastSeenAt)
            .Take(14)
            .Select(e => new
            {
                e.ClusterId,
                e.EventType,
                e.Summary,
                e.Importance,
                e.Sentiment,
                e.SourceTier,
                e.MemberCount,
                e.LastSeenAt,
                market = new
                {
                    e.ReactionScore,
                    e.MovePercent,
                    e.RelativeMovePercent,
                    e.RelativeVolume,
                },
                topSource = new
                {
                    e.TopSource,
                    e.TopPublisher,
                    e.TopHeadline,
                    e.TopUrl,
                },
            })
            .ToList();
}
