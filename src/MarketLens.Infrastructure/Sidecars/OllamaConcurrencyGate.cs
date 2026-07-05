namespace MarketLens.Infrastructure.Sidecars;

// Bounds GPU load from Ollama generations. A single qwen3 decode alone maxes the card,
// so serializing is not enough on its own: with a backlog of long jobs (idea memos,
// thesis plans) the GPU stays pinned and the fan roars. Two levers:
//   maxConcurrency    - how many generations may run at once (1 = no overlap).
//   minIntervalSeconds- minimum idle gap enforced AFTER each job before the next may
//                       start, so the card cools between bursts (duty-cycle cap).
public sealed class OllamaConcurrencyGate(int maxConcurrency = 1, int minIntervalSeconds = 0)
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(Math.Max(0, minIntervalSeconds));
    private readonly object _lock = new();
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_minInterval > TimeSpan.Zero)
            {
                TimeSpan wait;
                lock (_lock) { wait = _nextAllowedUtc - DateTime.UtcNow; }
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken);
            }
        }
        catch
        {
            _semaphore.Release();
            throw;
        }

        return new Lease(this);
    }

    private void Release()
    {
        if (_minInterval > TimeSpan.Zero)
            lock (_lock) { _nextAllowedUtc = DateTime.UtcNow + _minInterval; }

        _semaphore.Release();
    }

    private sealed class Lease(OllamaConcurrencyGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
