using System.Text.Json;
using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MarketLens.Api.Services.Pipeline;

public sealed record NewsSourcePollTarget(string Name, int FirstIndex)
{
    public static IReadOnlyList<NewsSourcePollTarget> CreateTargets(IReadOnlyList<INewsSource> sources)
    {
        var targets = new List<NewsSourcePollTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < sources.Count; i++)
        {
            var name = sources[i].Name.Trim();
            if (seen.Add(name))
                targets.Add(new NewsSourcePollTarget(name, i));
        }

        return targets;
    }
}

public sealed record NewsSourcePollResult(
    bool SourceFound,
    int ArticlesFetched,
    int ArticlesQueued,
    int ArticlesSkipped)
{
    public static NewsSourcePollResult MissingSource { get; } = new(false, 0, 0, 0);
}

public sealed class NewsSourcePollHandler(
    IEnumerable<INewsSource> sources,
    MarketLensDbContext db,
    ILocalWorkQueue queue,
    ISourceCursorStore cursorStore,
    IOptions<IngestionOptions> options,
    ILogger<NewsSourcePollHandler> logger)
{
    public const string CursorSourceName = "news_ingestion";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IngestionOptions _options = options.Value;

    public async Task<NewsSourcePollResult> ProcessAsync(
        string sourceName,
        CancellationToken cancellationToken)
    {
        var normalizedSourceName = sourceName.Trim();
        var matchingSources = sources
            .Where(s => string.Equals(s.Name, normalizedSourceName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingSources.Count == 0)
        {
            logger.LogWarning("Source poll skipped because source {Source} is no longer registered", normalizedSourceName);
            return NewsSourcePollResult.MissingSource;
        }

        await cursorStore.MarkStartedAsync(
            CursorSourceName,
            normalizedSourceName,
            NextEligibleRunAt(),
            cancellationToken);

        try
        {
            return await FetchAndQueueAsync(normalizedSourceName, matchingSources, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await cursorStore.MarkFailedAsync(
                new SourceCursorFailure(
                    CursorSourceName,
                    normalizedSourceName,
                    ex.Message,
                    await FailureNextEligibleRunAtAsync(normalizedSourceName, cancellationToken)),
                cancellationToken);
            throw;
        }
    }

    private async Task<NewsSourcePollResult> FetchAndQueueAsync(
        string sourceName,
        IReadOnlyList<INewsSource> matchingSources,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching from {Source}", sourceName);
        var fetched = new List<IngestedArticle>();
        foreach (var source in matchingSources)
        {
            var sourceArticles = await source.FetchAsync(cancellationToken);
            fetched.AddRange(sourceArticles);
        }

        if (fetched.Count == 0)
        {
            logger.LogInformation("{Source} returned 0 articles", sourceName);
            await MarkSucceededAsync(sourceName, fetched, queued: 0, skipped: 0, cancellationToken);
            return new NewsSourcePollResult(true, 0, 0, 0);
        }

        var sourceIds = fetched.Select(a => a.SourceId).ToList();
        var existing = await db.Articles
            .Where(a => a.Source == sourceName && sourceIds.Contains(a.SourceId))
            .Select(a => a.SourceId)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet();

        var candidates = fetched
            .Where(a => !existingSet.Contains(a.SourceId))
            .GroupBy(a => a.SourceId)
            .Select(g => g.First())
            .ToList();

        var maxArticles = Math.Max(1, _options.MaxArticlesPerSourcePerCycle);
        var fresh = candidates.Take(maxArticles).ToList();
        var skipped = fetched.Count - fresh.Count;

        foreach (var article in fresh)
        {
            var payload = new ArticleBodyEnrichmentPayload(
                article,
                _options.BodyFetchDelayMs,
                _options.PerArticleDelayMs,
                _options.TriageThreshold);

            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.ArticleBodyEnrichment,
                    NaturalKey: NaturalKey(article),
                    PayloadJson: JsonSerializer.Serialize(payload, JsonOptions),
                    Priority: PriorityFor(article.Source, article.PublishedAt)),
                cancellationToken);
        }

        logger.LogInformation(
            "Queued {Count} fresh articles from {Source} for body enrichment/finalization",
            fresh.Count,
            sourceName);

        await MarkSucceededAsync(sourceName, fetched, fresh.Count, skipped, cancellationToken);
        return new NewsSourcePollResult(true, fetched.Count, fresh.Count, skipped);
    }

    private async Task MarkSucceededAsync(
        string sourceName,
        IReadOnlyList<IngestedArticle> fetched,
        int queued,
        int skipped,
        CancellationToken cancellationToken)
    {
        var latest = fetched
            .OrderByDescending(a => a.PublishedAt)
            .FirstOrDefault();

        await cursorStore.MarkSucceededAsync(
            new SourceCursorAdvance(
                CursorSourceName,
                sourceName,
                CursorJson: JsonSerializer.Serialize(new
                {
                    fetched = fetched.Count,
                    queued,
                    skipped,
                }, JsonOptions),
                LastItemTimestamp: latest is null
                    ? null
                    : DateTime.SpecifyKind(latest.PublishedAt, DateTimeKind.Utc),
                LastItemId: latest?.SourceId,
                NextEligibleRunAt: NextEligibleRunAt()),
            cancellationToken);
    }

    private DateTime NextEligibleRunAt()
        => DateTime.UtcNow.AddMinutes(Math.Max(1, _options.IntervalMinutes));

    private async Task<DateTime> FailureNextEligibleRunAtAsync(
        string sourceName,
        CancellationToken cancellationToken)
    {
        var state = await cursorStore.GetAsync(CursorSourceName, sourceName, cancellationToken);
        var failures = Math.Clamp((state?.ConsecutiveFailures ?? 0) + 1, 1, 6);
        var delayMinutes = Math.Min(360, Math.Max(1, _options.IntervalMinutes) * Math.Pow(2, failures - 1));
        return DateTime.UtcNow.AddMinutes(delayMinutes);
    }

    private static string NaturalKey(IngestedArticle article)
        => $"{article.Source}:{article.SourceId}";

    private const int WeightBucketSize = 4_000_000;

    private static int PriorityFor(string source, DateTime publishedAt)
    {
        var (_, weight) = SourceReputation.For(source);
        var weightBucket = (int)Math.Clamp((double)weight * 10d, 0d, 10d) * WeightBucketSize;
        var utc = publishedAt.Kind == DateTimeKind.Utc
            ? publishedAt
            : DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc);
        var minutes = (utc - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        var recency = (int)Math.Clamp(minutes, 0, WeightBucketSize - 1);
        return weightBucket + recency;
    }
}
