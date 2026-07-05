using MarketLens.Api.HostedServices;
using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class TranscriptEndpoints
{
    public static void MapTranscriptEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/transcripts", async (
            TranscriptQueueRequest req,
            MarketLensDbContext db,
            CancellationToken ct) =>
        {
            var symbol = req.Symbol.ToUpperInvariant();
            var callDate = req.CallDate ?? DateTime.UtcNow.Date;
            var transcriptId = Guid.NewGuid();

            var headline = string.IsNullOrWhiteSpace(req.CallType)
                ? $"{symbol} earnings call ({callDate:yyyy-MM-dd})"
                : $"{symbol} {req.CallType} call ({callDate:yyyy-MM-dd})";

            var (tier, _) = SourceReputation.For(SourceNames.Transcript);

            var article = new Article
            {
                Id = Guid.NewGuid(),
                Source = SourceNames.Transcript,
                SourceId = transcriptId.ToString(),
                SourceTier = tier,
                Symbol = symbol,
                Headline = headline,
                Summary = null,
                Url = req.AudioUrl,
                Publisher = null,
                PublishedAt = DateTime.SpecifyKind(callDate, DateTimeKind.Utc),
                IngestedAt = DateTime.UtcNow,
                RawPayload = "{}",
            };

            var transcript = new Transcript
            {
                Id = transcriptId,
                Symbol = symbol,
                CallType = req.CallType,
                CallDate = DateTime.SpecifyKind(callDate, DateTimeKind.Utc),
                AudioUrl = req.AudioUrl,
                Status = TranscriptStatus.Queued,
                IngestedAt = DateTime.UtcNow,
                ArticleId = article.Id,
            };

            db.Articles.Add(article);
            db.Transcripts.Add(transcript);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = transcriptId, articleId = article.Id, status = transcript.Status });
        });

        app.MapGet("/api/transcripts/{id:guid}", async (
            Guid id,
            MarketLensDbContext db,
            CancellationToken ct) =>
        {
            var t = await db.Transcripts
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.Symbol,
                    x.CallType,
                    x.CallDate,
                    x.AudioUrl,
                    x.DurationSeconds,
                    x.SegmentCount,
                    x.Status,
                    x.IngestedAt,
                    x.CompletedAt,
                    x.Error,
                    x.ArticleId,
                })
                .FirstOrDefaultAsync(ct);

            return t is null ? Results.NotFound() : Results.Ok(t);
        });

        app.MapGet("/api/transcripts/discover/{symbol}", async (
            string symbol,
            AudioReplayDiscovery discovery,
            CancellationToken ct) =>
        {
            var url = await discovery.DiscoverAsync(symbol.ToUpperInvariant(), DateTime.UtcNow, ct);
            return Results.Ok(new { symbol = symbol.ToUpperInvariant(), discovered = url });
        });

        app.MapPost("/api/transcripts/discover-and-queue/{symbol}", async (
            string symbol,
            AudioReplayDiscovery discovery,
            MarketLensDbContext db,
            CancellationToken ct) =>
        {
            var sym = symbol.ToUpperInvariant();
            var callDate = DateTime.UtcNow.Date;
            var url = await discovery.DiscoverAsync(sym, callDate, ct);
            if (url is null)
                return Results.Ok(new { symbol = sym, discovered = (string?)null, queued = false });

            var transcriptId = Guid.NewGuid();
            var callDateUtc = DateTime.SpecifyKind(callDate, DateTimeKind.Utc);
            var (tier, _) = SourceReputation.For(SourceNames.Transcript);

            var article = new Article
            {
                Id = Guid.NewGuid(),
                Source = SourceNames.Transcript,
                SourceId = transcriptId.ToString(),
                SourceTier = tier,
                Symbol = sym,
                Headline = $"{sym} earnings call ({callDateUtc:yyyy-MM-dd})",
                Summary = null,
                Url = url,
                Publisher = null,
                PublishedAt = callDateUtc,
                IngestedAt = DateTime.UtcNow,
                RawPayload = "{}",
            };

            var transcript = new Transcript
            {
                Id = transcriptId,
                Symbol = sym,
                CallType = "earnings",
                CallDate = callDateUtc,
                AudioUrl = url,
                Status = TranscriptStatus.Queued,
                IngestedAt = DateTime.UtcNow,
                ArticleId = article.Id,
            };

            db.Articles.Add(article);
            db.Transcripts.Add(transcript);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { symbol = sym, discovered = url, queued = true, transcriptId, articleId = article.Id });
        });

        app.MapGet("/api/transcripts/{id:guid}/segments", async (
            Guid id,
            MarketLensDbContext db,
            CancellationToken ct) =>
        {
            var segments = await db.TranscriptSegments
                .AsNoTracking()
                .Where(s => s.TranscriptId == id)
                .OrderBy(s => s.SegmentIndex)
                .Select(s => new
                {
                    s.Id,
                    s.SegmentIndex,
                    s.StartSeconds,
                    s.EndSeconds,
                    s.Speaker,
                    s.Text,
                    hasEmbedding = s.Embedding != null,
                })
                .ToListAsync(ct);

            return Results.Ok(segments);
        });
    }
}

public record TranscriptQueueRequest(
    string Symbol,
    string? CallType,
    DateTime? CallDate,
    string AudioUrl);
