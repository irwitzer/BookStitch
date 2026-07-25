using BookStitch.Models;

namespace BookStitch.Services;

public sealed class WorkflowStatusCoordinator
{
    private readonly object _sync = new();
    private readonly WorkflowStatusFormatter _formatter;
    private Guid _activeOperationId;
    private WorkflowStatusSnapshot _snapshot = WorkflowStatusSnapshot.Empty;

    public WorkflowStatusCoordinator(WorkflowStatusFormatter? formatter = null)
    {
        _formatter = formatter ?? new WorkflowStatusFormatter();
    }

    public event EventHandler<WorkflowStatusViewState>? ViewStateChanged;

    public Guid ActiveOperationId
    {
        get
        {
            lock (_sync)
                return _activeOperationId;
        }
    }

    public WorkflowStatusSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    public WorkflowStatusViewState CurrentViewState
    {
        get
        {
            lock (_sync)
                return _formatter.Format(_snapshot);
        }
    }

    public Guid BeginOperation(string? projectId = null)
    {
        WorkflowStatusViewState viewState;
        Guid operationId;

        lock (_sync)
        {
            operationId = Guid.NewGuid();
            _activeOperationId = operationId;
            _snapshot = WorkflowStatusSnapshot.Empty with { ProjectId = projectId };
            viewState = _formatter.Format(_snapshot);
        }

        ViewStateChanged?.Invoke(this, viewState);
        return operationId;
    }

    public bool Publish(Guid operationId, WorkflowStatusSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        WorkflowStatusViewState viewState;
        lock (_sync)
        {
            if (operationId == Guid.Empty || operationId != _activeOperationId)
                return false;

            _snapshot = snapshot;
            viewState = _formatter.Format(snapshot);
        }

        ViewStateChanged?.Invoke(this, viewState);
        return true;
    }

    public bool Update(Guid operationId, Func<WorkflowStatusSnapshot, WorkflowStatusSnapshot> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        WorkflowStatusViewState viewState;
        lock (_sync)
        {
            if (operationId == Guid.Empty || operationId != _activeOperationId)
                return false;

            _snapshot = update(_snapshot)
                ?? throw new InvalidOperationException("Status update must return a snapshot.");
            viewState = _formatter.Format(_snapshot);
        }

        ViewStateChanged?.Invoke(this, viewState);
        return true;
    }

    public bool EndOperation(Guid operationId)
    {
        lock (_sync)
        {
            if (operationId == Guid.Empty || operationId != _activeOperationId)
                return false;

            _activeOperationId = Guid.Empty;
            return true;
        }
    }
}
