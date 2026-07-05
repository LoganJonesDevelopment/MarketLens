using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using MarketLens.Infrastructure.Sidecars;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MarketLens.Api.Services.Pipeline;

public sealed record TranscriptIngestionItemResult(Guid TranscriptId, bool Processed, int SegmentsCreated);

public sealed class TranscriptIngestionHandler(
    MarketLensDbContext db,
    WhisperClient whisper,
    IEmbeddingClient embedder,
    ILogger<TranscriptIngestionHandler> logger)
{
    public async Task<TranscriptIngestionItemResult> ProcessAsync(
        Guid transcriptId,
        CancellationToken cancellationToken)
    {
        var transcript = await db.Transcripts
            .Include(t => t.Segments)
            .SingleOrDefaultAsync(t => t.Id == transcriptId, cancellationToken);

        if (transcript is null)
            return new TranscriptIngestionItemResult(transcriptId, Processed: false, SegmentsCreated: 0);

        if (transcript.Status == TranscriptStatus.Completed || transcript.Segments.Count > 0)
            return new TranscriptIngestionItemResult(transcriptId, Processed: false, SegmentsCreated: 0);

        transcript.Status = TranscriptStatus.Processing;
        transcript.Error = null;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Transcribing {Id} [{Symbol}] from {Url}",
            transcript.Id, transcript.Symbol, transcript.AudioUrl);

        try
        {
            var result = await whisper.TranscribeAsync(transcript.AudioUrl, null, cancellationToken);

            var texts = result.Segments.Select(s => s.Text).ToList();
            IReadOnlyList<float[]> embeddings = texts.Count > 0
                ? await embedder.EmbedBatchAsync(texts, cancellationToken)
                : [];

            for (var i = 0; i < result.Segments.Count; i++)
            {
                var segment = result.Segments[i];
                db.TranscriptSegments.Add(new TranscriptSegment
                {
                    Id = Guid.NewGuid(),
                    TranscriptId = transcript.Id,
                    SegmentIndex = segment.Index,
                    StartSeconds = segment.Start,
                    EndSeconds = segment.End,
                    Speaker = null,
                    Text = segment.Text,
                    Embedding = embeddings.Count > i ? new Vector(embeddings[i]) : null,
                });
            }

            transcript.DurationSeconds = result.Duration;
            transcript.SegmentCount = result.Segments.Count;
            transcript.Status = TranscriptStatus.Completed;
            transcript.CompletedAt = DateTime.UtcNow;
            transcript.Error = null;

            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Transcript {Id} completed: {Segments} segments, {Duration:F1}s",
                transcript.Id, result.Segments.Count, result.Duration);

            return new TranscriptIngestionItemResult(
                transcript.Id,
                Processed: true,
                SegmentsCreated: result.Segments.Count);
        }
        catch (Exception ex)
        {
            transcript.Status = TranscriptStatus.Failed;
            transcript.Error = ex.Message;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}
