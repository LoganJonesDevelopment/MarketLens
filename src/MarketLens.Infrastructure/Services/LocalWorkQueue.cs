using MarketLens.Core.Domain;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketLens.Infrastructure.Services;

public class LocalWorkQueue(MarketLensDbContext db) : ILocalWorkQueue
{
    private const int MaxErrorLength = 2048;

    public async Task<PipelineWorkItem> EnqueueAsync(
        EnqueueWorkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NaturalKey);

        var now = DateTime.UtcNow;
        var workType = request.WorkType.Trim();
        var naturalKey = request.NaturalKey.Trim();

        var existing = await db.PipelineWorkItems
            .FirstOrDefaultAsync(
                i => i.WorkType == workType &&
                     i.NaturalKey == naturalKey &&
                     (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var item = new PipelineWorkItem
        {
            Id = Guid.NewGuid(),
            WorkType = workType,
            NaturalKey = naturalKey,
            PayloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson,
            Status = PipelineWorkStatuses.Queued,
            Priority = request.Priority,
            AvailableAt = request.AvailableAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
            MaxAttempts = Math.Max(request.MaxAttempts, 1),
        };

        db.PipelineWorkItems.Add(item);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return item;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(item).State = EntityState.Detached;
            return await db.PipelineWorkItems.SingleAsync(
                i => i.WorkType == workType &&
                     i.NaturalKey == naturalKey &&
                     (i.Status == PipelineWorkStatuses.Queued || i.Status == PipelineWorkStatuses.Running),
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ClaimedPipelineWork>> ClaimBatchAsync(
        string workType,
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workType);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        if (batchSize <= 0) return [];
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        var now = DateTime.UtcNow;
        var leaseExpiresAt = now.Add(leaseDuration);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var items = await db.PipelineWorkItems
            .FromSqlInterpolated($"""
                SELECT *
                FROM pipeline_work_items
                WHERE "WorkType" = {workType}
                  AND "Status" = {PipelineWorkStatuses.Queued}
                  AND "AvailableAt" <= {now}
                  AND "AttemptCount" < "MaxAttempts"
                ORDER BY "Priority" DESC, "AvailableAt", "CreatedAt"
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var claimed = new List<ClaimedPipelineWork>(items.Count);

        foreach (var item in items)
        {
            item.AttemptCount++;
            item.Status = PipelineWorkStatuses.Running;
            item.ClaimedBy = workerId;
            item.ClaimedAt = now;
            item.LeaseExpiresAt = leaseExpiresAt;
            item.UpdatedAt = now;
            item.LastError = null;

            var attempt = new PipelineWorkAttempt
            {
                Id = Guid.NewGuid(),
                WorkItemId = item.Id,
                AttemptNumber = item.AttemptCount,
                Status = PipelineWorkAttemptStatuses.Running,
                WorkerId = workerId,
                StartedAt = now,
                LeaseExpiresAt = leaseExpiresAt,
            };

            item.CurrentAttemptId = attempt.Id;
            db.PipelineWorkAttempts.Add(attempt);
            claimed.Add(new ClaimedPipelineWork(item, attempt));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return claimed;
    }

    public async Task<bool> CompleteAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.PipelineWorkAttempts
            .Include(a => a.WorkItem)
            .SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        if (attempt is null || !IsCurrentRunningAttempt(attempt))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        attempt.Status = PipelineWorkAttemptStatuses.Succeeded;
        attempt.FinishedAt = now;

        var item = attempt.WorkItem;
        item.Status = PipelineWorkStatuses.Completed;
        item.UpdatedAt = now;
        item.CompletedAt = now;
        item.ClaimedBy = null;
        item.ClaimedAt = null;
        item.LeaseExpiresAt = null;
        item.LastError = null;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> FailAsync(
        Guid attemptId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.PipelineWorkAttempts
            .Include(a => a.WorkItem)
            .SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        if (attempt is null || !IsCurrentRunningAttempt(attempt))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var item = attempt.WorkItem;
        var truncatedError = Truncate(errorMessage);

        attempt.Status = PipelineWorkAttemptStatuses.Failed;
        attempt.FinishedAt = now;
        attempt.ErrorMessage = truncatedError;

        item.UpdatedAt = now;
        item.ClaimedBy = null;
        item.ClaimedAt = null;
        item.LeaseExpiresAt = null;
        item.LastError = truncatedError;

        if (item.AttemptCount >= item.MaxAttempts)
        {
            item.Status = PipelineWorkStatuses.DeadLetter;
            item.DeadLetteredAt = now;
            item.AvailableAt = now;
        }
        else
        {
            item.Status = PipelineWorkStatuses.Queued;
            item.AvailableAt = now.Add(retryAfter ?? DefaultRetryDelay(item.AttemptCount));
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RecoverExpiredLeasesAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var expiredItems = await db.PipelineWorkItems
            .Where(i =>
                i.Status == PipelineWorkStatuses.Running &&
                i.LeaseExpiresAt != null &&
                i.LeaseExpiresAt <= utcNow)
            .OrderBy(i => i.LeaseExpiresAt)
            .ToListAsync(cancellationToken);

        if (expiredItems.Count == 0) return 0;

        var attemptIds = expiredItems
            .Where(i => i.CurrentAttemptId is not null)
            .Select(i => i.CurrentAttemptId!.Value)
            .ToArray();

        var attempts = await db.PipelineWorkAttempts
            .Where(a => attemptIds.Contains(a.Id) && a.Status == PipelineWorkAttemptStatuses.Running)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var item in expiredItems)
        {
            if (item.CurrentAttemptId is { } attemptId && attempts.TryGetValue(attemptId, out var attempt))
            {
                attempt.Status = PipelineWorkAttemptStatuses.Expired;
                attempt.FinishedAt = utcNow;
                attempt.ErrorMessage = "Lease expired.";
            }

            item.UpdatedAt = utcNow;
            item.ClaimedBy = null;
            item.ClaimedAt = null;
            item.LeaseExpiresAt = null;
            item.LastError = "Lease expired.";

            if (item.AttemptCount >= item.MaxAttempts)
            {
                item.Status = PipelineWorkStatuses.DeadLetter;
                item.DeadLetteredAt = utcNow;
                item.AvailableAt = utcNow;
            }
            else
            {
                item.Status = PipelineWorkStatuses.Queued;
                item.AvailableAt = utcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return expiredItems.Count;
    }

    private static bool IsCurrentRunningAttempt(PipelineWorkAttempt attempt)
        => attempt.Status == PipelineWorkAttemptStatuses.Running &&
           attempt.WorkItem.Status == PipelineWorkStatuses.Running &&
           attempt.WorkItem.CurrentAttemptId == attempt.Id;

    private static TimeSpan DefaultRetryDelay(int completedAttemptCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Max(completedAttemptCount - 1, 0)) * 15);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Truncate(string value)
        => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
