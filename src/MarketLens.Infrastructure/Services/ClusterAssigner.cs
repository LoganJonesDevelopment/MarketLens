using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MarketLens.Infrastructure.Services;

public class ClusterOptions
{
    public double SimilarityThreshold { get; set; } = 0.85;
    public int LookbackDays { get; set; } = 7;
}

public class ClusterAssigner(MarketLensDbContext db, Microsoft.Extensions.Options.IOptions<ClusterOptions> options)
{
    private readonly ClusterOptions _options = options.Value;

    public async Task<Cluster> AssignAsync(Article article, CancellationToken cancellationToken = default)
    {
        if (article.Embedding is null)
        {
            throw new InvalidOperationException($"Article {article.Id} has no embedding; cannot cluster");
        }

        var since = DateTime.UtcNow.AddDays(-_options.LookbackDays);
        var topicMatch = await FindCoarseTopicClusterAsync(article, since, cancellationToken);
        if (topicMatch is not null)
        {
            Attach(article, topicMatch);
            return topicMatch;
        }

        var maxDistance = 1.0 - _options.SimilarityThreshold;

        var nearest = await db.Articles
            .Where(a => a.PublishedAt >= since
                && a.Embedding != null
                && a.ClusterId != null
                && (article.Symbol == null ? a.Symbol == null : a.Symbol == article.Symbol))
            .Select(a => new
            {
                a.ClusterId,
                Distance = a.Embedding!.CosineDistance(article.Embedding!),
            })
            .OrderBy(x => x.Distance)
            .FirstOrDefaultAsync(cancellationToken);

        Cluster cluster;
        if (nearest is not null && nearest.Distance <= maxDistance)
        {
            cluster = await db.Clusters.FirstAsync(c => c.Id == nearest.ClusterId, cancellationToken);
            Attach(article, cluster);
        }
        else
        {
            var (tier, weight) = SourceReputation.For(article.Source);
            var seenAt = article.PublishedAt > DateTime.UtcNow ? DateTime.UtcNow : article.PublishedAt;
            cluster = new Cluster
            {
                Id = Guid.NewGuid(),
                Symbol = article.Symbol,
                FirstSeenAt = seenAt,
                LastSeenAt = seenAt,
                MemberCount = 1,
                DominantSourceTier = tier,
                TopSourceWeight = weight,
            };
            db.Clusters.Add(cluster);
        }

        article.Cluster = cluster;
        article.ClusterId = cluster.Id;
        return cluster;
    }

    private async Task<Cluster?> FindCoarseTopicClusterAsync(
        Article article,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var topic = EventFingerprints.CoarseTopic(article.Symbol, article.Headline, article.Summary);
        if (topic is null) return null;

        var windowStart = article.PublishedAt.AddDays(-4);
        if (windowStart < since) windowStart = since;
        var windowEnd = article.PublishedAt.AddDays(4);

        var candidates = await db.Articles
            .Include(a => a.Cluster)
            .Where(a => a.ClusterId != null
                && a.Symbol == article.Symbol
                && a.PublishedAt >= windowStart
                && a.PublishedAt <= windowEnd)
            .OrderByDescending(a => a.PublishedAt)
            .Take(250)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => EventFingerprints.CoarseTopic(a.Symbol, a.Headline, a.Summary) == topic)
            .OrderByDescending(a => a.SourceTier == SourceTiers.Primary)
            .ThenByDescending(a => a.PublishedAt)
            .Select(a => a.Cluster)
            .FirstOrDefault(c => c is not null);
    }

    private static void Attach(Article article, Cluster cluster)
    {
        cluster.MemberCount += 1;
        var seenAt = article.PublishedAt > DateTime.UtcNow ? DateTime.UtcNow : article.PublishedAt;
        cluster.LastSeenAt = seenAt > cluster.LastSeenAt ? seenAt : cluster.LastSeenAt;

        var (tier, weight) = SourceReputation.For(article.Source);
        if (weight > cluster.TopSourceWeight)
        {
            cluster.TopSourceWeight = weight;
            cluster.DominantSourceTier = tier;
        }

        article.Cluster = cluster;
        article.ClusterId = cluster.Id;
    }
}
