using System.Text.Json;
using MarketLens.Core.Entities;
using MarketLens.Core.Interfaces;
using MarketLens.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketLens.Infrastructure.Services;

public class SourceCursorStore(MarketLensDbContext db) : ISourceCursorStore
{
    private const int MaxErrorLength = 2048;

    public Task<SourceCursorState?> GetAsync(
        string sourceName,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRequired(sourceName, nameof(sourceName));
        var key = NormalizeRequired(sourceKey, nameof(sourceKey));

        return db.SourceCursorStates
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.SourceName == source && s.SourceKey == key, cancellationToken);
    }

    public async Task<IReadOnlyList<SourceCursorState>> ListEligibleAsync(
        DateTime utcNow,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0) return [];

        return await db.SourceCursorStates
            .AsNoTracking()
            .Where(s => s.NextEligibleRunAt == null || s.NextEligibleRunAt <= utcNow)
            .OrderBy(s => s.NextEligibleRunAt == null ? 0 : 1)
            .ThenBy(s => s.NextEligibleRunAt)
            .ThenBy(s => s.SourceName)
            .ThenBy(s => s.SourceKey)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<SourceCursorState> MarkStartedAsync(
        string sourceName,
        string sourceKey,
        DateTime? nextEligibleRunAt = null,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRequired(sourceName, nameof(sourceName));
        var key = NormalizeRequired(sourceKey, nameof(sourceKey));
        var now = DateTime.UtcNow;
        var state = await GetOrCreateTrackedAsync(source, key, now, cancellationToken);

        state.LastStartedAt = now;
        state.NextEligibleRunAt = nextEligibleRunAt;
        state.UpdatedAt = now;

        await SaveWithUniqueRetryAsync(state, source, key, cancellationToken);
        return state;
    }

    public async Task<SourceCursorState> MarkSucceededAsync(
        SourceCursorAdvance advance,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRequired(advance.SourceName, nameof(advance.SourceName));
        var key = NormalizeRequired(advance.SourceKey, nameof(advance.SourceKey));
        var now = DateTime.UtcNow;
        var state = await GetOrCreateTrackedAsync(source, key, now, cancellationToken);

        if (!string.IsNullOrWhiteSpace(advance.CursorJson))
        {
            state.CursorJson = NormalizeJson(advance.CursorJson);
        }

        state.LastSucceededAt = now;
        state.LastItemTimestamp = advance.LastItemTimestamp ?? state.LastItemTimestamp;
        state.LastItemId = NormalizeOptional(advance.LastItemId) ?? state.LastItemId;
        state.ConsecutiveFailures = 0;
        state.NextEligibleRunAt = advance.NextEligibleRunAt;
        state.LastError = null;
        state.UpdatedAt = now;

        await SaveWithUniqueRetryAsync(state, source, key, cancellationToken);
        return state;
    }

    public async Task<SourceCursorState> MarkFailedAsync(
        SourceCursorFailure failure,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRequired(failure.SourceName, nameof(failure.SourceName));
        var key = NormalizeRequired(failure.SourceKey, nameof(failure.SourceKey));
        var now = DateTime.UtcNow;
        var state = await GetOrCreateTrackedAsync(source, key, now, cancellationToken);

        state.LastFailedAt = now;
        state.ConsecutiveFailures++;
        state.NextEligibleRunAt = failure.NextEligibleRunAt;
        state.LastError = Truncate(failure.Error, MaxErrorLength);
        state.UpdatedAt = now;

        await SaveWithUniqueRetryAsync(state, source, key, cancellationToken);
        return state;
    }

    private async Task<SourceCursorState> GetOrCreateTrackedAsync(
        string sourceName,
        string sourceKey,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await db.SourceCursorStates
            .SingleOrDefaultAsync(s => s.SourceName == sourceName && s.SourceKey == sourceKey, cancellationToken);

        if (existing is not null) return existing;

        var state = new SourceCursorState
        {
            Id = Guid.NewGuid(),
            SourceName = sourceName,
            SourceKey = sourceKey,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SourceCursorStates.Add(state);
        return state;
    }

    private async Task SaveWithUniqueRetryAsync(
        SourceCursorState state,
        string sourceName,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && db.Entry(state).State == EntityState.Added)
        {
            db.Entry(state).State = EntityState.Detached;
            var existing = await db.SourceCursorStates.SingleAsync(
                s => s.SourceName == sourceName && s.SourceKey == sourceKey,
                cancellationToken);

            existing.LastStartedAt = state.LastStartedAt ?? existing.LastStartedAt;
            existing.LastSucceededAt = state.LastSucceededAt ?? existing.LastSucceededAt;
            existing.LastFailedAt = state.LastFailedAt ?? existing.LastFailedAt;
            existing.LastItemTimestamp = state.LastItemTimestamp ?? existing.LastItemTimestamp;
            existing.LastItemId = state.LastItemId ?? existing.LastItemId;
            if (state.LastSucceededAt is not null && state.LastSucceededAt >= (existing.LastSucceededAt ?? DateTime.MinValue))
            {
                existing.ConsecutiveFailures = 0;
            }
            else if (state.LastFailedAt is not null && state.LastFailedAt >= (existing.LastFailedAt ?? DateTime.MinValue))
            {
                existing.ConsecutiveFailures++;
            }
            existing.NextEligibleRunAt = state.NextEligibleRunAt;
            existing.LastError = state.LastError;
            existing.UpdatedAt = state.UpdatedAt;
            if (state.CursorJson != "{}") existing.CursorJson = state.CursorJson;

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string NormalizeJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.GetRawText();
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
