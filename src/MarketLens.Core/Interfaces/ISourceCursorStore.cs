using MarketLens.Core.Entities;

namespace MarketLens.Core.Interfaces;

public sealed record SourceCursorAdvance(
    string SourceName,
    string SourceKey,
    string? CursorJson = null,
    DateTime? LastItemTimestamp = null,
    string? LastItemId = null,
    DateTime? NextEligibleRunAt = null);

public sealed record SourceCursorFailure(
    string SourceName,
    string SourceKey,
    string Error,
    DateTime? NextEligibleRunAt = null);

public interface ISourceCursorStore
{
    Task<SourceCursorState?> GetAsync(
        string sourceName,
        string sourceKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceCursorState>> ListEligibleAsync(
        DateTime utcNow,
        int take,
        CancellationToken cancellationToken = default);

    Task<SourceCursorState> MarkStartedAsync(
        string sourceName,
        string sourceKey,
        DateTime? nextEligibleRunAt = null,
        CancellationToken cancellationToken = default);

    Task<SourceCursorState> MarkSucceededAsync(
        SourceCursorAdvance advance,
        CancellationToken cancellationToken = default);

    Task<SourceCursorState> MarkFailedAsync(
        SourceCursorFailure failure,
        CancellationToken cancellationToken = default);
}
