using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.HostedServices;

public class HistoricalBackfillService(
    IServiceProvider services,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<HistoricalBackfillService> logger) : BackgroundService
{
    private static readonly DateTime CutoffUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration["BACKFILL_HISTORY"];
        if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("HistoricalBackfillService: BACKFILL_HISTORY not set, skipping");
            return;
        }

        // Wait for sidecars to come up before starting.
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        logger.LogInformation("HistoricalBackfillService: starting historical backfill since {Cutoff:yyyy-MM-dd}", CutoffUtc);

        try
        {
            await RunBackfillAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HistoricalBackfillService: backfill failed");
        }

        logger.LogInformation("HistoricalBackfillService: backfill complete");
    }

    private async Task RunBackfillAsync(CancellationToken cancellationToken)
    {
        var triageThreshold = configuration.GetValue<decimal>("Ingestion:TriageThreshold", 0.40m);

        var httpClient = httpClientFactory.CreateClient("historical_backfill");

        using var scope = services.CreateScope();
        var watchlist = scope.ServiceProvider.GetRequiredService<IWatchlistProvider>();
        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);

        var allArticles = new List<IngestedArticle>();

        // EDGAR — drop the 7-day filter, walk all entries since cutoff
        logger.LogInformation("HistoricalBackfillService: fetching EDGAR since {Cutoff:yyyy-MM-dd}", CutoffUtc);
        var edgarArticles = await FetchEdgarAsync(httpClient, watched, cancellationToken);
        logger.LogInformation("HistoricalBackfillService: EDGAR fetched {Count} articles", edgarArticles.Count);
        allArticles.AddRange(edgarArticles);

        // CourtListener — page through results with filed_after=2026-04-01
        logger.LogInformation("HistoricalBackfillService: fetching CourtListener since {Cutoff:yyyy-MM-dd}", CutoffUtc);
        var courtArticles = await FetchCourtListenerAsync(httpClient, watched, cancellationToken);
        logger.LogInformation("HistoricalBackfillService: CourtListener fetched {Count} articles", courtArticles.Count);
        allArticles.AddRange(courtArticles);

        // RSS feeds — fetch with LookbackDays=120 to capture everything the feed retains
        logger.LogInformation("HistoricalBackfillService: fetching RSS feeds since {Cutoff:yyyy-MM-dd}", CutoffUtc);
        var rssArticles = await FetchRssFeedsAsync(httpClient, watched, cancellationToken);
        logger.LogInformation("HistoricalBackfillService: RSS feeds fetched {Count} articles", rssArticles.Count);
        allArticles.AddRange(rssArticles);

        // FRED — use observation_start=2026-04-01
        logger.LogInformation("HistoricalBackfillService: fetching FRED since {Cutoff:yyyy-MM-dd}", CutoffUtc);
        var fredArticles = await FetchFredAsync(httpClient, cancellationToken);
        logger.LogInformation("HistoricalBackfillService: FRED fetched {Count} articles", fredArticles.Count);
        allArticles.AddRange(fredArticles);

        // Finnhub — expand to 30-day window (free tier max)
        logger.LogInformation("HistoricalBackfillService: fetching Finnhub (30-day window)");
        var finnhubArticles = await FetchFinnhubAsync(httpClient, watched, cancellationToken);
        logger.LogInformation("HistoricalBackfillService: Finnhub fetched {Count} articles", finnhubArticles.Count);
        allArticles.AddRange(finnhubArticles);

        logger.LogInformation("HistoricalBackfillService: total fetched across all sources: {Total}", allArticles.Count);

        await EnqueueArticlesAsync(allArticles, triageThreshold, cancellationToken);
    }

    private async Task<List<IngestedArticle>> FetchEdgarAsync(
        HttpClient httpClient,
        IReadOnlyList<WatchedTicker> watched,
        CancellationToken cancellationToken)
    {
        var results = new List<IngestedArticle>();
        var withCik = watched.Where(w => !string.IsNullOrWhiteSpace(w.Cik)).ToList();

        foreach (var entry in withCik)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"https://data.sec.gov/submissions/CIK{entry.Cik}.json";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var recent = doc.RootElement.GetProperty("filings").GetProperty("recent");

                var forms = recent.GetProperty("form");
                var accessions = recent.GetProperty("accessionNumber");
                var dates = recent.GetProperty("filingDate");
                var primaryDocs = recent.GetProperty("primaryDocument");
                var items = recent.TryGetProperty("items", out var i) ? i : default;

                for (int idx = 0; idx < forms.GetArrayLength(); idx++)
                {
                    var form = forms[idx].GetString();
                    if (!SecFormDescriptions.IsTracked(form)) continue;

                    var accession = accessions[idx].GetString() ?? string.Empty;
                    var filingDate = dates[idx].GetString() ?? string.Empty;
                    var primaryDoc = primaryDocs[idx].GetString() ?? string.Empty;
                    var itemString = items.ValueKind == JsonValueKind.Array && idx < items.GetArrayLength()
                        ? items[idx].GetString() ?? string.Empty
                        : string.Empty;

                    if (!DateTime.TryParse(filingDate, out var publishedAt)) continue;
                    publishedAt = DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
                    if (publishedAt < CutoffUtc) continue;

                    var (headline, summary) = BuildEdgarFilingText(entry.Name, form!, itemString);
                    var cikNoZeros = long.Parse(entry.Cik!);
                    var accNoDashes = accession.Replace("-", "");
                    var filingUrl = $"https://www.sec.gov/Archives/edgar/data/{cikNoZeros}/{accNoDashes}/{primaryDoc}";

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Edgar,
                        SourceId: accession,
                        Symbol: entry.Symbol,
                        Headline: headline,
                        Summary: summary,
                        Url: filingUrl,
                        Publisher: "SEC EDGAR",
                        PublishedAt: publishedAt,
                        RawJson: JsonSerializer.Serialize(new
                        {
                            cik = entry.Cik,
                            ticker = entry.Symbol,
                            accession,
                            items = itemString,
                            form,
                        })));
                }

                await Task.Delay(120, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "HistoricalBackfillService EDGAR: fetch failed for {Ticker} ({Cik})", entry.Symbol, entry.Cik);
            }
        }

        return results;
    }

    private static (string Headline, string Summary) BuildEdgarFilingText(string companyName, string form, string itemString)
    {
        if (form.StartsWith("8-K", StringComparison.OrdinalIgnoreCase))
        {
            var itemList = itemString
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            var headline = itemList.Length > 0
                ? $"{companyName} {form}: {string.Join("; ", itemList.Select(SecItemDescriptions.Describe))}"
                : $"{companyName} {form} filing";
            var summary = itemList.Length > 0
                ? string.Join("\n", itemList.Select(it => $"Item {it}: {SecItemDescriptions.Describe(it)}"))
                : "current report";
            return (headline, summary);
        }

        var description = SecFormDescriptions.Describe(form);
        return ($"{companyName} {form}: {description}", description);
    }

    private async Task<List<IngestedArticle>> FetchCourtListenerAsync(
        HttpClient httpClient,
        IReadOnlyList<WatchedTicker> watched,
        CancellationToken cancellationToken)
    {
        const string baseUrl = "https://www.courtlistener.com/api/rest/v4/search/";
        const int maxPagesPerQuery = 100;
        var cutoffStr = CutoffUtc.ToString("yyyy-MM-dd");
        var results = new List<IngestedArticle>();
        var seenDockets = new HashSet<string>();

        foreach (var ticker in watched)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queries = BuildCourtListenerSearchTerms(ticker);

            foreach (var q in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = 1;
                var hasMore = true;

                while (hasMore && page <= maxPagesPerQuery)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var encoded = Uri.EscapeDataString(q);
                        var url = $"{baseUrl}?type=r&q={encoded}&filed_after={cutoffStr}&order_by=dateFiled+desc&format=json&page={page}";
                        var json = await httpClient.GetStringAsync(url, cancellationToken);
                        using var doc = JsonDocument.Parse(json);

                        if (!doc.RootElement.TryGetProperty("results", out var resultsEl))
                        {
                            hasMore = false;
                            break;
                        }

                        var pageResults = resultsEl.EnumerateArray().ToList();
                        if (pageResults.Count == 0)
                        {
                            hasMore = false;
                            break;
                        }

                        foreach (var item in pageResults)
                        {
                            var article = ParseCourtListenerDocket(item, ticker.Symbol);
                            if (article is not null && seenDockets.Add(article.SourceId))
                                results.Add(article);
                        }

                        // CourtListener returns next=null when there are no more pages
                        hasMore = doc.RootElement.TryGetProperty("next", out var next) &&
                                  next.ValueKind != JsonValueKind.Null &&
                                  !string.IsNullOrWhiteSpace(next.GetString());
                        page++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "HistoricalBackfillService CourtListener: fetch failed for {Ticker} query '{Query}' page {Page}",
                            ticker.Symbol, q, page);
                        hasMore = false;
                    }

                    await Task.Delay(200, cancellationToken);
                }
            }
        }

        return results;
    }

    private static IReadOnlyList<string> BuildCourtListenerSearchTerms(WatchedTicker ticker)
    {
        var terms = new List<string> { $"\"{ticker.Name}\"" };
        foreach (var alias in ticker.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
                terms.Add($"\"{alias}\"");
        }
        return terms;
    }

    private static IngestedArticle? ParseCourtListenerDocket(JsonElement item, string symbol)
    {
        var caseName = item.TryGetProperty("caseName", out var cn) ? cn.GetString() : null;
        if (string.IsNullOrWhiteSpace(caseName)) return null;

        var docketId = item.TryGetProperty("docket_id", out var did) ? did.GetInt64().ToString() : null;
        var docketUrl = item.TryGetProperty("docket_absolute_url", out var durl) ? durl.GetString() : null;
        var dateFiled = item.TryGetProperty("dateFiled", out var df) ? df.GetString() : null;
        var court = item.TryGetProperty("court", out var ct) ? ct.GetString() : null;
        var cause = item.TryGetProperty("cause", out var ca) ? ca.GetString() : null;
        var docketNumber = item.TryGetProperty("docketNumber", out var dn) ? dn.GetString() : null;

        if (string.IsNullOrWhiteSpace(docketId)) return null;

        if (!DateTime.TryParse(dateFiled, out var publishedAt))
            publishedAt = DateTime.UtcNow;
        publishedAt = DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);

        var headline = $"{caseName} — {court ?? "Federal Court"}";
        var sb = new StringBuilder();
        sb.Append($"Case: {caseName}.");
        if (!string.IsNullOrWhiteSpace(court)) sb.Append($" Court: {court}.");
        if (!string.IsNullOrWhiteSpace(cause)) sb.Append($" Cause: {cause}.");
        if (!string.IsNullOrWhiteSpace(docketNumber)) sb.Append($" Docket: {docketNumber}.");
        if (!string.IsNullOrWhiteSpace(dateFiled)) sb.Append($" Filed: {dateFiled}.");
        var summary = sb.ToString();

        var fullUrl = string.IsNullOrWhiteSpace(docketUrl)
            ? $"https://www.courtlistener.com/docket/{docketId}/"
            : $"https://www.courtlistener.com{docketUrl}";

        var stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"courtlistener:{docketId}"))).ToLowerInvariant();

        return new IngestedArticle(
            Source: SourceNames.CourtListener,
            SourceId: stableId,
            Symbol: symbol,
            Headline: headline,
            Summary: summary,
            Url: fullUrl,
            Publisher: "CourtListener / PACER",
            PublishedAt: publishedAt,
            RawJson: item.GetRawText());
    }

    private async Task<List<IngestedArticle>> FetchRssFeedsAsync(
        HttpClient httpClient,
        IReadOnlyList<WatchedTicker> watched,
        CancellationToken cancellationToken)
    {
        var results = new List<IngestedArticle>();
        var rssConfigs = configuration.GetSection("RssFeeds").Get<List<RssFeedConfig>>() ?? [];

        // Note on live retention limits:
        // - fed_speeches / fed_press: federalreserve.gov feeds retain ~90 days; 120-day window will reach April.
        // - bls: bls.gov feeds are thin (4 items typical); content since April is present but sparse.
        // - bea: apps.bea.gov retains ~90 days; 120-day window reaches April.
        // - sec_enforcement / ftc / doj_antitrust: government press release feeds vary 30-90 days.
        // - business_wire / globe_newswire / pr_newswire: wire feeds retain 7-30 days typically.
        //   The GlobeNewswire/PRNewswire RSS feeds retain only ~7-14 days of items; items before
        //   ~2026-04-19 will not appear in live feeds. This is an honest live-retention limit,
        //   not a bug in the backfill.
        // - mining_com: feed retains ~30 days.
        // - ir_feed: Apple/MSFT/NVDA/Google/Amazon newsroom feeds retain 30-90+ items; most should
        //   reach April.

        foreach (var cfg in rssConfigs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var xml = await httpClient.GetStringAsync(cfg.Url, cancellationToken);
                var doc = XDocument.Parse(xml);
                var items = RssParsing.ParseItems(doc, cfg).ToList();

                var feedCount = 0;
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.PublishedAt < CutoffUtc) continue;

                    IngestedArticle candidate;
                    if (cfg.FilterByWatchlist)
                    {
                        var matched = WatchlistMatcher.MatchSymbol(watched, item.Headline, item.Summary);
                        if (matched is null) continue;
                        candidate = item with { Symbol = matched };
                    }
                    else
                    {
                        if (cfg.Source == SourceNames.IrFeed &&
                            !MarketMateriality.IsCompanyFeedMaterial(item.Headline, item.Summary))
                        {
                            continue;
                        }
                        candidate = item with { Symbol = item.Symbol ?? cfg.Symbol };
                    }

                    results.Add(candidate);
                    feedCount++;
                }

                logger.LogInformation("HistoricalBackfillService RSS: {Source} {Url} → {Count} articles since cutoff",
                    cfg.Source, cfg.Url, feedCount);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "HistoricalBackfillService RSS: fetch failed for {Source} {Url}", cfg.Source, cfg.Url);
            }
        }

        return results;
    }

    private async Task<List<IngestedArticle>> FetchFredAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var results = new List<IngestedArticle>();
        var fredOptions = configuration.GetSection("Fred").Get<FredOptions>() ?? new FredOptions();

        if (string.IsNullOrWhiteSpace(fredOptions.ApiKey))
        {
            logger.LogWarning("HistoricalBackfillService: FRED key missing — skipping FRED backfill");
            return results;
        }

        var observationStart = CutoffUtc.ToString("yyyy-MM-dd");

        foreach (var seriesId in fredOptions.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"{fredOptions.BaseUrl}/series/observations?series_id={seriesId}&observation_start={observationStart}&api_key={fredOptions.ApiKey}&file_type=json";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("observations", out var obs)) continue;

                foreach (var o in obs.EnumerateArray())
                {
                    var dateStr = o.GetProperty("date").GetString() ?? string.Empty;
                    var value = o.GetProperty("value").GetString() ?? "";
                    if (!DateTime.TryParse(dateStr, out var dt)) continue;
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Fred,
                        SourceId: $"{seriesId}:{dateStr}",
                        Symbol: null,
                        Headline: $"FRED {seriesId} release: {value} ({dateStr})",
                        Summary: $"Federal Reserve Economic Data series {seriesId} observation {value} for {dateStr}.",
                        Url: $"https://fred.stlouisfed.org/series/{seriesId}",
                        Publisher: "Federal Reserve Bank of St. Louis",
                        PublishedAt: dt,
                        RawJson: o.GetRawText()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "HistoricalBackfillService: FRED fetch failed for {Series}", seriesId);
            }
        }

        return results;
    }

    private async Task<List<IngestedArticle>> FetchFinnhubAsync(
        HttpClient httpClient,
        IReadOnlyList<WatchedTicker> watched,
        CancellationToken cancellationToken)
    {
        var results = new List<IngestedArticle>();
        var finnhubOptions = configuration.GetSection("Finnhub").Get<FinnhubOptions>() ?? new FinnhubOptions();

        if (string.IsNullOrWhiteSpace(finnhubOptions.ApiKey))
        {
            logger.LogWarning("HistoricalBackfillService: Finnhub key missing — skipping Finnhub backfill");
            return results;
        }

        // Finnhub free tier max lookback is 30 days
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-30);

        foreach (var entry in watched)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = $"{finnhubOptions.BaseUrl}/company-news?symbol={Uri.EscapeDataString(entry.Symbol)}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&token={finnhubOptions.ApiKey}";
                var response = await httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("HistoricalBackfillService Finnhub: {Status} for {Ticker}", response.StatusCode, entry.Symbol);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var id = element.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                        ? idProp.GetInt64() : 0;
                    if (id == 0) continue;

                    var headline = element.TryGetProperty("headline", out var hl) ? hl.GetString() ?? "" : "";
                    var summary = element.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var newsUrl = element.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    var publisher = element.TryGetProperty("source", out var src) ? src.GetString() : null;
                    var datetime = element.TryGetProperty("datetime", out var dt) && dt.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(dt.GetInt64()).UtcDateTime
                        : DateTime.UtcNow;
                    datetime = DateTime.SpecifyKind(datetime, DateTimeKind.Utc);

                    if (datetime < CutoffUtc) continue;

                    if (!FinnhubMentionsEntity(entry, headline, summary)) continue;
                    if (!MarketMateriality.IsAggregatorMaterial(headline, summary)) continue;

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Finnhub,
                        SourceId: id.ToString(),
                        Symbol: entry.Symbol,
                        Headline: headline,
                        Summary: summary,
                        Url: newsUrl,
                        Publisher: publisher,
                        PublishedAt: datetime,
                        RawJson: element.GetRawText()));
                }

                await Task.Delay(finnhubOptions.DelayBetweenSymbolsMs, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "HistoricalBackfillService Finnhub: fetch failed for {Ticker}", entry.Symbol);
            }
        }

        return results;
    }

    private static bool FinnhubMentionsEntity(WatchedTicker entry, string headline, string summary)
    {
        var haystack = $"{headline} {summary}";
        return entry.Aliases.Any(alias =>
            !string.IsNullOrWhiteSpace(alias) &&
            haystack.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnqueueArticlesAsync(
        List<IngestedArticle> allArticles,
        decimal triageThreshold,
        CancellationToken cancellationToken)
    {
        // Deduplicate within the fetched batch by SourceId
        var deduped = allArticles
            .GroupBy(a => (a.Source, a.SourceId))
            .Select(g => g.First())
            .ToList();

        logger.LogInformation("HistoricalBackfillService: {Total} articles after in-batch dedup (from {Raw} raw)", deduped.Count, allArticles.Count);

        var bodyFetchDelayMs = configuration.GetValue<int>("Ingestion:BodyFetchDelayMs", 0);
        var perArticleDelayMs = configuration.GetValue<int>("Ingestion:PerArticleDelayMs", 0);
        var maxQueueItems = Math.Max(1, configuration.GetValue<int>("BACKFILL_HISTORY_MAX_QUEUE_ITEMS", 1000));
        var maxQueueItemsPerSource = Math.Max(1, configuration.GetValue<int>("BACKFILL_HISTORY_MAX_QUEUE_ITEMS_PER_SOURCE", 250));

        // Group by source for per-source skip-check against the DB.
        var bySource = deduped.GroupBy(a => a.Source).ToList();

        var queuedTotal = 0;

        foreach (var sourceGroup in bySource)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queuedTotal >= maxQueueItems)
                break;

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

            var sourceName = sourceGroup.Key;
            var fetched = sourceGroup.ToList();

            var sourceIds = fetched.Select(a => a.SourceId).ToList();
            var existing = await db.Articles
                .Where(a => a.Source == sourceName && sourceIds.Contains(a.SourceId))
                .Select(a => a.SourceId)
                .ToListAsync(cancellationToken);
            var existingSet = existing.ToHashSet();

            var fresh = fetched.Where(a => !existingSet.Contains(a.SourceId)).ToList();
            if (fresh.Count == 0)
            {
                logger.LogInformation("HistoricalBackfillService: {Source} — all {Count} articles already exist, skipping", sourceName, fetched.Count);
                continue;
            }

            logger.LogInformation("HistoricalBackfillService: {Source} — {Fresh} new articles to queue ({Existing} already existed)",
                sourceName, fresh.Count, existingSet.Count);

            var remaining = maxQueueItems - queuedTotal;
            var cappedFresh = fresh
                .Take(Math.Min(maxQueueItemsPerSource, remaining))
                .ToList();

            var queuedForSource = 0;
            foreach (var article in cappedFresh)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queuedArticle = PrepareForQueue(article);
                var payload = new ArticleBodyEnrichmentPayload(
                    queuedArticle,
                    bodyFetchDelayMs,
                    perArticleDelayMs,
                    triageThreshold);

                await queue.EnqueueAsync(
                    new EnqueueWorkRequest(
                        WorkType: PipelineWorkTypes.ArticleBodyEnrichment,
                        NaturalKey: NaturalKey(queuedArticle),
                        PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
                        Priority: PriorityFromPublishedAt(queuedArticle.PublishedAt)),
                    cancellationToken);

                queuedForSource++;
                queuedTotal++;
            }

            logger.LogInformation(
                "HistoricalBackfillService: {Source} queued {Queued} article finalization jobs ({Fresh} fresh, per-source cap {PerSourceCap})",
                sourceName,
                queuedForSource,
                fresh.Count,
                maxQueueItemsPerSource);
        }

        logger.LogInformation(
            "HistoricalBackfillService: queued {Total} article finalization jobs (global cap {GlobalCap})",
            queuedTotal,
            maxQueueItems);
    }

    private static IngestedArticle PrepareForQueue(IngestedArticle article)
        => article with
        {
            NeedsBodyFetch = article.NeedsBodyFetch || ShouldFetchBody(article),
        };

    private static bool ShouldFetchBody(IngestedArticle article)
        => !string.IsNullOrWhiteSpace(article.Url) &&
           (string.IsNullOrWhiteSpace(article.Summary) || article.Summary.Trim().Length < 200);

    private static string NaturalKey(IngestedArticle article)
        => $"{article.Source}:{article.SourceId}";

    private static int PriorityFromPublishedAt(DateTime publishedAt)
    {
        var utc = publishedAt.Kind == DateTimeKind.Utc
            ? publishedAt
            : DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
        var minutes = (utc - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        return (int)Math.Clamp(minutes, 0, int.MaxValue);
    }
}
