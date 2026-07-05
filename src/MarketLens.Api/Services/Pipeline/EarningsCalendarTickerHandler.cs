using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Services.Pipeline;

public sealed record EarningsCalendarTickerResult(bool Processed, bool TranscriptQueued, bool ManualActionCreated, bool Current);

public sealed class EarningsCalendarTickerHandler(
    MarketLensDbContext db,
    IHttpClientFactory httpClientFactory,
    AudioReplayDiscovery audioDiscovery,
    ILogger<EarningsCalendarTickerHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Regex AudioExtension =
        new(@"\.(mp3|mp4|m4a|wav|ogg|webm|aac)(\?|$)", RegexOptions.IgnoreCase);

    private static readonly string[] EarningsKeywords =
    [
        "earnings", "quarterly results", "financial results", "quarter results",
        "q1 ", "q2 ", "q3 ", "q4 ", "first quarter", "second quarter",
        "third quarter", "fourth quarter",
    ];

    private static readonly string[] WebcastVendorPatterns =
    [
        "vcall.com", "earningscast.com", "companyboardroom.com",
        "viavid.com", "streetevents.com", "wsw.com",
        "ir.q4cdn.com", "edge.media-server.com",
        "event.webcasts.com", "onlinepresentations.com",
    ];

    public async Task<EarningsCalendarTickerResult> ProcessAsync(
        string naturalKey,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(payloadJson);
        var symbol = string.IsNullOrWhiteSpace(payload.Symbol) ? naturalKey : payload.Symbol!;
        symbol = symbol.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return new EarningsCalendarTickerResult(false, false, false, false);

        var ticker = new WatchedTicker(
            payload.AssetId,
            symbol,
            payload.Name ?? symbol,
            payload.Cik,
            payload.IrFeedUrl,
            payload.Aliases ?? []);

        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, payload.LookbackHours));
        var http = httpClientFactory.CreateClient("earnings_calendar");
        return await ProcessTickerAsync(http, ticker, cutoff, cancellationToken);
    }

    private async Task<EarningsCalendarTickerResult> ProcessTickerAsync(
        HttpClient http,
        WatchedTicker ticker,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var callDate = await DetectEarningsCallFromIrFeedAsync(ticker, cutoff, cancellationToken);
        callDate ??= await DetectEarningsCallFrom8KAsync(ticker, cutoff, cancellationToken);
        callDate ??= await DetectEarningsCallFromEconomicEventsAsync(ticker, cutoff, cancellationToken);

        if (callDate is null)
        {
            logger.LogDebug("No recent earnings call detected for {Symbol}", ticker.Symbol);
            return new EarningsCalendarTickerResult(true, false, false, false);
        }

        var alreadyQueued = await db.Transcripts
            .AnyAsync(t => t.Symbol == ticker.Symbol
                        && t.CallDate.HasValue
                        && t.CallDate.Value.Date == callDate.Value.Date, cancellationToken);

        var alreadyArticled = await db.Articles
            .AnyAsync(a => a.Source == SourceNames.EarningsCall
                        && a.Symbol == ticker.Symbol
                        && a.PublishedAt.Date == callDate.Value.Date, cancellationToken);

        if (alreadyQueued || alreadyArticled)
        {
            logger.LogDebug("Earnings call for {Symbol} on {Date} already tracked, skipping",
                ticker.Symbol, callDate.Value.Date);
            return new EarningsCalendarTickerResult(true, false, false, true);
        }

        var audioUrl = await FindAudioUrlAsync(http, ticker, callDate.Value, cancellationToken);
        if (audioUrl is not null)
        {
            await EnqueueTranscriptAsync(ticker, callDate.Value, audioUrl, cancellationToken);
            logger.LogInformation("Auto-queued transcript for {Symbol} call on {Date} ({Url})",
                ticker.Symbol, callDate.Value.Date, audioUrl);
            return new EarningsCalendarTickerResult(true, true, false, true);
        }

        await CreateManualActionArticleAsync(ticker, callDate.Value, cancellationToken);
        logger.LogInformation("Created manual-action article for {Symbol} call on {Date}; no audio URL found",
            ticker.Symbol, callDate.Value.Date);
        return new EarningsCalendarTickerResult(true, false, true, true);
    }

    private async Task<DateTime?> DetectEarningsCallFromIrFeedAsync(
        WatchedTicker ticker,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var recentIr = await db.Articles
            .AsNoTracking()
            .Where(a => a.Source == SourceNames.IrFeed
                     && a.Symbol == ticker.Symbol
                     && a.PublishedAt >= cutoff)
            .OrderByDescending(a => a.PublishedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var article in recentIr)
        {
            if (IsEarningsHeadline(article.Headline))
                return article.PublishedAt.Date;
        }

        return null;
    }

    private async Task<DateTime?> DetectEarningsCallFrom8KAsync(
        WatchedTicker ticker,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var recent8K = await db.Articles
            .AsNoTracking()
            .Where(a => a.Source == SourceNames.Edgar
                     && a.Symbol == ticker.Symbol
                     && a.PublishedAt >= cutoff)
            .OrderByDescending(a => a.PublishedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var article in recent8K)
        {
            if (article.RawPayload is not { Length: > 2 }) continue;
            try
            {
                using var doc = JsonDocument.Parse(article.RawPayload);
                var form = doc.RootElement.TryGetProperty("form", out var f) ? f.GetString() : null;
                if (form != "8-K") continue;
                var items = doc.RootElement.TryGetProperty("items", out var it) ? it.GetString() : null;
                if (items is null || !items.Contains("2.02")) continue;
                return article.PublishedAt.Date;
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private async Task<DateTime?> DetectEarningsCallFromEconomicEventsAsync(
        WatchedTicker ticker,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var ev = await db.EconomicEvents
            .AsNoTracking()
            .Where(e => e.EventType == "earnings"
                     && e.Symbol == ticker.Symbol
                     && e.ScheduledAt >= cutoff
                     && e.ScheduledAt <= DateTime.UtcNow.AddHours(1))
            .OrderByDescending(e => e.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);

        return ev?.ScheduledAt.Date;
    }

    private static bool IsEarningsHeadline(string headline)
    {
        var lower = headline.ToLowerInvariant();
        return EarningsKeywords.Any(k => lower.Contains(k));
    }

    private async Task<string?> FindAudioUrlAsync(
        HttpClient http,
        WatchedTicker ticker,
        DateTime callDate,
        CancellationToken cancellationToken)
    {
        var discovered = await audioDiscovery.DiscoverAsync(ticker.Symbol, callDate, cancellationToken);
        if (discovered is not null)
            return discovered;

        var windowStart = callDate.Date.AddDays(-1);
        var windowEnd = callDate.Date.AddDays(2);

        var irArticles = await db.Articles
            .AsNoTracking()
            .Where(a => a.Source == SourceNames.IrFeed
                     && a.Symbol == ticker.Symbol
                     && a.PublishedAt >= windowStart
                     && a.PublishedAt <= windowEnd)
            .OrderByDescending(a => a.PublishedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var article in irArticles)
        {
            var candidateUrl = article.Url ?? string.Empty;

            if (IsDirectAudioUrl(candidateUrl))
                return candidateUrl;

            if (IsWebcastUrl(candidateUrl))
                return candidateUrl;
        }

        if (!string.IsNullOrWhiteSpace(ticker.IrFeedUrl))
        {
            var audioFromFeed = await TryExtractAudioEnclosureAsync(
                http, ticker.IrFeedUrl, ticker.Symbol, callDate, cancellationToken);
            if (audioFromFeed is not null)
                return audioFromFeed;
        }

        return null;
    }

    private static bool IsDirectAudioUrl(string url) =>
        AudioExtension.IsMatch(url);

    private static bool IsWebcastUrl(string url) =>
        WebcastVendorPatterns.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase));

    private async Task<string?> TryExtractAudioEnclosureAsync(
        HttpClient http,
        string feedUrl,
        string symbol,
        DateTime callDate,
        CancellationToken cancellationToken)
    {
        try
        {
            var xml = await http.GetStringAsync(feedUrl, cancellationToken);
            var doc = XDocument.Parse(xml);
            var windowStart = callDate.Date.AddDays(-1);
            var windowEnd = callDate.Date.AddDays(2);

            foreach (var item in doc.Descendants("item"))
            {
                var pubDateStr = (string?)item.Element("pubDate");
                if (!string.IsNullOrWhiteSpace(pubDateStr) &&
                    DateTimeOffset.TryParse(pubDateStr, out var pubDate))
                {
                    if (pubDate.UtcDateTime < windowStart || pubDate.UtcDateTime > windowEnd)
                        continue;
                }

                var enclosure = item.Element("enclosure");
                if (enclosure is not null)
                {
                    var encUrl = enclosure.Attribute("url")?.Value ?? string.Empty;
                    var encType = enclosure.Attribute("type")?.Value ?? string.Empty;
                    if (IsDirectAudioUrl(encUrl) ||
                        encType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        return encUrl;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IR feed enclosure check failed for {Symbol}", symbol);
            return null;
        }
    }

    private async Task EnqueueTranscriptAsync(
        WatchedTicker ticker,
        DateTime callDate,
        string audioUrl,
        CancellationToken cancellationToken)
    {
        var transcriptId = Guid.NewGuid();
        var callDateUtc = DateTime.SpecifyKind(callDate.Date, DateTimeKind.Utc);
        var (tier, _) = SourceReputation.For(SourceNames.Transcript);

        var article = new Article
        {
            Id = Guid.NewGuid(),
            Source = SourceNames.Transcript,
            SourceId = transcriptId.ToString(),
            SourceTier = tier,
            Symbol = ticker.Symbol,
            Headline = $"{ticker.Symbol} earnings call ({callDateUtc:yyyy-MM-dd})",
            Summary = null,
            Url = audioUrl,
            Publisher = null,
            PublishedAt = callDateUtc,
            IngestedAt = DateTime.UtcNow,
            RawPayload = "{}",
        };

        var transcript = new Transcript
        {
            Id = transcriptId,
            Symbol = ticker.Symbol,
            CallType = "earnings",
            CallDate = callDateUtc,
            AudioUrl = audioUrl,
            Status = TranscriptStatus.Queued,
            IngestedAt = DateTime.UtcNow,
            ArticleId = article.Id,
        };

        db.Articles.Add(article);
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CreateManualActionArticleAsync(
        WatchedTicker ticker,
        DateTime callDate,
        CancellationToken cancellationToken)
    {
        var callDateUtc = DateTime.SpecifyKind(callDate.Date, DateTimeKind.Utc);
        var (tier, _) = SourceReputation.For(SourceNames.EarningsCall);

        db.Articles.Add(new Article
        {
            Id = Guid.NewGuid(),
            Source = SourceNames.EarningsCall,
            SourceId = $"{ticker.Symbol}:{callDateUtc:yyyyMMdd}",
            SourceTier = tier,
            Symbol = ticker.Symbol,
            Headline = $"{ticker.Symbol} earnings call held {callDateUtc:yyyy-MM-dd} - replay URL needed",
            Summary = null,
            Url = null,
            Publisher = null,
            PublishedAt = callDateUtc,
            IngestedAt = DateTime.UtcNow,
            RawPayload = "{}",
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static EarningsCalendarTickerPayload ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new EarningsCalendarTickerPayload();

        try
        {
            return JsonSerializer.Deserialize<EarningsCalendarTickerPayload>(payloadJson, JsonOptions)
                ?? new EarningsCalendarTickerPayload();
        }
        catch (JsonException)
        {
            return new EarningsCalendarTickerPayload();
        }
    }

    private sealed class EarningsCalendarTickerPayload
    {
        public Guid AssetId { get; set; }
        public string? Symbol { get; set; }
        public string? Name { get; set; }
        public string? Cik { get; set; }
        public string? IrFeedUrl { get; set; }
        public List<string>? Aliases { get; set; }
        public int LookbackHours { get; set; } = 120;
    }
}
