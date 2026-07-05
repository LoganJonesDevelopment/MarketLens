using MarketLens.Core.Domain;
using MarketLens.Core.Entities;

namespace MarketLens.Infrastructure.Services;

public class ImportanceCalculator
{
    public (decimal Importance, decimal SourceWeight, decimal NoveltyWeight, decimal EventClassPrior) Compute(
        Cluster cluster,
        string eventType,
        decimal magnitudeSignal)
    {
        var sourceWeight = cluster.TopSourceWeight;
        var novelty = NoveltyFromClusterAge(cluster);
        var prior = EventClassPriors.For(eventType);
        var corroboration = CorroborationFromClusterSize(cluster.MemberCount);

        var importance = sourceWeight * novelty * prior * (0.5m + 0.5m * magnitudeSignal) * corroboration;
        return (Math.Clamp(importance, 0m, 1m), sourceWeight, novelty, prior);
    }

    private static decimal NoveltyFromClusterAge(Cluster cluster)
    {
        var earliestPublished = cluster.Articles.Count > 0
            ? cluster.Articles.Min(a => a.PublishedAt)
            : cluster.FirstSeenAt;
        var age = DateTime.UtcNow - earliestPublished;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalHours < 1) return 1.00m;
        if (age.TotalHours < 6) return 0.90m;
        if (age.TotalHours < 24) return 0.75m;
        if (age.TotalDays < 3) return 0.55m;
        if (age.TotalDays < 7) return 0.35m;
        if (age.TotalDays < 30) return 0.20m;
        return 0.05m;
    }

    private static decimal CorroborationFromClusterSize(int memberCount)
    {
        if (memberCount <= 1) return 1.00m;
        if (memberCount == 2) return 1.08m;
        if (memberCount == 3) return 1.15m;
        return 1.25m;
    }
}
