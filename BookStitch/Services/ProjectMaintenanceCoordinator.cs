namespace BookStitch.Services;

[Flags]
public enum MaintenanceSafeSpace { None = 0, GlobalSettings = 1, Metadata = 2, Complete = 4 }

public sealed record MaintenanceEligibility(MaintenanceSafeSpace SafeSpace, bool ActiveWork,
    bool WorkspaceTransition, bool ProjectOperation)
{
    public bool CanRun => SafeSpace != MaintenanceSafeSpace.None && !ActiveWork && !WorkspaceTransition && !ProjectOperation;
}

public sealed record ProjectMaintenanceRequest(string WorkingRoot, string CoversFolder,
    int RetentionDays, string RetentionMode, DateTime NowUtc);

/// <summary>Only explicit product-safe spaces authorize automatic maintenance.</summary>
public sealed class ProjectMaintenanceCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<ProjectMaintenanceRequest, CancellationToken, ProjectCleanupResult> _projects;
    private readonly Func<ProjectMaintenanceRequest, CancellationToken, IReadOnlyList<string>> _covers;
    private MaintenanceEligibility _eligibility = new(MaintenanceSafeSpace.None, false, false, false);
    private CancellationTokenSource? _cancellation;
    private Task<bool> _running = Task.FromResult(false);
    private DateTime? _lastAttemptUtc;
    private bool _disposed;

    public ProjectMaintenanceCoordinator(
        Func<ProjectMaintenanceRequest, CancellationToken, ProjectCleanupResult> projects,
        Func<ProjectMaintenanceRequest, CancellationToken, IReadOnlyList<string>> covers)
    { _projects = projects; _covers = covers; }

    public Func<string, PerformanceOperationScope>? StartOperation { get; set; }
    public Func<Guid, string, PerformancePhaseScope>? StartPhase { get; set; }
    public Action<string>? LogFailure { get; set; }
    public bool IsRunning { get { lock (_gate) return !_running.IsCompleted; } }

    public void SetEligibility(MaintenanceEligibility eligibility)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _eligibility = eligibility;
            if (!eligibility.CanRun) _cancellation?.Cancel();
        }
    }

    public bool IsDue(DateTime? persistedUtc, DateTime nowUtc)
    {
        lock (_gate)
        {
            var last = persistedUtc;
            if (_lastAttemptUtc.HasValue && (!last.HasValue || _lastAttemptUtc.Value > last.Value))
                last = _lastAttemptUtc;
            return !_disposed && _running.IsCompleted && (last is null || nowUtc - last.Value >= TimeSpan.FromHours(24));
        }
    }

    // Called by the UI owner: persist the reservation before starting any worker.
    // Even a cancelled run counts as today's attempt; repeated safe-space entry cannot spam cleanup.
    public Task<bool> TryStartAsync(ProjectMaintenanceRequest request, DateTime? persistedUtc,
        Func<DateTime, bool> persistTimestamp)
    {
        lock (_gate)
        {
            if (!_eligibility.CanRun || !IsDue(persistedUtc, request.NowUtc)) return Task.FromResult(false);
            if (!persistTimestamp(request.NowUtc)) return Task.FromResult(false);
            _lastAttemptUtc = request.NowUtc;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _running = Task.Run(() => Run(request, token));
            return _running;
        }
    }

    private bool Run(ProjectMaintenanceRequest request, CancellationToken token)
    {
        using var operation = StartOperation?.Invoke("maintenance.total") ?? default;
        try
        {
            token.ThrowIfCancellationRequested();
            // Never means never delete projects, including a stored numeric zero.
            if (!ProjectRetentionPolicyService.IsNever(request.RetentionMode))
            {
                var result = Measure(operation.OperationId, "maintenance.project-cleanup", () => _projects(request, token));
                foreach (var failure in result.Failures) LogFailure?.Invoke(failure);
            }
            token.ThrowIfCancellationRequested();
            var failures = Measure(operation.OperationId, "maintenance.cover-cleanup", () => _covers(request, token));
            foreach (var failure in failures) LogFailure?.Invoke(failure);
            token.ThrowIfCancellationRequested();
            operation.Complete();
            return true;
        }
        catch (OperationCanceledException) { operation.Cancel(); return false; }
        catch (Exception error) { operation.Fail(error); LogFailure?.Invoke(error.Message); return false; }
    }

    private T Measure<T>(Guid operation, string name, Func<T> action)
    {
        using var phase = StartPhase?.Invoke(operation, name) ?? default;
        try { var result = action(); phase.Complete(); return result; }
        catch (OperationCanceledException) { phase.Cancel(); throw; }
        catch (Exception error) { phase.Fail(error); throw; }
    }

    public Task StopAsync()
    {
        lock (_gate) { _cancellation?.Cancel(); return _running; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _cancellation?.Cancel();
            if (_running.IsCompleted) { _cancellation?.Dispose(); _cancellation = null; }
        }
    }
}
