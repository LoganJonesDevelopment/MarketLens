using System.Net;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketLens.Infrastructure.Tests;

public class RssSourceTests
{
    [Fact]
    public async Task FetchAsync_MarksThinItemsForLaterBodyFetchWithoutFetchingBody()
    {
        const string feedUrl = "https://feeds.example.test/rss";
        const string articleUrl = "https://news.example.test/article";

        var xml = $"""
            <rss version="2.0">
              <channel>
                <item>
                  <title>Example earnings release</title>
                  <link>{articleUrl}</link>
                  <guid>example-1</guid>
                  <description>Short summary.</description>
                  <pubDate>{DateTimeOffset.UtcNow:R}</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var handler = new RecordingHandler(feedUrl, xml);
        var source = new RssSource(
            new HttpClient(handler),
            new EmptyWatchlistProvider(),
            NullLogger<RssSource>.Instance,
            new RssFeedConfig
            {
                Source = "test_source",
                Url = feedUrl,
                FetchBody = true,
                FetchDelayMs = 250,
            });

        var items = await source.FetchAsync();

        var item = Assert.Single(items);
        Assert.True(item.NeedsBodyFetch);
        Assert.Equal(250, item.BodyFetchDelayMs);
        Assert.Equal([feedUrl], handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_AnnotatesGenericFeedItemsWhenWatchlistMatches()
    {
        const string feedUrl = "https://feeds.example.test/rss";

        var xml = $"""
            <rss version="2.0">
              <channel>
                <item>
                  <title>Cisco raises guidance after AI order growth</title>
                  <link>https://news.example.test/cisco</link>
                  <guid>example-csco</guid>
                  <description>Cisco Systems highlighted demand for networking gear.</description>
                  <pubDate>{DateTimeOffset.UtcNow:R}</pubDate>
                </item>
                <item>
                  <title>Retail sales report comes in flat</title>
                  <link>https://news.example.test/macro</link>
                  <guid>example-macro</guid>
                  <description>No company ticker in this item.</description>
                  <pubDate>{DateTimeOffset.UtcNow:R}</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var source = new RssSource(
            new HttpClient(new RecordingHandler(feedUrl, xml)),
            new StaticWatchlistProvider([
                new WatchedTicker(Guid.NewGuid(), "CSCO", "Cisco Systems", "0000858877", null, ["Cisco", "Cisco Systems"]),
            ]),
            NullLogger<RssSource>.Instance,
            new RssFeedConfig
            {
                Source = "test_source",
                Url = feedUrl,
                FilterByWatchlist = false,
                LookbackDays = 1,
            });

        var items = await source.FetchAsync();

        Assert.Equal(2, items.Count);
        Assert.Equal("CSCO", items.Single(i => i.Headline.Contains("Cisco", StringComparison.OrdinalIgnoreCase)).Symbol);
        Assert.Null(items.Single(i => i.Headline.Contains("Retail sales", StringComparison.OrdinalIgnoreCase)).Symbol);
    }

    [Fact]
    public async Task FetchAsync_FilterByWatchlistStillDropsUnmatchedItems()
    {
        const string feedUrl = "https://feeds.example.test/rss";

        var xml = $"""
            <rss version="2.0">
              <channel>
                <item>
                  <title>Cisco raises guidance after AI order growth</title>
                  <link>https://news.example.test/cisco</link>
                  <guid>example-csco</guid>
                  <description>Cisco Systems highlighted demand for networking gear.</description>
                  <pubDate>{DateTimeOffset.UtcNow:R}</pubDate>
                </item>
                <item>
                  <title>Retail sales report comes in flat</title>
                  <link>https://news.example.test/macro</link>
                  <guid>example-macro</guid>
                  <description>No company ticker in this item.</description>
                  <pubDate>{DateTimeOffset.UtcNow:R}</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var source = new RssSource(
            new HttpClient(new RecordingHandler(feedUrl, xml)),
            new StaticWatchlistProvider([
                new WatchedTicker(Guid.NewGuid(), "CSCO", "Cisco Systems", "0000858877", null, ["Cisco", "Cisco Systems"]),
            ]),
            NullLogger<RssSource>.Instance,
            new RssFeedConfig
            {
                Source = "test_source",
                Url = feedUrl,
                FilterByWatchlist = true,
                LookbackDays = 1,
            });

        var item = Assert.Single(await source.FetchAsync());
        Assert.Equal("CSCO", item.Symbol);
    }

    private sealed class EmptyWatchlistProvider : IWatchlistProvider
    {
        public Task<IReadOnlyList<WatchedTicker>> GetWatchedTickersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WatchedTicker>>([]);
    }

    private sealed class StaticWatchlistProvider(IReadOnlyList<WatchedTicker> watched) : IWatchlistProvider
    {
        public Task<IReadOnlyList<WatchedTicker>> GetWatchedTickersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(watched);
    }

    private sealed class RecordingHandler(string feedUrl, string xml) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            Requests.Add(url);

            if (!string.Equals(url, feedUrl, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected request to {url}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml),
            });
        }
    }
}
