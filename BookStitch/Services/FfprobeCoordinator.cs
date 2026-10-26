namespace BookStitch.Services;

public sealed class FfprobeCoordinator
{
    private readonly SemaphoreSlim _semaphore;

    public FfprobeCoordinator()
        : this(FfprobeConcurrencyPolicy.MaximumConcurrency)
    {
    }

    internal FfprobeCoordinator(int maximumConcurrency)
    {
        _semaphore = new SemaphoreSlim(
            Math.Clamp(maximumConcurrency, 1, FfprobeConcurrencyPolicy.MaximumConcurrency));
    }

    public async Task<IDisposable> EnterAsync(CancellationToken token)
    {
        await _semaphore.WaitAsync(token).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _semaphore.Release();
        }
    }
}
