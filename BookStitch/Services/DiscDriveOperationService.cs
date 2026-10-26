using System.Collections.Concurrent;
using System.IO;

namespace BookStitch.Services;

/// <summary>Owns per-drive gates across service instances, including native calls that cannot be interrupted.</summary>
internal static class DiscDriveOperationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    [ThreadStatic] private static HashSet<string>? _heldRoots;
    [ThreadStatic] private static CancellationToken _currentCancellationToken;
    internal static CancellationToken CurrentCancellationToken => _currentCancellationToken;

    private static string Key(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return (Path.GetPathRoot(path.Trim()) ?? path.Trim()).TrimEnd('\\', '/'); }
        catch (ArgumentException) { return path.Trim(); }
    }

    public static async Task<IDisposable> AcquireAsync(string? root, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(Key(root), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    public static T Execute<T>(string? root, Func<T> action, CancellationToken cancellationToken = default)
    {
        var key = Key(root);
        var token = cancellationToken.CanBeCanceled ? cancellationToken : _currentCancellationToken;
        token.ThrowIfCancellationRequested();
        if (_heldRoots?.Contains(key) == true) return action();
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        gate.Wait(token);
        var previousToken = _currentCancellationToken;
        (_heldRoots ??= new(StringComparer.OrdinalIgnoreCase)).Add(key);
        _currentCancellationToken = token;
        try
        {
            token.ThrowIfCancellationRequested();
            var result = action();
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            _currentCancellationToken = previousToken;
            _heldRoots.Remove(key);
            gate.Release();
        }
    }

    // Await the actual operation. Cancellation never reports a running native call as finished.
    public static Task<T> ExecuteAsync<T>(string? root, Func<T> action, CancellationToken cancellationToken) =>
        Task.Run(() => Execute(root, action, cancellationToken), CancellationToken.None);

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
