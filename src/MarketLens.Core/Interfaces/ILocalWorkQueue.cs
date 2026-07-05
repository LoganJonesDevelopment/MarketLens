using MarketLens.Core.Entities;

namespace MarketLens.Core.Interfaces;

public sealed record EnqueueWorkRequest(
    string WorkType,
    string NaturalKey,
    string PayloadJson = "{}",
    int Priority = 0,
    DateTime? AvailableAt = null,
    int MaxAttempts = 3);

public sealed record ClaimedPipelineWork(
    PipelineWorkItem Item,
    PipelineWorkAttempt Attempt);

public interface ILocalWorkQueue
{
    Task<PipelineWorkItem> EnqueueAsync(EnqueueWorkRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimedPipelineWork>> ClaimBatchAsync(
        string workType,
        int batchSize,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteAsync(Guid attemptId, CancellationToken cancellationToken = default);

    Task<bool> FailAsync(
        Guid attemptId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredLeasesAsync(DateTime utcNow, CancellationToken cancellationToken = default);
}
