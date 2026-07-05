using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLens.Api.Endpoints;

public static class PipelineQueueEndpoints
{
    private const string CancelledStatus = "cancelled";
    private const int MaxQueueTake = 250;
    private const int MaxPurgeTake = 5_000;

    public static void MapPipelineQueueEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pipeline/queue/summary", async (MarketLensDbContext db, CancellationToken ct) =>
        {
            var countRows = await db.PipelineWorkItems
                .AsNoTracking()
                .GroupBy(i => new { i.WorkType, i.Status })
                .Select(g => new { g.Key.WorkType, g.Key.Status, Count = g.Count() })
                .OrderBy(c => c.WorkType)
                .ThenBy(c => c.Status)
                .ToListAsync(ct);
            var counts = countRows
                .Select(c => new PipelineQueueCountDto(c.WorkType, c.Status, c.Count))
                .ToList();

            var oldestPending = await db.PipelineWorkItems
                .AsNoTracking()
                .Where(i => i.Status == PipelineWorkStatuses.Queued)
                .OrderBy(i => i.AvailableAt)
                .ThenBy(i => i.CreatedAt)
                .Select(i => ToItemDto(i))
                .FirstOrDefaultAsync(ct);

            var recentDeadLetters = await db.PipelineWorkItems
                .AsNoTracking()
                .Where(i => i.Status == PipelineWorkStatuses.DeadLetter)
                .OrderByDescending(i => i.DeadLetteredAt ?? i.UpdatedAt)
                .Take(8)
                .Select(i => ToItemDto(i))
                .ToListAsync(ct);

            var recentErrors = await db.PipelineWorkAttempts
                .AsNoTracking()
                .Where(a => a.ErrorMessage != null && a.ErrorMessage != "")
                .OrderByDescending(a => a.FinishedAt ?? a.StartedAt)
                .Take(8)
                .Select(a => new PipelineQueueAttemptDto(
                    a.Id,
                    a.WorkItemId,
                    a.WorkItem.WorkType,
                    a.WorkItem.NaturalKey,
                    a.AttemptNumber,
                    a.Status,
                    a.WorkerId,
                    a.StartedAt,
                    a.LeaseExpiresAt,
                    a.FinishedAt,
                    a.FinishedAt == null ? null : (double?)(a.FinishedAt.Value - a.StartedAt).TotalMilliseconds,
                    a.ErrorMessage))
                .ToListAsync(ct);

            return Results.Ok(new PipelineQueueSummaryDto(counts, oldestPending, recentDeadLetters, recentErrors));
        });

        app.MapGet("/api/pipeline/queue/items", async (
            MarketLensDbContext db,
            string? workType,
            string? status,
            string? q,
            int? take,
            CancellationToken ct) =>
        {
            var query = db.PipelineWorkItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(workType))
                query = query.Where(i => i.WorkType == workType);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(i => i.Status == status);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(i => i.NaturalKey.Contains(term) || i.WorkType.Contains(term));
            }

            var limit = Math.Clamp(take ?? 50, 1, MaxQueueTake);
            var items = await query
                .OrderBy(i => i.Status == PipelineWorkStatuses.Queued ? 0 : 1)
                .ThenBy(i => i.Status == PipelineWorkStatuses.Queued ? i.AvailableAt : DateTime.MaxValue)
                .ThenByDescending(i => i.UpdatedAt)
                .Take(limit)
                .Select(i => ToItemDto(i))
                .ToListAsync(ct);

            return Results.Ok(new PipelineQueueItemsDto(items));
        });

        app.MapGet("/api/pipeline/queue/items/{id:guid}/attempts", async (
            MarketLensDbContext db,
            Guid id,
            int? take,
            CancellationToken ct) =>
        {
            var exists = await db.PipelineWorkItems
                .AsNoTracking()
                .AnyAsync(i => i.Id == id, ct);
            if (!exists) return Results.NotFound();

            var limit = Math.Clamp(take ?? 12, 1, 50);
            var attempts = await db.PipelineWorkAttempts
                .AsNoTracking()
                .Where(a => a.WorkItemId == id)
                .OrderByDescending(a => a.AttemptNumber)
                .Take(limit)
                .Select(a => new PipelineQueueAttemptDto(
                    a.Id,
                    a.WorkItemId,
                    a.WorkItem.WorkType,
                    a.WorkItem.NaturalKey,
                    a.AttemptNumber,
                    a.Status,
                    a.WorkerId,
                    a.StartedAt,
                    a.LeaseExpiresAt,
                    a.FinishedAt,
                    a.FinishedAt == null ? null : (double?)(a.FinishedAt.Value - a.StartedAt).TotalMilliseconds,
                    a.ErrorMessage))
                .ToListAsync(ct);

            return Results.Ok(new PipelineQueueAttemptsDto(attempts));
        });

        app.MapPost("/api/pipeline/queue/items/{id:guid}/retry", async (
            MarketLensDbContext db,
            Guid id,
            CancellationToken ct) =>
        {
            var item = await db.PipelineWorkItems.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) return Results.NotFound();

            if (item.Status is PipelineWorkStatuses.Queued or PipelineWorkStatuses.Running)
                return Results.Ok(PipelineQueueActionResult.From(item, false, "Work is already active."));

            if (item.Status != PipelineWorkStatuses.DeadLetter && item.Status != CancelledStatus)
                return Results.Ok(PipelineQueueActionResult.From(item, false, "Only dead-lettered or cancelled work can be retried."));

            var activeDuplicateExists = await db.PipelineWorkItems
                .AnyAsync(i =>
                    i.Id != item.Id &&
                    i.WorkType == item.WorkType &&
                    i.NaturalKey == item.NaturalKey &&
                    (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                    ct);
            if (activeDuplicateExists)
                return Results.Ok(PipelineQueueActionResult.From(item, false, "Matching work is already active."));

            var originalStatus = item.Status;
            var now = DateTime.UtcNow;
            item.Status = PipelineWorkStatuses.Queued;
            item.AvailableAt = now;
            item.UpdatedAt = now;
            item.MaxAttempts = Math.Max(item.MaxAttempts, item.AttemptCount + 1);
            item.CurrentAttemptId = null;
            item.ClaimedBy = null;
            item.ClaimedAt = null;
            item.LeaseExpiresAt = null;
            item.CompletedAt = null;
            item.DeadLetteredAt = null;
            item.LastError = null;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Ok(new PipelineQueueActionResult(item.Id, originalStatus, false, "Matching work is already active."));
            }

            return Results.Ok(PipelineQueueActionResult.From(item, true, "Work requeued."));
        });

        app.MapPost("/api/pipeline/queue/items/{id:guid}/cancel", async (
            MarketLensDbContext db,
            Guid id,
            CancellationToken ct) =>
        {
            var item = await db.PipelineWorkItems.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) return Results.NotFound();

            if (item.Status is not (PipelineWorkStatuses.Queued or PipelineWorkStatuses.Running))
                return Results.Ok(PipelineQueueActionResult.From(item, false, "Work is not active."));

            var now = DateTime.UtcNow;
            if (item.CurrentAttemptId is { } attemptId)
            {
                var attempt = await db.PipelineWorkAttempts.SingleOrDefaultAsync(a => a.Id == attemptId, ct);
                if (attempt is not null && attempt.Status == PipelineWorkAttemptStatuses.Running)
                {
                    attempt.Status = CancelledStatus;
                    attempt.FinishedAt = now;
                    attempt.ErrorMessage = "Cancelled by operator.";
                }
            }

            item.Status = CancelledStatus;
            item.UpdatedAt = now;
            item.CurrentAttemptId = null;
            item.ClaimedBy = null;
            item.ClaimedAt = null;
            item.LeaseExpiresAt = null;
            item.LastError = "Cancelled by operator.";

            await db.SaveChangesAsync(ct);
            return Results.Ok(PipelineQueueActionResult.From(item, true, "Work cancelled."));
        });

        app.MapPost("/api/pipeline/queue/purge-completed", async (
            MarketLensDbContext db,
            int? olderThanHours,
            int? take,
            CancellationToken ct) =>
        {
            var hours = Math.Clamp(olderThanHours ?? 168, 1, 24 * 90);
            var limit = Math.Clamp(take ?? 1_000, 1, MaxPurgeTake);
            var cutoff = DateTime.UtcNow.AddHours(-hours);

            var ids = await db.PipelineWorkItems
                .AsNoTracking()
                .Where(i => i.Status == PipelineWorkStatuses.Completed &&
                    i.CompletedAt != null &&
                    i.CompletedAt <= cutoff)
                .OrderBy(i => i.CompletedAt)
                .Take(limit)
                .Select(i => i.Id)
                .ToListAsync(ct);

            var deleted = ids.Count == 0
                ? 0
                : await db.PipelineWorkItems
                    .Where(i => ids.Contains(i.Id))
                    .ExecuteDeleteAsync(ct);

            return Results.Ok(new PipelineQueuePurgeResult(deleted, cutoff, limit));
        });
    }

    private static PipelineQueueItemDto ToItemDto(PipelineWorkItem item) => new(
        item.Id,
        item.WorkType,
        item.NaturalKey,
        item.Status,
        item.Priority,
        item.AvailableAt,
        item.CreatedAt,
        item.UpdatedAt,
        item.AttemptCount,
        item.MaxAttempts,
        item.CurrentAttemptId,
        item.ClaimedBy,
        item.ClaimedAt,
        item.LeaseExpiresAt,
        item.CompletedAt,
        item.DeadLetteredAt,
        item.LastError);

    private sealed record PipelineQueueCountDto(string WorkType, string Status, int Count);

    private sealed record PipelineQueueItemDto(
        Guid Id,
        string WorkType,
        string NaturalKey,
        string Status,
        int Priority,
        DateTime AvailableAt,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int AttemptCount,
        int MaxAttempts,
        Guid? CurrentAttemptId,
        string? ClaimedBy,
        DateTime? ClaimedAt,
        DateTime? LeaseExpiresAt,
        DateTime? CompletedAt,
        DateTime? DeadLetteredAt,
        string? LastError);

    private sealed record PipelineQueueAttemptDto(
        Guid Id,
        Guid WorkItemId,
        string WorkType,
        string NaturalKey,
        int AttemptNumber,
        string Status,
        string WorkerId,
        DateTime StartedAt,
        DateTime LeaseExpiresAt,
        DateTime? FinishedAt,
        double? DurationMs,
        string? ErrorMessage);

    private sealed record PipelineQueueSummaryDto(
        IReadOnlyList<PipelineQueueCountDto> Counts,
        PipelineQueueItemDto? OldestPending,
        IReadOnlyList<PipelineQueueItemDto> RecentDeadLetters,
        IReadOnlyList<PipelineQueueAttemptDto> RecentErrors);

    private sealed record PipelineQueueItemsDto(IReadOnlyList<PipelineQueueItemDto> Items);

    private sealed record PipelineQueueAttemptsDto(IReadOnlyList<PipelineQueueAttemptDto> Attempts);

    private sealed record PipelineQueuePurgeResult(int Deleted, DateTime Cutoff, int Limit);

    private sealed record PipelineQueueActionResult(Guid Id, string Status, bool Changed, string Message)
    {
        public static PipelineQueueActionResult From(PipelineWorkItem item, bool changed, string message)
            => new(item.Id, item.Status, changed, message);
    }
}
