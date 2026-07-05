using System.Xml.Linq;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class RssFeedConfig
{
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public bool FilterByWatchlist { get; set; }
    public int LookbackDays { get; set; } = 7;
    public bool FetchBody { get; set; } = false;
    public int FetchDelayMs { get; set; } = 1500;
}

public class RssSource(
    HttpClient httpClient,
    IWatchlistProvider watchlist,
    ILogger<RssSource> logger,
    RssFeedConfig config) : INewsSource
{
    public string Name => config.Source;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        IReadOnlyList<WatchedTicker> watched = [];
        if (config.FilterByWatchlist || string.IsNullOrWhiteSpace(config.Symbol))
            watched = await watchlist.GetWatchedTickersAsync(cancellationToken);

        try
        {
            var xml = await httpClient.GetStringAsync(config.Url, cancellationToken);
            var doc = XDocument.Parse(xml);
            var items = RssParsing.ParseItems(doc, config).ToList();
            var cutoff = DateTime.UtcNow.AddDays(-config.LookbackDays);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.PublishedAt < cutoff) continue;

                IngestedArticle candidate;
                var matchedSymbol = watched.Count == 0
                    ? null
                    : WatchlistMatcher.MatchSymbol(watched, item.Headline, item.Summary);

                if (config.FilterByWatchlist)
                {
                    if (matchedSymbol is null) continue;
                    candidate = item with { Symbol = matchedSymbol };
                }
                else
                {
                    if (config.Source == SourceNames.IrFeed &&
                        !MarketMateriality.IsCompanyFeedMaterial(item.Headline, item.Summary))
                    {
                        continue;
                    }
                    candidate = item with { Symbol = item.Symbol ?? config.Symbol ?? matchedSymbol };
                }

                if (config.FetchBody && IsThinSummary(candidate.Summary) && !string.IsNullOrWhiteSpace(candidate.Url))
                    candidate = candidate with { NeedsBodyFetch = true, BodyFetchDelayMs = config.FetchDelayMs };

                results.Add(candidate);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RSS fetch failed for {Source} {Url}", config.Source, config.Url);
        }
        return results;
    }

    private static bool IsThinSummary(string? summary) =>
        string.IsNullOrWhiteSpace(summary) || summary.Trim().Length < 200;
}
