using MarketLens.Api.Services.Pipeline;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints.Research;

public static partial class ResearchEndpoints
{
    private static void MapEvidenceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/theses/{id:guid}/evidence", async (
            MarketLensDbContext db,
            Guid id,
            int? take,
            CancellationToken ct) =>
        {
            var exists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            if (!exists) return Results.NotFound();

            var limit = Math.Clamp(take ?? 100, 1, 500);
            var evidence = await db.ResearchEvidence
                .AsNoTracking()
                .Include(e => e.Article)
                .Include(e => e.Cluster)
                .ThenInclude(c => c!.Event)
                .Include(e => e.TranscriptSegment)
                .ThenInclude(s => s!.Transcript)
                .Include(e => e.ArticleChunk)
                .ThenInclude(c => c!.Article)
                .Where(e => e.ThesisId == id)
                .OrderByDescending(e => e.MatchedAt)
                .Take(limit)
                .Select(e => new
                {
                    e.Id,
                    evidenceKind = e.ArticleChunkId != null ? "chunk" : (e.TranscriptSegmentId != null ? "segment" : (e.ClusterId != null ? "cluster" : "article")),
                    e.EvidenceType,
                    e.MatchKind,
                    e.MatchReason,
                    e.Similarity,
                    e.Stance,
                    e.StanceConfidence,
                    e.StanceRationale,
                    e.StanceModel,
                    e.StancePromptVersion,
                    e.ClassifiedAt,
                    e.ReviewStatus,
                    e.IsPinned,
                    e.Notes,
                    e.ReviewerNote,
                    e.MatchedAt,
                    e.ReviewedAt,
                    e.ThesisRuleId,
                    article = e.Article == null ? null : new
                    {
                        e.Article.Id,
                        e.Article.Source,
                        e.Article.SourceTier,
                        e.Article.Symbol,
                        e.Article.Headline,
                        e.Article.Summary,
                        e.Article.Url,
                        e.Article.Publisher,
                        e.Article.PublishedAt,
                    },
                    eventItem = e.Cluster == null || e.Cluster.Event == null ? null : new
                    {
                        clusterId = e.Cluster.Id,
                        e.Cluster.Symbol,
                        e.Cluster.Event.EventType,
                        e.Cluster.Event.Summary,
                        e.Cluster.Event.Importance,
                        e.Cluster.Event.Sentiment,
                        e.Cluster.LastSeenAt,
                    },
                    segmentItem = e.TranscriptSegment == null ? null : new
                    {
                        segmentId = e.TranscriptSegment.Id,
                        transcriptId = e.TranscriptSegment.TranscriptId,
                        e.TranscriptSegment.SegmentIndex,
                        e.TranscriptSegment.StartSeconds,
                        e.TranscriptSegment.EndSeconds,
                        e.TranscriptSegment.Speaker,
                        e.TranscriptSegment.Text,
                        audioUrl = e.TranscriptSegment.Transcript == null ? null : e.TranscriptSegment.Transcript.AudioUrl,
                        callDate = e.TranscriptSegment.Transcript == null ? (DateTime?)null : e.TranscriptSegment.Transcript.CallDate,
                        transcriptSymbol = e.TranscriptSegment.Transcript == null ? null : e.TranscriptSegment.Transcript.Symbol,
                    },
                    chunkItem = e.ArticleChunk == null ? null : new
                    {
                        chunkId = e.ArticleChunk.Id,
                        articleId = e.ArticleChunk.ArticleId,
                        e.ArticleChunk.ChunkIndex,
                        e.ArticleChunk.Section,
                        text = e.ArticleChunk.Text,
                        filingRawPayload = e.ArticleChunk.Article == null ? null : e.ArticleChunk.Article.RawPayload,
                        filingUrl = e.ArticleChunk.Article == null ? null : e.ArticleChunk.Article.Url,
                        filingSymbol = e.ArticleChunk.Article == null ? null : e.ArticleChunk.Article.Symbol,
                        filingHeadline = e.ArticleChunk.Article == null ? null : e.ArticleChunk.Article.Headline,
                        filingPublishedAt = e.ArticleChunk.Article == null ? (DateTime?)null : e.ArticleChunk.Article.PublishedAt,
                    },
                })
                .ToListAsync(ct);

            return Results.Ok(evidence);
        });

        group.MapPatch("/theses/{id:guid}/evidence/{evidenceId:guid}/review", async (
            MarketLensDbContext db,
            ThesisKillCriterionEscalator killCriteriaEscalator,
            Guid id,
            Guid evidenceId,
            ReviewEvidenceRequest request,
            CancellationToken ct) =>
        {
            var evidence = await db.ResearchEvidence
                .FirstOrDefaultAsync(e => e.Id == evidenceId && e.ThesisId == id, ct);
            if (evidence is null) return Results.NotFound();

            evidence.ReviewStatus = NormalizeReviewStatus(request.ReviewStatus);
            if (request.Stance is not null)
            {
                if (evidence.OriginalStance is null && evidence.ClassifiedAt is not null)
                {
                    evidence.OriginalStance = evidence.Stance;
                    evidence.OriginalStanceConfidence = evidence.StanceConfidence;
                }
                evidence.Stance = NormalizeStance(request.Stance);
            }
            if (request.IsPinned.HasValue)
                evidence.IsPinned = request.IsPinned.Value;
            if (request.ReviewerNote is not null)
                evidence.ReviewerNote = EmptyToNull(request.ReviewerNote);
            evidence.ReviewedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            var killCriteria = await killCriteriaEscalator.ReconcileAsync(id, ct);
            return Results.Ok(new
            {
                evidence.Id,
                evidence.ThesisId,
                evidence.Stance,
                evidence.ReviewStatus,
                evidence.IsPinned,
                evidence.ReviewerNote,
                evidence.ReviewedAt,
                killCriteria,
            });
        });

        group.MapPost("/theses/{id:guid}/evidence/articles/{articleId:guid}", async (
            MarketLensDbContext db,
            Guid id,
            Guid articleId,
            AttachEvidenceRequest request,
            CancellationToken ct) =>
        {
            var thesisExists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            var articleExists = await db.Articles.AnyAsync(a => a.Id == articleId, ct);
            if (!thesisExists || !articleExists) return Results.NotFound();

            var existing = await db.ResearchEvidence
                .FirstOrDefaultAsync(e => e.ThesisId == id && e.ArticleId == articleId, ct);
            if (existing is not null) return Results.Ok(existing);

            var evidence = new ResearchEvidence
            {
                Id = Guid.NewGuid(),
                ThesisId = id,
                ArticleId = articleId,
                EvidenceType = "article",
                MatchKind = "manual",
                MatchReason = EmptyToNull(request.Reason) ?? "Manual article attachment",
                Stance = NormalizeStance(request.Stance),
                Notes = request.Notes ?? string.Empty,
                MatchedAt = DateTime.UtcNow,
            };
            db.ResearchEvidence.Add(evidence);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/theses/{id}/evidence/{evidence.Id}", evidence);
        });

        group.MapPost("/theses/{id:guid}/evidence/events/{clusterId:guid}", async (
            MarketLensDbContext db,
            Guid id,
            Guid clusterId,
            AttachEvidenceRequest request,
            CancellationToken ct) =>
        {
            var thesisExists = await db.ResearchTheses.AnyAsync(t => t.Id == id, ct);
            var eventExists = await db.Events.AnyAsync(e => e.ClusterId == clusterId, ct);
            if (!thesisExists || !eventExists) return Results.NotFound();

            var existing = await db.ResearchEvidence
                .FirstOrDefaultAsync(e => e.ThesisId == id && e.ClusterId == clusterId, ct);
            if (existing is not null) return Results.Ok(existing);

            var evidence = new ResearchEvidence
            {
                Id = Guid.NewGuid(),
                ThesisId = id,
                ClusterId = clusterId,
                EvidenceType = "event",
                MatchKind = "manual",
                MatchReason = EmptyToNull(request.Reason) ?? "Manual event attachment",
                Stance = NormalizeStance(request.Stance),
                Notes = request.Notes ?? string.Empty,
                MatchedAt = DateTime.UtcNow,
            };
            db.ResearchEvidence.Add(evidence);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/research/theses/{id}/evidence/{evidence.Id}", evidence);
        });
    }

    private static string NormalizeReviewStatus(string? status)
    {
        var value = EmptyToNull(status)?.ToLowerInvariant();
        return value is "pending" or "accepted" or "rejected" or "needs_review" ? value : "pending";
    }

    private static string NormalizeStance(string? stance)
    {
        var value = EmptyToNull(stance)?.ToLowerInvariant();
        return value is StanceValues.Supports or StanceValues.Contradicts or StanceValues.Neutral or StanceValues.Unknown
            ? value
            : StanceValues.Unknown;
    }
}
