using System.Text.Json;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Core.Models;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MarketLens.Api.HostedServices;

public class ThesisBootstrapOptions
{
    public int CorpusArticleSampleSize { get; set; } = 600;
    public int CorpusClusterTake { get; set; } = 40;
    public int CorpusLookbackDays { get; set; } = 60;
    public decimal MinSimilarity { get; set; } = 0.25m;
    public decimal SubTrackSimilarityThreshold { get; set; } = 0.72m;
}

public sealed record ThesisBootstrapResult(
    Guid ThesisId,
    bool PlanGenerated,
    int CorpusContextSize,
    int SubTracksCreated,
    string? Error);

public class ThesisBootstrapper(
    MarketLensDbContext db,
    IThesisPlanner planner,
    IEmbeddingClient embedder,
    Microsoft.Extensions.Options.IOptions<ThesisBootstrapOptions> options,
    ILogger<ThesisBootstrapper> logger)
{
    private readonly ThesisBootstrapOptions _options = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public async Task<ThesisBootstrapResult> BootstrapAsync(Guid thesisId, CancellationToken cancellationToken = default)
    {
        var thesis = await db.ResearchTheses
            .Include(t => t.Rules)
            .FirstOrDefaultAsync(t => t.Id == thesisId, cancellationToken);
        if (thesis is null)
            return new ThesisBootstrapResult(thesisId, false, 0, 0, "thesis not found");

        if (thesis.Embedding is null)
        {
            try
            {
                thesis.Embedding = new Vector(await embedder.EmbedAsync(thesis.ThesisText, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to embed thesis {ThesisId} during bootstrap", thesisId);
                return new ThesisBootstrapResult(thesisId, false, 0, 0, $"embedding failed: {ex.Message}");
            }
        }

        var corpus = await BuildCorpusAsync(thesis.Embedding!, cancellationToken);

        ThesisPlanResult planResult;
        try
        {
            var context = new ThesisPlanContext(thesis.Name, thesis.ThesisText, corpus);
            planResult = await planner.PlanAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Thesis planner call failed for {ThesisId}", thesisId);
            return new ThesisBootstrapResult(thesisId, false, corpus.Count, 0, $"planner failed: {ex.Message}");
        }

        thesis.Plan = JsonSerializer.Serialize(planResult.Plan, Json);
        thesis.PlanModel = planResult.ModelName;
        thesis.PlanPromptVersion = planResult.PromptVersion;
        thesis.PlanGeneratedAt = DateTime.UtcNow;
        thesis.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(thesis.Summary))
            thesis.Summary = planResult.Plan.Summary;

        var generatedRules = ReplaceGeneratedRules(thesis, planResult.Plan);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bootstrapped thesis {ThesisId} with {SubTracks} sub-tracks against {CorpusSize} corpus clusters",
            thesisId, generatedRules, corpus.Count);

        return new ThesisBootstrapResult(thesisId, true, corpus.Count, generatedRules, null);
    }

    private async Task<IReadOnlyList<ThesisPlanDigestCluster>> BuildCorpusAsync(
        Vector embedding,
        CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-_options.CorpusLookbackDays);
        var maxDistance = (double)(1m - _options.MinSimilarity);

        var rows = await db.Articles
            .AsNoTracking()
            .Where(a => a.Embedding != null
                && a.ClusterId != null
                && a.IngestedAt >= since)
            .Select(a => new
            {
                a.ClusterId,
                Distance = a.Embedding!.CosineDistance(embedding),
            })
            .OrderBy(x => x.Distance)
            .Take(_options.CorpusArticleSampleSize)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0) return [];

        var bestByCluster = new Dictionary<Guid, double>();
        foreach (var row in rows)
        {
            if (row.ClusterId is null) continue;
            if (row.Distance > maxDistance) continue;
            if (!bestByCluster.TryGetValue(row.ClusterId.Value, out var existing) || row.Distance < existing)
                bestByCluster[row.ClusterId.Value] = row.Distance;
        }

        var clusterIds = bestByCluster
            .OrderBy(kv => kv.Value)
            .Take(_options.CorpusClusterTake)
            .Select(kv => kv.Key)
            .ToList();
        if (clusterIds.Count == 0) return [];

        var clusters = await db.Clusters
            .AsNoTracking()
            .Include(c => c.Articles.OrderBy(a => a.SourceTier).Take(1))
            .Include(c => c.Event)
            .Where(c => clusterIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        return clusterIds
            .Select(id =>
            {
                var c = clusters.FirstOrDefault(x => x.Id == id);
                if (c is null) return null;
                var rep = c.Articles.OrderBy(a => TierRank(a.SourceTier)).FirstOrDefault();
                var sim = (decimal)Math.Max(0d, 1d - bestByCluster[id]);
                return new ThesisPlanDigestCluster(
                    ClusterId: c.Id,
                    Symbol: c.Symbol,
                    Headline: rep?.Headline ?? c.Event?.Summary ?? "(no headline)",
                    Summary: c.Event?.Summary ?? rep?.Summary,
                    SourceTier: c.DominantSourceTier,
                    EventType: c.Event?.EventType ?? c.TriageEventType,
                    Importance: c.Event?.Importance,
                    Sentiment: c.Event?.Sentiment,
                    LastSeenAt: c.LastSeenAt,
                    Similarity: Math.Round(sim, 3));
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
    }

    private int ReplaceGeneratedRules(ResearchThesis thesis, ThesisPlan plan)
    {
        var generatedPrefix = "[plan] ";
        var now = DateTime.UtcNow;
        var existingByName = thesis.Rules
            .Where(r => r.Name.StartsWith(generatedPrefix, StringComparison.Ordinal))
            .ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
        var planRuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var processed = 0;
        foreach (var sub in plan.SubTracks)
        {
            var name = $"{generatedPrefix}{sub.Name}";
            planRuleNames.Add(name);

            if (existingByName.TryGetValue(name, out var existing))
            {
                existing.IsEnabled = true;
                existing.AssetKeywords = SerializeTerms(sub.AssetTerms);
                existing.ConceptKeywords = SerializeTerms(sub.ConceptTerms);
                existing.EventTypes = SerializeTerms(sub.EventTypes);
                existing.ExcludeTerms = SerializeTerms(sub.ExcludeTerms);
                existing.MinArticleSimilarity = _options.SubTrackSimilarityThreshold;
                existing.UpdatedAt = now;
            }
            else
            {
                db.ThesisRules.Add(new ThesisRule
                {
                    Id = Guid.NewGuid(),
                    ThesisId = thesis.Id,
                    Name = name,
                    IsEnabled = true,
                    AssetKeywords = SerializeTerms(sub.AssetTerms),
                    ConceptKeywords = SerializeTerms(sub.ConceptTerms),
                    EventTypes = SerializeTerms(sub.EventTypes),
                    SourceNames = "[]",
                    SourceTiers = "[]",
                    ExcludeTerms = SerializeTerms(sub.ExcludeTerms),
                    MinArticleSimilarity = _options.SubTrackSimilarityThreshold,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            processed++;
        }

        // Disable stale [plan] rules rather than deleting them. Deleting fires the
        // evidence FK's ON DELETE SET NULL, which collides with the NULLS-NOT-DISTINCT
        // unique index on research_evidence (ThesisId, ThesisRuleId, ArticleId/ClusterId)
        // whenever a ruleless binding already exists for the same thesis+article — that
        // crashed plan refresh and re-ran the LLM on every retry. Disabling stops the
        // matcher from using the sub-track while keeping its accumulated evidence intact.
        foreach (var (name, rule) in existingByName)
        {
            if (!planRuleNames.Contains(name) && rule.IsEnabled)
            {
                rule.IsEnabled = false;
                rule.UpdatedAt = now;
            }
        }

        return processed;
    }

    private static string SerializeTerms(IReadOnlyList<string> terms)
    {
        var deduped = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return JsonSerializer.Serialize(deduped);
    }

    private static int TierRank(string tier) => tier switch
    {
        "primary" => 0,
        "wire" => 1,
        "ir_feed" => 1,
        "trade_press" => 2,
        "aggregator" => 3,
        "opinion" => 4,
        _ => 5,
    };
}
