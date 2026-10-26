using System.Collections.Concurrent;

namespace BookStitch.Services;

/// <summary>Owns the application mutex on its startup/exit thread; crashes abandon it for the next process.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> LocalOwners = new();
    private readonly string _name;
    private Mutex? _mutex;
    private SingleInstanceService(string name, Mutex mutex) { _name = name; _mutex = mutex; }

    public static SingleInstanceService? TryAcquire(string name = @"Global\BookStitch.Application.Singleton")
    {
        if (!LocalOwners.TryAdd(name, 0)) return null;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new SingleInstanceService(name, mutex);
            mutex.Dispose();
            LocalOwners.TryRemove(name, out _);
            return null;
        }
        catch
        {
            mutex?.Dispose();
            LocalOwners.TryRemove(name, out _);
            throw;
        }
    }

    // Named mutex ownership is thread-affine. WPF startup and exit both run on the dispatcher thread.
    public void Dispose()
    {
        if (_mutex is null) return;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _mutex = null;
        LocalOwners.TryRemove(_name, out _);
    }
}
