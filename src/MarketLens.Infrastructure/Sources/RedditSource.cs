using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using Microsoft.Extensions.Logging;

namespace MarketLens.Infrastructure.Sources;

public class RedditSourceConfig
{
    public IReadOnlyList<string> Subreddits { get; set; } = [];
    public int LookbackDays { get; set; } = 7;
    public int DelayBetweenSubredditsMs { get; set; } = 1500;
}

public class RedditSource(
    HttpClient httpClient,
    IWatchlistProvider watchlist,
    ILogger<RedditSource> logger,
    RedditSourceConfig config) : INewsSource
{
    public string Name => SourceNames.Reddit;

    public async Task<IReadOnlyList<IngestedArticle>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<IngestedArticle>();
        var watched = await watchlist.GetWatchedTickersAsync(cancellationToken);
        if (watched.Count == 0) return results;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-config.LookbackDays).ToUnixTimeSeconds();

        foreach (var subreddit in config.Subreddits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var url = $"https://www.reddit.com/r/{subreddit}/new.json?limit=100";
                var json = await httpClient.GetStringAsync(url, cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("data", out var data)) continue;
                if (!data.TryGetProperty("children", out var children)) continue;

                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetProperty("data", out var post)) continue;

                    var createdUtc = post.TryGetProperty("created_utc", out var cu) ? cu.GetDouble() : 0;
                    if (createdUtc < cutoff) continue;

                    var title = post.TryGetProperty("title", out var ti) ? ti.GetString() ?? string.Empty : string.Empty;
                    var selftext = post.TryGetProperty("selftext", out var st) ? st.GetString() ?? string.Empty : string.Empty;
                    if (selftext.Length > 2000) selftext = selftext[..2000];

                    var matched = WatchlistMatcher.MatchSymbol(watched, title, selftext);
                    if (matched is null) continue;

                    var id = post.TryGetProperty("id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var permalink = post.TryGetProperty("permalink", out var pl) ? pl.GetString() : null;
                    var score = post.TryGetProperty("score", out var sc) ? sc.GetInt32() : 0;
                    var numComments = post.TryGetProperty("num_comments", out var nc) ? nc.GetInt32() : 0;

                    var publishedAt = DateTimeOffset.FromUnixTimeSeconds((long)createdUtc).UtcDateTime;
                    var summary = string.IsNullOrWhiteSpace(selftext) ? null : selftext;
                    var fullUrl = permalink is not null ? $"https://www.reddit.com{permalink}" : null;

                    results.Add(new IngestedArticle(
                        Source: SourceNames.Reddit,
                        SourceId: StableId(id),
                        Symbol: matched,
                        Headline: title,
                        Summary: summary,
                        Url: fullUrl,
                        Publisher: $"r/{subreddit}",
                        PublishedAt: publishedAt,
                        RawJson: BuildRawJson(id, title, selftext, subreddit, score, numComments, permalink)));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reddit fetch failed for r/{Subreddit}", subreddit);
            }

            if (config.DelayBetweenSubredditsMs > 0)
                await Task.Delay(config.DelayBetweenSubredditsMs, cancellationToken);
        }

        return results;
    }

    private static string StableId(string postId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"reddit:{postId}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildRawJson(string id, string title, string selftext, string subreddit, int score, int numComments, string? permalink)
    {
        return JsonSerializer.Serialize(new
        {
            id,
            title,
            selftext,
            subreddit,
            score,
            num_comments = numComments,
            permalink
        });
    }
}
