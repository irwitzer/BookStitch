using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectCatalogIndex(IReadOnlyList<ProjectIndexItem> Items,
    IReadOnlyList<string> ProjectFolders, IReadOnlyList<DamagedProjectInfo> ReadFailures);

public sealed record ProjectCatalogSnapshot(string WorkingRoot, int RetentionDays, long Generation,
    DateTime CreatedUtc, IReadOnlyList<ProjectIndexItem> ProjectIndexItems,
    IReadOnlyList<DamagedProjectInfo> DamageInformation, bool DamageComplete, bool IsDirty);

/// <summary>Browser projections only. Never stores or builds a ResumePlan.</summary>
public sealed class ProjectCatalog : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<string, int, CancellationToken, ProjectCatalogIndex> _index;
    private readonly Func<string, ProjectCatalogIndex, CancellationToken, IReadOnlyList<DamagedProjectInfo>> _damage;
    private readonly CancellationTokenSource _lifetime = new();
    private ProjectCatalogSnapshot? _snapshot;
    private Task _worker = Task.CompletedTask;
    private Task<ProjectCatalogSnapshot>? _indexReady;
    private string _root = "";
    private int _retention;
    private long _generation;
    private long _workerGeneration;
    private bool _dirty = true;

    public ProjectCatalog(Func<string, int, CancellationToken, ProjectCatalogIndex> index,
        Func<string, ProjectCatalogIndex, CancellationToken, IReadOnlyList<DamagedProjectInfo>> damage)
    { _index = index; _damage = damage; }

    public Func<string, PerformanceOperationScope>? StartOperation { get; set; }
    public Func<Guid, string, PerformancePhaseScope>? StartPhase { get; set; }
    public event EventHandler? SnapshotChanged;
    public ProjectCatalogSnapshot? Snapshot { get { lock (_gate) return _snapshot; } }

    public void MarkDirty()
    {
        lock (_gate)
        {
            _dirty = true;
            ++_generation;
            if (_snapshot is not null) _snapshot = _snapshot with { IsDirty = true };
        }
    }

    public Task<ProjectCatalogSnapshot> RefreshAsync(string root, int retentionDays, bool force = false)
    {
        lock (_gate)
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            root = System.IO.Path.GetFullPath(root);
            ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);
            if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase) || retentionDays != _retention)
            { _root = root; _retention = retentionDays; _dirty = true; ++_generation; _snapshot = null; }
            // Explicit refresh shares a compatible running scan too.
            if (!_worker.IsCompleted)
            {
                if (_workerGeneration == _generation) return _indexReady!;
                return RefreshAfterWorkerAsync(_worker, root, retentionDays);
            }
            if (!force && !_dirty && _snapshot is not null) return Task.FromResult(_snapshot);
            if (force && !_dirty) ++_generation;
            var generation = _generation;
            _workerGeneration = generation;
            var ready = new TaskCompletionSource<ProjectCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            _indexReady = ready.Task;
            _worker = Task.Run(() => RefreshCore(root, retentionDays, generation, ready));
            return ready.Task;
        }
    }

    private async Task<ProjectCatalogSnapshot> RefreshAfterWorkerAsync(Task worker, string root, int retention)
    {
        await worker.ConfigureAwait(false);
        Task<ProjectCatalogSnapshot> refresh;
        lock (_gate)
        {
            if (!string.Equals(root, _root, StringComparison.OrdinalIgnoreCase) || retention != _retention)
                throw new OperationCanceledException("Catalog request was superseded by another working root.");
            refresh = RefreshAsync(root, retention);
        }
        return await refresh.ConfigureAwait(false);
    }

    public async Task<ProjectCatalogSnapshot?> WaitForDamageAsync(long generation, CancellationToken token)
    {
        Task worker;
        lock (_gate) worker = _worker;
        await worker.WaitAsync(token).ConfigureAwait(false);
        var snapshot = Snapshot;
        return snapshot?.Generation == generation && snapshot.DamageComplete && !snapshot.IsDirty ? snapshot : null;
    }

    private void RefreshCore(string root, int retention, long generation, TaskCompletionSource<ProjectCatalogSnapshot> ready)
    {
        using var operation = StartOperation?.Invoke("project.catalog.refresh") ?? default;
        try
        {
            var data = Measure(operation.OperationId, "project.catalog.index", () => _index(root, retention, _lifetime.Token));
            var snapshot = new ProjectCatalogSnapshot(root, retention, generation, DateTime.UtcNow,
                Array.AsReadOnly(data.Items.ToArray()), Array.AsReadOnly(data.ReadFailures.ToArray()), false, false);
            Publish(snapshot);
            ready.TrySetResult(snapshot);
            var damage = Measure(operation.OperationId, "project.catalog.damage", () => _damage(root, data, _lifetime.Token));
            Publish(snapshot with { DamageInformation = Array.AsReadOnly(damage.ToArray()), DamageComplete = true });
            operation.Complete();
        }
        catch (OperationCanceledException) { ready.TrySetCanceled(); operation.Cancel(); }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _dirty = true;
                    ++_generation;
                    if (_snapshot is not null) _snapshot = _snapshot with { IsDirty = true };
                }
            }
            ready.TrySetException(error);
            operation.Fail(error);
            System.Diagnostics.Trace.TraceWarning("Project catalog refresh failed: {0}", error.Message);
        }
    }

    private T Measure<T>(Guid operation, string name, Func<T> action)
    {
        using var phase = StartPhase?.Invoke(operation, name) ?? default;
        try { var result = action(); phase.Complete(); return result; }
        catch (OperationCanceledException) { phase.Cancel(); throw; }
        catch (Exception error) { phase.Fail(error); throw; }
    }

    private void Publish(ProjectCatalogSnapshot snapshot)
    {
        lock (_gate)
        {
            if (snapshot.Generation != _generation || _lifetime.IsCancellationRequested) return;
            _snapshot = snapshot;
            _dirty = false;
        }
        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _lifetime.Cancel();
}
