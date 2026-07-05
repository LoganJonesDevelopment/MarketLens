using System.Text.Json;
using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace MarketLens.Api.HostedServices;

public class ResearchMatcherOptions
{
    public int InitialDelaySeconds { get; set; } = 45;
    public int IntervalSeconds { get; set; } = 60;
    public int IdleIntervalSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 200;
    public int WorkBatchSize { get; set; } = 5;
    public int EnqueueBatchSize { get; set; } = 25;
    public int QueueBacklogLimit { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 20;
    public int ReenqueueCooldownMinutes { get; set; } = 60;
    public int LookbackHours { get; set; } = 48;
    public decimal DefaultSimilarityThreshold { get; set; } = 0.74m;
    public decimal DefaultSegmentSimilarityThreshold { get; set; } = 0.58m;
}

public sealed record ResearchScanRequest(
    Guid? ThesisId = null,
    bool ActiveOnly = true,
    int? LookbackHours = null,
    int? BatchSize = null);

public sealed record ResearchScanResult(
    int ThesesScanned,
    int ArticlesScanned,
    int EventsScanned,
    int SegmentsScanned,
    int ChunksScanned,
    int EvidenceAdded);

public class ResearchMatcher(
    MarketLensDbContext db,
    IEmbeddingClient embedder,
    IOptions<ResearchMatcherOptions> options,
    ILogger<ResearchMatcher> logger)
{
    private readonly ResearchMatcherOptions _options = options.Value;

    public async Task<ResearchScanResult> ScanAsync(
        ResearchScanRequest request,
        CancellationToken cancellationToken = default)
    {
        var thesisQuery = db.ResearchTheses
            .Include(t => t.Rules)
            .Include(t => t.ThesisAssets)
            .ThenInclude(ta => ta.Asset)
            .AsQueryable();

        if (request.ThesisId.HasValue)
            thesisQuery = thesisQuery.Where(t => t.Id == request.ThesisId.Value);
        if (request.ActiveOnly)
            thesisQuery = thesisQuery.Where(t => t.Status == ThesisStatuses.Active);

        var allTheses = await thesisQuery.ToListAsync(cancellationToken);
        var theses = allTheses.Where(t => t.Rules.Any(r => r.IsEnabled)).ToList();
        if (allTheses.Count == 0) return new ResearchScanResult(0, 0, 0, 0, 0, 0);

        foreach (var thesis in allTheses.Where(t => t.Embedding is null))
        {
            try
            {
                thesis.Embedding = new Vector(await embedder.EmbedAsync(thesis.ThesisText, cancellationToken));
                thesis.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Unable to embed thesis {ThesisId} during matcher run", thesis.Id);
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var thesesWithEmbedding = allTheses.Where(t => t.Embedding != null).ToList();

        var batchSize = Math.Clamp(request.BatchSize ?? _options.BatchSize, 1, 5000);
        var lookbackHours = request.LookbackHours ?? _options.LookbackHours;
        var hasCutoff = lookbackHours > 0;
        var cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, lookbackHours));

        var articleQuery = db.Articles
            .AsNoTracking()
            .Include(a => a.Cluster)
            .ThenInclude(c => c!.Event)
            .AsQueryable();
        if (hasCutoff)
            articleQuery = articleQuery.Where(a => a.IngestedAt >= cutoff);

        var articles = await articleQuery
            .OrderByDescending(a => a.IngestedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var allAssetTerms = theses
            .SelectMany(t =>
                t.Rules.Where(r => r.IsEnabled)
                    .SelectMany(r => ParseTerms(r.AssetKeywords))
                    .Concat(GetLinkedAssetTerms(t)))
            .Where(IsSafeBareAssetTerm)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allAssetTerms.Count > 0)
        {
            var batchArticleIds = articles.Select(a => a.Id).ToHashSet();
            var keywordArticleIds = await FindArticleIdsByKeywordsAsync(
                db, allAssetTerms, hasCutoff ? cutoff : null, batchSize, cancellationToken);

            var missingIds = keywordArticleIds.Where(id => !batchArticleIds.Contains(id)).ToList();
            if (missingIds.Count > 0)
            {
                var keywordArticles = await db.Articles
                    .AsNoTracking()
                    .Include(a => a.Cluster)
                    .ThenInclude(c => c!.Event)
                    .Where(a => missingIds.Contains(a.Id))
                    .ToListAsync(cancellationToken);

                foreach (var ka in keywordArticles)
                {
                    if (batchArticleIds.Add(ka.Id))
                        articles.Add(ka);
                }
            }
        }

        var eventQuery = db.Events
            .AsNoTracking()
            .Include(e => e.Cluster)
            .ThenInclude(c => c!.Articles)
            .AsQueryable();
        if (hasCutoff)
            eventQuery = eventQuery.Where(e => e.ExtractedAt >= cutoff);

        var events = await eventQuery
            .OrderByDescending(e => e.ExtractedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var thesisIds = theses.Select(t => t.Id).ToList();
        var articleIds = articles.Select(a => a.Id).ToList();
        var clusterIds = events.Select(e => e.ClusterId).ToList();

        var existingArticleLinks = await db.ResearchEvidence
            .AsNoTracking()
            .Where(e => e.ArticleId != null &&
                thesisIds.Contains(e.ThesisId) &&
                articleIds.Contains(e.ArticleId.Value))
            .Select(e => new { e.ThesisId, e.ThesisRuleId, ArticleId = e.ArticleId!.Value })
            .ToListAsync(cancellationToken);

        var existingEventLinks = await db.ResearchEvidence
            .AsNoTracking()
            .Where(e => e.ClusterId != null &&
                thesisIds.Contains(e.ThesisId) &&
                clusterIds.Contains(e.ClusterId.Value))
            .Select(e => new { e.ThesisId, e.ThesisRuleId, ClusterId = e.ClusterId!.Value })
            .ToListAsync(cancellationToken);

        var linkedArticles = existingArticleLinks
            .Select(e => (e.ThesisId, e.ThesisRuleId, e.ArticleId))
            .ToHashSet();
        var linkedEvents = existingEventLinks
            .Select(e => (e.ThesisId, e.ThesisRuleId, e.ClusterId))
            .ToHashSet();

        var added = 0;
        foreach (var thesis in theses)
        {
            var linkedAssetTerms = GetLinkedAssetTerms(thesis).ToList();
            foreach (var rule in thesis.Rules.Where(r => r.IsEnabled))
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var article in articles)
                {
                    if (linkedArticles.Contains((thesis.Id, (Guid?)rule.Id, article.Id))) continue;

                    var match = MatchArticle(thesis, rule, linkedAssetTerms, article);
                    if (match is null) continue;

                    db.ResearchEvidence.Add(new ResearchEvidence
                    {
                        Id = Guid.NewGuid(),
                        ThesisId = thesis.Id,
                        ThesisRuleId = rule.Id,
                        ArticleId = article.Id,
                        EvidenceType = "article",
                        MatchKind = "matcher",
                        MatchReason = match.Reason,
                        Similarity = match.Similarity,
                        MatchedAt = DateTime.UtcNow,
                    });
                    linkedArticles.Add((thesis.Id, (Guid?)rule.Id, article.Id));
                    added++;
                }

                foreach (var ev in events)
                {
                    if (linkedEvents.Contains((thesis.Id, (Guid?)rule.Id, ev.ClusterId))) continue;

                    var match = MatchEvent(thesis, rule, linkedAssetTerms, ev);
                    if (match is null) continue;

                    db.ResearchEvidence.Add(new ResearchEvidence
                    {
                        Id = Guid.NewGuid(),
                        ThesisId = thesis.Id,
                        ThesisRuleId = rule.Id,
                        ClusterId = ev.ClusterId,
                        EvidenceType = "event",
                        MatchKind = "matcher",
                        MatchReason = match.Reason,
                        Similarity = match.Similarity,
                        MatchedAt = DateTime.UtcNow,
                    });
                    linkedEvents.Add((thesis.Id, (Guid?)rule.Id, ev.ClusterId));
                    added++;
                }
            }
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        var segmentsScanned = await ScanSegmentsAsync(thesesWithEmbedding, request, cancellationToken);
        added += segmentsScanned.EvidenceAdded;

        var chunksScanned = await ScanChunksAsync(thesesWithEmbedding, request, cancellationToken);
        added += chunksScanned.EvidenceAdded;

        return new ResearchScanResult(theses.Count, articles.Count, events.Count, segmentsScanned.SegmentsScanned, chunksScanned.ChunksScanned, added);
    }

    private async Task<(int SegmentsScanned, int EvidenceAdded)> ScanSegmentsAsync(
        IReadOnlyList<ResearchThesis> theses,
        ResearchScanRequest request,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(request.BatchSize ?? _options.BatchSize, 1, 5000);
        var minMatchedAt = theses.Select(t => t.LastSegmentMatchedAt).Min();
        var cutoff = minMatchedAt ?? DateTime.UtcNow.AddDays(-30);

        var segments = await db.TranscriptSegments
            .AsNoTracking()
            .Include(s => s.Transcript)
            .Where(s =>
                s.Transcript!.Status == "completed" &&
                s.Embedding != null &&
                s.Transcript.CompletedAt >= cutoff)
            .OrderBy(s => s.Transcript!.CompletedAt)
            .ThenBy(s => s.SegmentIndex)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (segments.Count == 0) return (0, 0);

        if (segments.Count == batchSize)
        {
            // The watermark only advances past fully-scanned timestamps, so a
            // truncated batch must carry every segment sharing the boundary
            // timestamp or the rest of that transcript would be skipped forever.
            var boundary = segments[^1].Transcript!.CompletedAt;
            segments.RemoveAll(s => s.Transcript!.CompletedAt == boundary);
            segments.AddRange(await db.TranscriptSegments
                .AsNoTracking()
                .Include(s => s.Transcript)
                .Where(s =>
                    s.Transcript!.Status == "completed" &&
                    s.Embedding != null &&
                    s.Transcript.CompletedAt == boundary)
                .OrderBy(s => s.SegmentIndex)
                .ToListAsync(cancellationToken));
            logger.LogInformation(
                "Segment scan hit the {BatchSize}-row cap; scanning {Count} segments completed through {Boundary} and deferring newer segments to the next run",
                batchSize,
                segments.Count,
                boundary);
        }

        var thesisIds = theses.Select(t => t.Id).ToList();
        var segmentIds = segments.Select(s => s.Id).ToList();

        var existingLinks = await db.ResearchEvidence
            .AsNoTracking()
            .Where(e => e.TranscriptSegmentId != null &&
                thesisIds.Contains(e.ThesisId) &&
                segmentIds.Contains(e.TranscriptSegmentId!.Value))
            .Select(e => new { e.ThesisId, SegmentId = e.TranscriptSegmentId!.Value })
            .ToListAsync(cancellationToken);

        var linked = existingLinks
            .Select(e => (e.ThesisId, e.SegmentId))
            .ToHashSet();

        var added = 0;
        var now = DateTime.UtcNow;
        var maxCompleted = segments
            .Where(s => s.Transcript?.CompletedAt != null)
            .Select(s => s.Transcript!.CompletedAt!.Value)
            .DefaultIfEmpty()
            .Max();

        foreach (var thesis in theses)
        {
            var watermark = thesis.LastSegmentMatchedAt;
            var linkedAssetTerms = GetLinkedAssetTerms(thesis).ToList();
            var enabledRules = thesis.Rules.Where(r => r.IsEnabled).ToList();

            foreach (var segment in segments)
            {
                if (linked.Contains((thesis.Id, segment.Id))) continue;

                var completedAt = segment.Transcript?.CompletedAt;
                if (watermark.HasValue && completedAt.HasValue && completedAt.Value <= watermark.Value) continue;

                var similarity = Similarity(thesis.Embedding, segment.Embedding);
                var similarityThreshold = _options.DefaultSegmentSimilarityThreshold;

                if (!similarity.HasValue || similarity.Value < similarityThreshold) continue;

                var anchor = AnchorSegment(segment, enabledRules, linkedAssetTerms);
                if (anchor.Rule is null) continue;

                db.ResearchEvidence.Add(new ResearchEvidence
                {
                    Id = Guid.NewGuid(),
                    ThesisId = thesis.Id,
                    ThesisRuleId = anchor.Rule.Id,
                    TranscriptSegmentId = segment.Id,
                    EvidenceType = "segment",
                    MatchKind = "matcher",
                    MatchReason = anchor.Reason,
                    Similarity = similarity,
                    MatchedAt = now,
                });
                linked.Add((thesis.Id, segment.Id));
                added++;
            }

            if (maxCompleted > (thesis.LastSegmentMatchedAt ?? DateTime.MinValue))
                thesis.LastSegmentMatchedAt = maxCompleted;
        }

        await db.SaveChangesAsync(cancellationToken);

        return (segments.Count, added);
    }

    private async Task<(int ChunksScanned, int EvidenceAdded)> ScanChunksAsync(
        IReadOnlyList<ResearchThesis> theses,
        ResearchScanRequest request,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(request.BatchSize ?? _options.BatchSize, 1, 5000);
        var minMatchedAt = theses.Select(t => t.LastChunkMatchedAt).Min();
        var cutoff = minMatchedAt ?? DateTime.UtcNow.AddDays(-30);

        var chunks = await db.ArticleChunks
            .AsNoTracking()
            .Include(c => c.Article)
            .Where(c => c.Embedding != null && c.CreatedAt >= cutoff)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.ChunkIndex)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (chunks.Count == 0) return (0, 0);

        if (chunks.Count == batchSize)
        {
            // The watermark only advances past fully-scanned timestamps, so a
            // truncated batch must carry every chunk sharing the boundary
            // timestamp or the rest of that filing would be skipped forever.
            var boundary = chunks[^1].CreatedAt;
            chunks.RemoveAll(c => c.CreatedAt == boundary);
            chunks.AddRange(await db.ArticleChunks
                .AsNoTracking()
                .Include(c => c.Article)
                .Where(c => c.Embedding != null && c.CreatedAt == boundary)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync(cancellationToken));
            logger.LogInformation(
                "Chunk scan hit the {BatchSize}-row cap; scanning {Count} chunks created through {Boundary} and deferring newer chunks to the next run",
                batchSize,
                chunks.Count,
                boundary);
        }

        var thesisIds = theses.Select(t => t.Id).ToList();
        var chunkIds = chunks.Select(c => c.Id).ToList();

        var existingLinks = await db.ResearchEvidence
            .AsNoTracking()
            .Where(e => e.ArticleChunkId != null &&
                thesisIds.Contains(e.ThesisId) &&
                chunkIds.Contains(e.ArticleChunkId!.Value))
            .Select(e => new { e.ThesisId, ChunkId = e.ArticleChunkId!.Value })
            .ToListAsync(cancellationToken);

        var linked = existingLinks
            .Select(e => (e.ThesisId, e.ChunkId))
            .ToHashSet();

        var added = 0;
        var now = DateTime.UtcNow;
        var similarityThreshold = _options.DefaultSegmentSimilarityThreshold;
        var maxCreated = chunks
            .Select(c => c.CreatedAt)
            .DefaultIfEmpty()
            .Max();

        foreach (var thesis in theses)
        {
            var watermark = thesis.LastChunkMatchedAt;
            var linkedAssetTerms = GetLinkedAssetTerms(thesis).ToList();
            var enabledRules = thesis.Rules.Where(r => r.IsEnabled).ToList();

            foreach (var chunk in chunks)
            {
                if (linked.Contains((thesis.Id, chunk.Id))) continue;

                if (watermark.HasValue && chunk.CreatedAt <= watermark.Value) continue;

                var similarity = Similarity(thesis.Embedding, chunk.Embedding);
                if (!similarity.HasValue || similarity.Value < similarityThreshold) continue;

                var anchor = AnchorChunk(chunk, enabledRules, linkedAssetTerms);
                if (anchor.Rule is null) continue;

                db.ResearchEvidence.Add(new ResearchEvidence
                {
                    Id = Guid.NewGuid(),
                    ThesisId = thesis.Id,
                    ThesisRuleId = anchor.Rule.Id,
                    ArticleChunkId = chunk.Id,
                    EvidenceType = "chunk",
                    MatchKind = "matcher",
                    MatchReason = anchor.Reason,
                    Similarity = similarity,
                    MatchedAt = now,
                });
                linked.Add((thesis.Id, chunk.Id));
                added++;
            }

            if (maxCreated > (thesis.LastChunkMatchedAt ?? DateTime.MinValue))
                thesis.LastChunkMatchedAt = maxCreated;
        }

        await db.SaveChangesAsync(cancellationToken);

        return (chunks.Count, added);
    }

    private MatchResult? MatchArticle(
        ResearchThesis thesis,
        ThesisRule rule,
        IReadOnlyCollection<string> linkedAssetTerms,
        Article article)
    {
        var text = BuildArticleText(article);
        if (ContainsAnyWord(text, ParseTerms(rule.ExcludeTerms))) return null;
        if (!PassesSourceFilters(rule, article.Source, article.SourceTier)) return null;

        var eventType = article.Cluster?.Event?.EventType ?? article.Cluster?.TriageEventType;
        if (!PassesEventFilter(rule, eventType)) return null;

        var assetTerms = ParseTerms(rule.AssetKeywords).Concat(linkedAssetTerms).Where(IsSafeBareAssetTerm).ToArray();
        var conceptTerms = ParseTerms(rule.ConceptKeywords).ToArray();
        var assetMatch = ContainsAnyWord(text, assetTerms);
        var conceptMatch = ContainsAnyWord(text, conceptTerms);
        var similarity = Similarity(thesis.Embedding, article.Embedding);
        var similarityThreshold = rule.MinArticleSimilarity ?? _options.DefaultSimilarityThreshold;
        var similarityMatch = similarity.HasValue && similarity.Value >= similarityThreshold;

        if (!AcceptSignal(assetMatch, conceptMatch, conceptTerms.Length > 0, similarityMatch))
            return null;

        return new MatchResult(
            BuildReason(assetMatch, conceptMatch, similarityMatch, eventType),
            similarity);
    }

    private MatchResult? MatchEvent(
        ResearchThesis thesis,
        ThesisRule rule,
        IReadOnlyCollection<string> linkedAssetTerms,
        Event ev)
    {
        if (!PassesEventFilter(rule, ev.EventType)) return null;
        if (ev.Cluster is null) return null;

        var sourcePass = ev.Cluster.Articles.Count == 0 ||
            ev.Cluster.Articles.Any(a => PassesSourceFilters(rule, a.Source, a.SourceTier));
        if (!sourcePass) return null;

        var text = string.Join('\n',
            ev.EventType,
            ev.Summary,
            ev.Cluster.Symbol,
            string.Join('\n', ev.Cluster.Articles.Select(BuildArticleText)));

        if (ContainsAnyWord(text, ParseTerms(rule.ExcludeTerms))) return null;

        var assetTerms = ParseTerms(rule.AssetKeywords).Concat(linkedAssetTerms).Where(IsSafeBareAssetTerm).ToArray();
        var conceptTerms = ParseTerms(rule.ConceptKeywords).ToArray();
        var assetMatch = ContainsAnyWord(text, assetTerms);
        var conceptMatch = ContainsAnyWord(text, conceptTerms);
        var similarity = ev.Cluster.Articles
            .Select(a => Similarity(thesis.Embedding, a.Embedding))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .DefaultIfEmpty()
            .Max();
        var hasSimilarity = similarity > 0;
        var similarityThreshold = rule.MinArticleSimilarity ?? _options.DefaultSimilarityThreshold;
        var similarityMatch = hasSimilarity && similarity >= similarityThreshold;

        if (!AcceptSignal(assetMatch, conceptMatch, conceptTerms.Length > 0, similarityMatch))
            return null;

        return new MatchResult(
            BuildReason(assetMatch, conceptMatch, similarityMatch, ev.EventType),
            hasSimilarity ? similarity : null);
    }

    private static bool AcceptSignal(
        bool assetMatch,
        bool conceptMatch,
        bool hasConceptTerms,
        bool similarityMatch)
    {
        // Asset match or similarity match is sufficient on its own.
        // Concept terms broaden coverage (they show up in the reason text)
        // but do not gate matches — the previous AND-gate produced silent
        // zero-evidence theses on topics the embedding clearly understood.
        _ = hasConceptTerms;
        _ = conceptMatch;
        return assetMatch || similarityMatch;
    }

    private static bool PassesEventFilter(ThesisRule rule, string? eventType)
    {
        var eventTypes = ParseTerms(rule.EventTypes);
        return eventTypes.Count == 0 ||
            (!string.IsNullOrWhiteSpace(eventType) &&
             eventTypes.Any(t => string.Equals(t, eventType, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool PassesSourceFilters(ThesisRule rule, string source, string sourceTier)
    {
        var sourceNames = ParseTerms(rule.SourceNames);
        var sourceTiers = ParseTerms(rule.SourceTiers);

        var namePass = sourceNames.Count == 0 ||
            sourceNames.Any(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase));
        var tierPass = sourceTiers.Count == 0 ||
            sourceTiers.Any(s => string.Equals(s, sourceTier, StringComparison.OrdinalIgnoreCase));

        return namePass && tierPass;
    }

    private static (ThesisRule? Rule, string Reason) AnchorChunk(
        ArticleChunk chunk,
        IReadOnlyCollection<ThesisRule> rules,
        IReadOnlyCollection<string> linkedAssetTerms)
    {
        var symbol = chunk.Article?.Symbol ?? string.Empty;
        var headlineText = chunk.Article is null
            ? string.Empty
            : string.Join('\n', chunk.Article.Symbol, chunk.Article.Headline, chunk.Article.Summary);
        var chunkText = chunk.Text ?? string.Empty;

        foreach (var rule in rules)
        {
            var assetTerms = ParseTerms(rule.AssetKeywords).Concat(linkedAssetTerms).Where(IsSafeBareAssetTerm).ToArray();
            if (assetTerms.Length == 0) continue;

            if (!string.IsNullOrWhiteSpace(symbol) &&
                assetTerms.Any(t => string.Equals(t, symbol, StringComparison.OrdinalIgnoreCase)))
                return (rule, $"asset match (parent symbol {symbol}); embedding similarity");

            if (ContainsAnyWord(chunkText, assetTerms))
                return (rule, "asset match (chunk body); embedding similarity");

            if (ContainsAnyWord(headlineText, assetTerms))
                return (rule, "asset match (parent article); embedding similarity");
        }

        return (null, string.Empty);
    }

    private static (ThesisRule? Rule, string Reason) AnchorSegment(
        TranscriptSegment segment,
        IReadOnlyCollection<ThesisRule> rules,
        IReadOnlyCollection<string> linkedAssetTerms)
    {
        var symbol = segment.Transcript?.Symbol ?? string.Empty;
        var segmentText = segment.Text ?? string.Empty;

        foreach (var rule in rules)
        {
            var assetTerms = ParseTerms(rule.AssetKeywords).Concat(linkedAssetTerms).Where(IsSafeBareAssetTerm).ToArray();
            if (assetTerms.Length == 0) continue;

            if (!string.IsNullOrWhiteSpace(symbol) &&
                assetTerms.Any(t => string.Equals(t, symbol, StringComparison.OrdinalIgnoreCase)))
                return (rule, $"asset match (transcript symbol {symbol}); embedding similarity");

            if (ContainsAnyWord(segmentText, assetTerms))
                return (rule, "asset match (segment body); embedding similarity");
        }

        return (null, string.Empty);
    }

    private static async Task<List<Guid>> FindArticleIdsByKeywordsAsync(
        MarketLensDbContext db,
        IReadOnlyList<string> terms,
        DateTime? cutoff,
        int limit,
        CancellationToken cancellationToken)
    {
        var escaped = terms
            .Select(t => System.Text.RegularExpressions.Regex.Escape(t))
            .ToArray();
        var conditions = escaped
            .Select((_, i) => $"""("Headline" ~* @p{i} OR "Summary" ~* @p{i})""");
        var where = string.Join(" OR ", conditions);

        var sql = $"""
            SELECT "Id" FROM articles
            WHERE ({where})
            {(cutoff.HasValue ? $"""AND "IngestedAt" >= @cutoff""" : "")}
            ORDER BY "IngestedAt" DESC
            LIMIT {limit}
            """;

        var parameters = new List<Npgsql.NpgsqlParameter>();
        for (var i = 0; i < escaped.Length; i++)
            parameters.Add(new Npgsql.NpgsqlParameter($"@p{i}", $@"\y{escaped[i]}\y"));
        if (cutoff.HasValue)
            parameters.Add(new Npgsql.NpgsqlParameter("@cutoff", cutoff.Value));

        return await db.Database
            .SqlQueryRaw<Guid>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
    }

    private static IEnumerable<string> GetLinkedAssetTerms(ResearchThesis thesis)
    {
        foreach (var link in thesis.ThesisAssets)
        {
            if (link.Asset is null) continue;

            if (!string.IsNullOrWhiteSpace(link.Asset.Name))
                yield return link.Asset.Name;
            if (!string.IsNullOrWhiteSpace(link.Asset.Symbol))
            {
                if (!WatchlistMatcher.IsAmbiguousBareSymbol(link.Asset.Symbol))
                    yield return link.Asset.Symbol;
            }

            foreach (var keyword in ParseTerms(link.Asset.Keywords))
            {
                if (!WatchlistMatcher.IsAmbiguousBareSymbol(keyword))
                    yield return keyword;
            }
        }
    }

    private static bool IsSafeBareAssetTerm(string term)
        => !WatchlistMatcher.IsAmbiguousBareSymbol(term);

    private static IReadOnlyList<string> ParseTerms(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json)?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ContainsAnyWord(string text, IEnumerable<string> terms)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var pattern = $@"(?<![A-Za-z0-9]){System.Text.RegularExpressions.Regex.Escape(term)}(?![A-Za-z0-9])";
            if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    private static string BuildArticleText(Article article)
    {
        return string.Join('\n',
            article.Symbol,
            article.Source,
            article.SourceTier,
            article.Publisher,
            article.Headline,
            article.Summary);
    }

    private static decimal? Similarity(Vector? thesisEmbedding, Vector? articleEmbedding)
    {
        if (thesisEmbedding is null || articleEmbedding is null) return null;

        var a = thesisEmbedding.ToArray();
        var b = articleEmbedding.ToArray();
        if (a.Length == 0 || a.Length != b.Length) return null;

        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return null;

        var cosine = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Round((decimal)cosine, 5);
    }

    private static string BuildReason(bool assetMatch, bool conceptMatch, bool similarityMatch, string? eventType)
    {
        var parts = new List<string>();
        if (assetMatch) parts.Add("asset keyword");
        if (conceptMatch) parts.Add("concept keyword");
        if (similarityMatch) parts.Add("embedding similarity");
        if (!string.IsNullOrWhiteSpace(eventType)) parts.Add($"event type {eventType}");

        return string.Join("; ", parts);
    }

    private sealed record MatchResult(string Reason, decimal? Similarity);
}

public class ResearchMatcherService(
    IServiceProvider services,
    ILogger<ResearchMatcherService> logger,
    IOptions<ResearchMatcherOptions> options) : BackgroundService
{
    private readonly ResearchMatcherOptions _options = options.Value;
    private readonly string _workerId = $"{nameof(ResearchMatcherService)}:{Environment.MachineName}:{Environment.ProcessId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_options.InitialDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            ResearchMatcherBatchResult result;
            try
            {
                result = await DrainAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Research matcher run failed");
                result = new ResearchMatcherBatchResult(0, 0, 0, 0, 0, 0, 0, 1);
            }

            var delay = result.Claimed == 0 && result.EvidenceAdded == 0
                ? TimeSpan.FromSeconds(Math.Max(_options.IntervalSeconds, _options.IdleIntervalSeconds))
                : TimeSpan.FromSeconds(_options.IntervalSeconds);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<ResearchMatcherBatchResult> DrainAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketLensDbContext>();
        var recorder = scope.ServiceProvider.GetRequiredService<PipelineRunRecorder>();
        var queue = scope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
        var runId = await recorder.StartAsync(
            PipelineStages.ResearchMatcher,
            PipelineTriggers.Scheduled,
            metadata: new
            {
                _options.BatchSize,
                _options.WorkBatchSize,
                _options.EnqueueBatchSize,
                _options.ReenqueueCooldownMinutes,
                _options.LookbackHours,
            },
            cancellationToken: cancellationToken);

        try
        {
            await queue.RecoverExpiredLeasesAsync(DateTime.UtcNow, cancellationToken);
            var enqueued = await EnqueueThesisWorkAsync(db, queue, cancellationToken);

            var claimed = await queue.ClaimBatchAsync(
                PipelineWorkTypes.ResearchMatching,
                Math.Max(1, _options.WorkBatchSize),
                _workerId,
                TimeSpan.FromMinutes(Math.Max(1, _options.LeaseMinutes)),
                cancellationToken);

            if (claimed.Count == 0)
            {
                var empty = new ResearchMatcherBatchResult(enqueued, 0, 0, 0, 0, 0, 0);
                await recorder.SucceedAsync(runId, new PipelineRunCounts(), empty, cancellationToken);
                return empty;
            }

            var articlesScanned = 0;
            var eventsScanned = 0;
            var segmentsScanned = 0;
            var chunksScanned = 0;
            var evidenceAdded = 0;
            var failures = 0;

            foreach (var work in claimed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thesisId = Guid.Parse(work.Item.NaturalKey);

                try
                {
                    using var itemScope = services.CreateScope();
                    var handler = itemScope.ServiceProvider.GetRequiredService<ResearchMatchThesisHandler>();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();

                    var itemResult = await handler.ProcessAsync(
                        thesisId,
                        new ResearchScanRequest(
                            ThesisId: thesisId,
                            ActiveOnly: false,
                            LookbackHours: _options.LookbackHours,
                            BatchSize: _options.BatchSize),
                        cancellationToken);

                    articlesScanned += itemResult.ArticlesScanned;
                    eventsScanned += itemResult.EventsScanned;
                    segmentsScanned += itemResult.SegmentsScanned;
                    chunksScanned += itemResult.ChunksScanned;
                    evidenceAdded += itemResult.EvidenceAdded;

                    await itemQueue.CompleteAsync(work.Attempt.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Research matching failed for thesis {ThesisId}", thesisId);

                    using var itemScope = services.CreateScope();
                    var itemQueue = itemScope.ServiceProvider.GetRequiredService<ILocalWorkQueue>();
                    await itemQueue.FailAsync(work.Attempt.Id, ex.Message, cancellationToken: cancellationToken);
                }
            }

            var result = new ResearchMatcherBatchResult(
                enqueued,
                claimed.Count,
                articlesScanned,
                eventsScanned,
                segmentsScanned,
                chunksScanned,
                evidenceAdded,
                failures);

            var inputCount = articlesScanned + eventsScanned + segmentsScanned + chunksScanned;
            await recorder.SucceedAsync(
                runId,
                new PipelineRunCounts(InputCount: inputCount, OutputCount: evidenceAdded, ErrorCount: failures),
                result,
                cancellationToken);
            if (evidenceAdded > 0)
            {
                await recorder.RecordMaterializationAsync(
                    runId,
                    assetType: "table",
                    assetKey: "research_evidence",
                    recordCount: evidenceAdded,
                    metadata: result,
                    cancellationToken: cancellationToken);
                logger.LogInformation("Research matcher attached {Count} evidence items", evidenceAdded);
            }

            return result;
        }
        catch (Exception ex)
        {
            await recorder.FailAsync(runId, ex, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<int> EnqueueThesisWorkAsync(
        MarketLensDbContext db,
        ILocalWorkQueue queue,
        CancellationToken cancellationToken)
    {
        var backlogLimit = Math.Max(1, _options.QueueBacklogLimit);
        var backlog = await db.PipelineWorkItems
            .AsNoTracking()
            .CountAsync(i =>
                i.WorkType == PipelineWorkTypes.ResearchMatching &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        var capacity = backlogLimit - backlog;
        if (capacity <= 0) return 0;

        var take = Math.Min(Math.Max(1, _options.EnqueueBatchSize), capacity);
        var activeKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.ResearchMatching &&
                (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running))
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var cooldownCutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, _options.ReenqueueCooldownMinutes));
        var recentCompletedKeys = await db.PipelineWorkItems
            .AsNoTracking()
            .Where(i =>
                i.WorkType == PipelineWorkTypes.ResearchMatching &&
                i.Status == PipelineWorkStatuses.Completed &&
                i.CompletedAt != null &&
                i.CompletedAt >= cooldownCutoff)
            .Select(i => i.NaturalKey)
            .ToListAsync(cancellationToken);

        var blockedKeys = activeKeys
            .Concat(recentCompletedKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = await db.ResearchTheses
            .AsNoTracking()
            .Where(t => t.Rules.Any(r => r.IsEnabled))
            .OrderByDescending(t => t.Status == ThesisStatuses.Active)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenByDescending(t => t.CreatedAt)
            .Take(Math.Max(take * 4, take))
            .Select(t => new { t.Id, t.Status, t.UpdatedAt })
            .ToListAsync(cancellationToken);

        candidates = candidates
            .Where(t => !blockedKeys.Contains(t.Id.ToString()))
            .Take(take)
            .ToList();

        foreach (var candidate in candidates)
        {
            await queue.EnqueueAsync(
                new EnqueueWorkRequest(
                    WorkType: PipelineWorkTypes.ResearchMatching,
                    NaturalKey: candidate.Id.ToString(),
                    PayloadJson: $$"""{"thesisId":"{{candidate.Id}}"}""",
                    Priority: PriorityFromThesis(candidate.Status, candidate.UpdatedAt)),
                cancellationToken);
        }

        return candidates.Count;
    }

    private static int PriorityFromThesis(string status, DateTime updatedAt)
    {
        var recencyMinutes = (updatedAt - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes;
        var activeBoost = string.Equals(status, ThesisStatuses.Active, StringComparison.OrdinalIgnoreCase) ? 1_000_000 : 0;
        return activeBoost + (int)Math.Clamp(recencyMinutes, 0, int.MaxValue - activeBoost);
    }
}

public sealed record ResearchMatcherBatchResult(
    int Enqueued,
    int Claimed,
    int ArticlesScanned,
    int EventsScanned,
    int SegmentsScanned,
    int ChunksScanned,
    int EvidenceAdded,
    int ItemFailures = 0);
