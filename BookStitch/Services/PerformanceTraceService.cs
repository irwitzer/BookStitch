using BookStitch.Models;

namespace BookStitch.Services;

public sealed record PerformanceSessionStartContext
{
    public PerformanceSessionStartReason StartReason { get; init; } = PerformanceSessionStartReason.ApplicationStartup;

    public string ApplicationVersion { get; init; } = string.Empty;

    public string? BuildIdentifier { get; init; }

    public string? SourceRevision { get; init; }

    public string? BuildConfiguration { get; init; }

    public string? SelectedParallelJobs { get; init; }
}

public readonly struct PerformanceOperationScope : IDisposable
{
    private readonly PerformanceTraceService? _owner;

    internal PerformanceOperationScope(PerformanceTraceService owner, Guid operationId)
    {
        _owner = owner;
        OperationId = operationId;
    }

    public Guid OperationId { get; }

    public bool IsActive => _owner is not null && OperationId != Guid.Empty;

    public void Complete() => Finish(PerformanceOutcome.Success);

    public void Fail(Exception exception) => Finish(PerformanceOutcome.Failed, exception);

    public void Cancel() => Finish(PerformanceOutcome.Cancelled);

    public void Pause() => Finish(PerformanceOutcome.Paused);

    public void Cancel(bool pauseRequested) { if (pauseRequested) Pause(); else Cancel(); }

    public void Dispose() => Finish(PerformanceOutcome.Incomplete);

    private void Finish(PerformanceOutcome outcome, Exception? exception = null)
    {
        try { _owner?.CompleteOperation(OperationId, outcome, exception); }
        catch { /* Trace failure must never change workflow completion. */ }
    }
}

public readonly struct PerformancePhaseScope : IDisposable
{
    private readonly PerformanceTraceService? _owner;

    internal PerformancePhaseScope(PerformanceTraceService owner, Guid phaseId)
    {
        _owner = owner;
        PhaseId = phaseId;
    }

    public Guid PhaseId { get; }

    public bool IsActive => _owner is not null && PhaseId != Guid.Empty;

    public void Complete() => Finish(PerformanceOutcome.Success);

    public void Fail(Exception exception) => Finish(PerformanceOutcome.Failed, exception);

    public void Cancel() => Finish(PerformanceOutcome.Cancelled);

    public void Pause() => Finish(PerformanceOutcome.Paused);

    public void Cancel(bool pauseRequested) { if (pauseRequested) Pause(); else Cancel(); }

    public void Dispose() => Finish(PerformanceOutcome.Incomplete);

    private void Finish(PerformanceOutcome outcome, Exception? exception = null)
    {
        try { _owner?.CompletePhase(PhaseId, outcome, exception); }
        catch { /* Trace failure must never change workflow completion. */ }
    }
}

public sealed record PerformanceExternalProcessContext(
    PerformanceTraceService TraceService,
    Guid OperationId,
    Guid? PhaseId = null,
    string? Purpose = null)
{
    public PerformanceExternalProcessScope Start(string tool, string? defaultPurpose = null)
    {
        try
        {
            var resolvedPurpose = string.IsNullOrWhiteSpace(Purpose)
                ? defaultPurpose
                : Purpose;

            return TraceService.StartExternalProcess(
                OperationId,
                PhaseId,
                tool,
                resolvedPurpose);
        }
        catch
        {
            // Performance instrumentation must never alter the business operation.
            return default;
        }
    }
}

public readonly struct PerformanceExternalProcessScope : IDisposable
{
    private readonly PerformanceTraceService? _owner;

    internal PerformanceExternalProcessScope(
        PerformanceTraceService owner,
        Guid invocationId)
    {
        _owner = owner;
        InvocationId = invocationId;
    }

    public Guid InvocationId { get; }

    public bool IsActive => _owner is not null && InvocationId != Guid.Empty;

    public void Complete(int exitCode) =>
        CompleteCore(exitCode == 0
            ? PerformanceOutcome.Success
            : PerformanceOutcome.Failed);

    public void Fail() => CompleteCore(PerformanceOutcome.Failed);

    public void Cancel() => CompleteCore(PerformanceOutcome.Cancelled);

    public void Dispose() => CompleteCore(PerformanceOutcome.Incomplete);

    private void CompleteCore(PerformanceOutcome outcome)
    {
        try
        {
            _owner?.CompleteExternalProcess(InvocationId, outcome);
        }
        catch
        {
            // Performance instrumentation must never alter the business operation.
        }
    }
}

/// <summary>
/// In-memory, thread-safe performance tracing engine. It deliberately contains no
/// persistence, UI, hardware probing or workflow-specific behavior.
/// </summary>
public sealed class PerformanceTraceService
{
    public void RecordParallelism(Guid operationId, string requested, int effectiveWorkerLimit,
        string prefix = "", Guid? phaseId = null)
    {
        try
        {
            var numeric = int.TryParse(requested, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value);
            RecordMetric(operationId, prefix + "RequestedParallelJobs", numeric ? value : 0, numeric ? "jobs" : "Auto");
            RecordMetric(operationId, prefix + "EffectiveWorkerLimit", effectiveWorkerLimit, "workers");
            if (phaseId.HasValue)
            {
                RecordPhaseMetric(phaseId.Value, prefix + "RequestedParallelJobs", numeric ? value : 0, numeric ? "jobs" : "Auto");
                RecordPhaseMetric(phaseId.Value, prefix + "EffectiveWorkerLimit", effectiveWorkerLimit, "workers");
            }
        }
        catch { /* Performance context must never change worker scheduling. */ }
    }

    internal PerformanceMeasurement? StartMeasurement(string name)
    {
        if (!IsEnabled) return null;
        lock (_syncRoot)
            return _session is null ? null : new PerformanceMeasurement(this, _session.SessionId, name);
    }

    internal void RecordMeasurement(Guid sessionId, string name, double elapsed,
        Dictionary<string, double> counters)
    {
        lock (_syncRoot)
        {
            if (_session is null || _session.SessionId != sessionId) return;
            if (!_session.Measurements.TryGetValue(name, out var aggregate))
            {
                aggregate = new MeasurementState(Guid.NewGuid(), _timeProvider.GetUtcNow());
                _session.Measurements.Add(name, aggregate);
            }
            aggregate.Count++;
            aggregate.Sum += elapsed;
            aggregate.Maximum = Math.Max(aggregate.Maximum, elapsed);
            foreach (var counter in counters)
                aggregate.Counters[counter.Key] = aggregate.Counters.GetValueOrDefault(counter.Key) + counter.Value;
        }
    }

    private const int MaxFailureMessageLength = 512;

    private readonly TimeProvider _timeProvider;
    private readonly Action<PerformanceJournalEvent>? _journalEventSink;
    private readonly object _syncRoot = new();
    private bool _enabled;
    private SessionState? _session;

    public PerformanceTraceService(
        bool enabled = false,
        TimeProvider? timeProvider = null,
        Action<PerformanceJournalEvent>? journalEventSink = null)
    {
        _enabled = enabled;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _journalEventSink = journalEventSink;
    }

    public bool IsEnabled => Volatile.Read(ref _enabled);

    public Guid? ActiveSessionId
    {
        get
        {
            lock (_syncRoot)
            {
                return _session?.SessionId;
            }
        }
    }

    public int ActiveScopeCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _session is null
                    ? 0
                    : _session.ActiveOperationCount +
                      _session.ActivePhaseCount +
                      _session.ActiveExternalProcessCount;
            }
        }
    }

    /// <summary>
    /// Enables or disables creation of new tracing scopes. Existing scopes can still
    /// complete after disabling so a caller can finish the active session cleanly.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        Volatile.Write(ref _enabled, enabled);
    }

    /// <summary>
    /// Starts a session if tracing is enabled and no session is already active.
    /// Optional start values allow a very early application-start timestamp to be
    /// captured before settings are loaded and adopted after the enabled flag is known.
    /// </summary>
    public bool TryStartSession(
        PerformanceSessionStartContext context,
        long? startedTimestamp = null,
        DateTimeOffset? startedUtc = null,
        Guid? sessionId = null)
    {
        if (!IsEnabled || context is null)
            return false;

        lock (_syncRoot)
        {
            if (!IsEnabled || _session is not null)
                return false;

            var resolvedStart = ResolveStart(startedTimestamp, startedUtc);

            var resolvedSessionId = sessionId.GetValueOrDefault(Guid.NewGuid());
            if (resolvedSessionId == Guid.Empty)
                return false;

            _session = new SessionState(
                resolvedSessionId,
                context,
                resolvedStart.Timestamp,
                resolvedStart.Utc);

            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.started",
                SessionId = resolvedSessionId,
                Utc = resolvedStart.Utc
            });

            return true;
        }
    }

    public PerformanceOperationScope StartOperation(
        string operationName,
        Guid? parentOperationId = null,
        PerformanceProjectType projectType = PerformanceProjectType.Unknown,
        PerformanceWorkloadSnapshot? workload = null,
        long? startedTimestamp = null,
        DateTimeOffset? startedUtc = null)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(operationName))
            return default;

        lock (_syncRoot)
        {
            if (!IsEnabled || _session is null)
                return default;

            OperationState? parent = null;
            if (parentOperationId.HasValue)
            {
                if (!_session.Operations.TryGetValue(parentOperationId.Value, out parent) || parent.IsCompleted)
                    return default;
            }

            var resolvedStart = ResolveStart(startedTimestamp, startedUtc);
            var operationId = Guid.NewGuid();
            var state = new OperationState(
                operationId,
                _session.SessionId,
                parentOperationId,
                operationName,
                projectType,
                workload ?? new PerformanceWorkloadSnapshot { ProjectType = projectType },
                resolvedStart.Timestamp,
                resolvedStart.Utc);

            _session.Operations.Add(operationId, state);
            _session.OperationOrder.Add(operationId);
            _session.ActiveOperationCount++;

            parent?.ChildOperationIds.Add(operationId);
            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "operation.started",
                SessionId = _session.SessionId,
                OperationId = operationId,
                Utc = resolvedStart.Utc,
                Name = state.Name
            });

            return new PerformanceOperationScope(this, operationId);
        }
    }

    public PerformancePhaseScope StartPhase(Guid operationId, string phaseName)
    {
        if (!IsEnabled || operationId == Guid.Empty || string.IsNullOrWhiteSpace(phaseName))
            return default;

        lock (_syncRoot)
        {
            if (!IsEnabled ||
                _session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return default;
            }

            var phaseId = Guid.NewGuid();
            var state = new PhaseState(
                phaseId,
                operationId,
                phaseName,
                _timeProvider.GetTimestamp(),
                _timeProvider.GetUtcNow());

            _session.Phases.Add(phaseId, state);
            _session.ActivePhaseCount++;
            operation.PhaseIds.Add(phaseId);
            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "phase.started",
                SessionId = _session.SessionId,
                OperationId = operationId,
                PhaseId = phaseId,
                Utc = state.StartedUtc,
                Name = state.Name
            });

            return new PerformancePhaseScope(this, phaseId);
        }
    }

    public PerformanceExternalProcessScope StartExternalProcess(
        Guid operationId,
        Guid? phaseId,
        string tool,
        string? purpose = null)
    {
        if (!IsEnabled ||
            operationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(tool))
        {
            return default;
        }

        lock (_syncRoot)
        {
            if (!IsEnabled ||
                _session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return default;
            }

            PhaseState? phase = null;
            if (phaseId.HasValue)
            {
                if (!_session.Phases.TryGetValue(phaseId.Value, out phase) ||
                    phase.IsCompleted ||
                    phase.OperationId != operationId)
                {
                    return default;
                }
            }

            var key = new ExternalProcessKey(
                NormalizeExternalProcessTool(tool),
                NormalizeExternalProcessPurpose(purpose));
            var invocationId = Guid.NewGuid();
            var invocation = new ExternalProcessInvocationState(
                invocationId,
                operationId,
                phaseId,
                key,
                _timeProvider.GetTimestamp());

            GetOrCreateExternalProcessAggregate(
                operation.ExternalProcessAggregates,
                key).Start();

            if (phase is not null)
            {
                GetOrCreateExternalProcessAggregate(
                    phase.ExternalProcessAggregates,
                    key).Start();
            }

            _session.ExternalProcesses.Add(invocationId, invocation);
            _session.ActiveExternalProcessCount++;
            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "external.started",
                SessionId = _session.SessionId,
                OperationId = operationId,
                PhaseId = phaseId,
                Utc = _timeProvider.GetUtcNow(),
                Tool = key.Tool,
                Purpose = key.Purpose
            });

            return new PerformanceExternalProcessScope(this, invocationId);
        }
    }

    public void RecordMetric(Guid operationId, string metricName, double value, string? unit = null)
    {
        if (!IsEnabled || operationId == Guid.Empty || string.IsNullOrWhiteSpace(metricName))
            return;

        lock (_syncRoot)
        {
            if (!IsEnabled ||
                _session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return;
            }

            operation.Metrics.Add(new PerformanceMetric
            {
                Name = metricName,
                Value = value,
                Unit = unit
            });
        }
    }

    public void RecordPhaseMetric(Guid phaseId, string metricName, double value, string? unit = null)
    {
        if (!IsEnabled || phaseId == Guid.Empty || string.IsNullOrWhiteSpace(metricName))
            return;

        lock (_syncRoot)
        {
            if (!IsEnabled ||
                _session is null ||
                !_session.Phases.TryGetValue(phaseId, out var phase) ||
                phase.IsCompleted)
            {
                return;
            }

            phase.Metrics.Add(new PerformanceMetric
            {
                Name = metricName,
                Value = value,
                Unit = unit
            });
        }
    }

    public void RecordDriveMeasurement(DiscDriveMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (!IsEnabled)
            return;

        lock (_syncRoot)
        {
            if (!IsEnabled || _session is null)
                return;

            Guid? driveId = null;
            try
            {
                var resolved = PerformanceDriveContextService.CreateDriveIdForPath(measurement.DriveRoot);
                if (resolved != Guid.Empty)
                    driveId = resolved;
            }
            catch
            {
            }

            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "drive.measurement",
                SessionId = _session.SessionId,
                Utc = _timeProvider.GetUtcNow(),
                StartedUtc = measurement.StartedUtc,
                ElapsedMilliseconds = measurement.ElapsedMilliseconds,
                DriveId = driveId,
                DriveReference = measurement.DriveReference,
                ProbeType = measurement.Method,
                Success = measurement.Success,
                TimedOut = measurement.TimedOut,
                Cancelled = measurement.Cancelled,
                IsReady = measurement.IsReady,
                MediumPresent = measurement.MediumPresent,
                FirstTouchInSession = measurement.IsFirstTouchInSession,
                Result = measurement.Result,
                Outcome = measurement.Cancelled
                    ? PerformanceOutcome.Cancelled
                    : measurement.Success
                        ? PerformanceOutcome.Success
                        : PerformanceOutcome.Failed
            });
        }
    }

    public void UpdateOperationWorkload(Guid operationId, PerformanceWorkloadSnapshot workload)
    {
        if (operationId == Guid.Empty || workload is null)
            return;

        lock (_syncRoot)
        {
            if (_session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return;
            }

            operation.Workload = workload;
        }
    }

    public void RecordDriveRoleReference(
        Guid operationId,
        Guid driveId,
        PerformanceDriveRole roles)
    {
        if (operationId == Guid.Empty ||
            driveId == Guid.Empty ||
            roles == PerformanceDriveRole.None)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return;
            }

            operation.DriveRoleReferences.TryGetValue(driveId, out var existingRoles);
            operation.DriveRoleReferences[driveId] = existingRoles | roles;
        }
    }

    public void SetSessionContext(
        PerformanceEnvironmentSnapshot? environment,
        PerformanceToolSnapshot? ffmpegContext,
        PerformanceToolSnapshot? ffprobeContext,
        IReadOnlyList<PerformanceDriveSnapshot>? driveSnapshots)
    {
        lock (_syncRoot)
        {
            if (_session is null)
                return;

            _session.Environment = environment;
            _session.FfmpegContext = ffmpegContext;
            _session.FfprobeContext = ffprobeContext;
            _session.DriveSnapshots = driveSnapshots?.ToList() ?? [];
        }
    }

    public PerformanceSessionReport? GetCurrentSnapshot()
    {
        lock (_syncRoot)
        {
            if (_session is null)
                return null;

            return CreateSessionReport(
                _session,
                _timeProvider.GetTimestamp(),
                _timeProvider.GetUtcNow(),
                isFinal: false);
        }
    }

    public PerformanceSessionReport? CompleteSession(PerformanceOutcome outcome)
    {
        lock (_syncRoot)
        {
            if (_session is null)
                return null;

            var completedTimestamp = _timeProvider.GetTimestamp();
            var completedUtc = _timeProvider.GetUtcNow();
            var hadIncompleteScopes = false;

            foreach (var externalProcess in _session.ExternalProcesses.Values.ToList())
            {
                CompleteExternalProcessCore(
                    _session,
                    externalProcess,
                    PerformanceOutcome.Incomplete,
                    completedTimestamp);
                hadIncompleteScopes = true;
            }

            foreach (var phase in _session.Phases.Values)
            {
                if (phase.IsCompleted)
                    continue;

                CompletePhaseCore(phase, PerformanceOutcome.Incomplete, null, completedTimestamp, completedUtc);
                _session.ActivePhaseCount--;
                hadIncompleteScopes = true;
            }

            foreach (var operation in _session.Operations.Values)
            {
                if (operation.IsCompleted)
                    continue;

                CompleteOperationCore(operation, PerformanceOutcome.Incomplete, null, completedTimestamp, completedUtc);
                _session.ActiveOperationCount--;
                hadIncompleteScopes = true;
            }

            _session.CompletedTimestamp = completedTimestamp;
            _session.CompletedUtc = completedUtc;
            _session.Outcome = outcome == PerformanceOutcome.Success && hadIncompleteScopes
                ? PerformanceOutcome.Incomplete
                : outcome;

            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "session.completed",
                SessionId = _session.SessionId,
                Utc = completedUtc,
                Outcome = _session.Outcome,
                ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                    _session.StartedTimestamp, completedTimestamp).TotalMilliseconds
            });

            var report = CreateSessionReport(
                _session,
                completedTimestamp,
                completedUtc,
                isFinal: true);

            _session = null;
            return report;
        }
    }

    internal void CompleteOperation(Guid operationId, PerformanceOutcome outcome, Exception? exception)
    {
        if (operationId == Guid.Empty)
            return;

        lock (_syncRoot)
        {
            if (_session is null ||
                !_session.Operations.TryGetValue(operationId, out var operation) ||
                operation.IsCompleted)
            {
                return;
            }

            var completedTimestamp = _timeProvider.GetTimestamp();
            var completedUtc = _timeProvider.GetUtcNow();

            foreach (var externalProcess in _session.ExternalProcesses.Values
                         .Where(item => item.OperationId == operationId)
                         .ToList())
            {
                CompleteExternalProcessCore(
                    _session,
                    externalProcess,
                    PerformanceOutcome.Incomplete,
                    completedTimestamp);
            }

            foreach (var phaseId in operation.PhaseIds)
            {
                if (!_session.Phases.TryGetValue(phaseId, out var phase) || phase.IsCompleted)
                    continue;

                CompletePhaseCore(phase, PerformanceOutcome.Incomplete, null, completedTimestamp, completedUtc);
                _session.ActivePhaseCount--;
            }

            CompleteOperationCore(operation, outcome, exception, completedTimestamp, completedUtc);
            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "operation.completed",
                SessionId = _session.SessionId,
                OperationId = operation.OperationId,
                Utc = completedUtc,
                Name = operation.Name,
                Outcome = operation.Outcome,
                ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                    operation.StartedTimestamp, completedTimestamp).TotalMilliseconds
            });
            _session.ActiveOperationCount--;
        }
    }

    internal void CompletePhase(Guid phaseId, PerformanceOutcome outcome, Exception? exception)
    {
        if (phaseId == Guid.Empty)
            return;

        lock (_syncRoot)
        {
            if (_session is null ||
                !_session.Phases.TryGetValue(phaseId, out var phase) ||
                phase.IsCompleted)
            {
                return;
            }

            var completedTimestamp = _timeProvider.GetTimestamp();
            var completedUtc = _timeProvider.GetUtcNow();

            foreach (var externalProcess in _session.ExternalProcesses.Values
                         .Where(item => item.PhaseId == phaseId)
                         .ToList())
            {
                CompleteExternalProcessCore(
                    _session,
                    externalProcess,
                    PerformanceOutcome.Incomplete,
                    completedTimestamp);
            }

            CompletePhaseCore(
                phase,
                outcome,
                exception,
                completedTimestamp,
                completedUtc);
            WriteJournalEvent(new PerformanceJournalEvent
            {
                Event = "phase.completed",
                SessionId = _session.SessionId,
                OperationId = phase.OperationId,
                PhaseId = phase.PhaseId,
                Utc = completedUtc,
                Name = phase.Name,
                Outcome = phase.Outcome,
                ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                    phase.StartedTimestamp, completedTimestamp).TotalMilliseconds
            });

            _session.ActivePhaseCount--;
        }
    }

    internal void CompleteExternalProcess(
        Guid invocationId,
        PerformanceOutcome outcome)
    {
        if (invocationId == Guid.Empty)
            return;

        lock (_syncRoot)
        {
            if (_session is null ||
                !_session.ExternalProcesses.TryGetValue(invocationId, out var invocation))
            {
                return;
            }

            CompleteExternalProcessCore(
                _session,
                invocation,
                outcome,
                _timeProvider.GetTimestamp());
        }
    }

    private void CompleteExternalProcessCore(
        SessionState session,
        ExternalProcessInvocationState invocation,
        PerformanceOutcome outcome,
        long completedTimestamp)
    {
        var elapsedMilliseconds = Math.Max(
            0d,
            _timeProvider.GetElapsedTime(
                invocation.StartedTimestamp,
                completedTimestamp).TotalMilliseconds);

        if (session.Operations.TryGetValue(invocation.OperationId, out var operation) &&
            operation.ExternalProcessAggregates.TryGetValue(invocation.Key, out var operationAggregate))
        {
            operationAggregate.Complete(outcome, elapsedMilliseconds);
        }

        if (invocation.PhaseId.HasValue &&
            session.Phases.TryGetValue(invocation.PhaseId.Value, out var phase) &&
            phase.ExternalProcessAggregates.TryGetValue(invocation.Key, out var phaseAggregate))
        {
            phaseAggregate.Complete(outcome, elapsedMilliseconds);
        }

        WriteJournalEvent(new PerformanceJournalEvent
        {
            Event = "external.completed",
            SessionId = session.SessionId,
            OperationId = invocation.OperationId,
            PhaseId = invocation.PhaseId,
            Utc = _timeProvider.GetUtcNow(),
            Outcome = outcome,
            ElapsedMilliseconds = elapsedMilliseconds,
            Tool = invocation.Key.Tool,
            Purpose = invocation.Key.Purpose
        });

        session.ExternalProcesses.Remove(invocation.InvocationId);
        if (session.ActiveExternalProcessCount > 0)
            session.ActiveExternalProcessCount--;
    }

    private static ExternalProcessAggregateState GetOrCreateExternalProcessAggregate(
        Dictionary<ExternalProcessKey, ExternalProcessAggregateState> aggregates,
        ExternalProcessKey key)
    {
        if (aggregates.TryGetValue(key, out var existing))
            return existing;

        var created = new ExternalProcessAggregateState(key.Tool, key.Purpose);
        aggregates.Add(key, created);
        return created;
    }

    private void CompleteOperationCore(
        OperationState operation,
        PerformanceOutcome outcome,
        Exception? exception,
        long completedTimestamp,
        DateTimeOffset completedUtc)
    {
        operation.CompletedTimestamp = completedTimestamp;
        operation.CompletedUtc = completedUtc;
        operation.Outcome = outcome;
        SetFailure(operation, exception);
    }

    private void CompletePhaseCore(
        PhaseState phase,
        PerformanceOutcome outcome,
        Exception? exception,
        long completedTimestamp,
        DateTimeOffset completedUtc)
    {
        phase.CompletedTimestamp = completedTimestamp;
        phase.CompletedUtc = completedUtc;
        phase.Outcome = outcome;
        SetFailure(phase, exception);
    }

    private PerformanceSessionReport CreateSessionReport(
        SessionState session,
        long currentTimestamp,
        DateTimeOffset currentUtc,
        bool isFinal)
    {
        var operations = new List<PerformanceOperationReport>(session.OperationOrder.Count);

        foreach (var operationId in session.OperationOrder)
        {
            if (!session.Operations.TryGetValue(operationId, out var operation))
                continue;

            var phases = new List<PerformancePhaseReport>(operation.PhaseIds.Count);
            foreach (var phaseId in operation.PhaseIds)
            {
                if (!session.Phases.TryGetValue(phaseId, out var phase))
                    continue;

                var phaseEndTimestamp = phase.CompletedTimestamp ?? currentTimestamp;
                phases.Add(new PerformancePhaseReport
                {
                    PhaseId = phase.PhaseId,
                    OperationId = phase.OperationId,
                    Name = phase.Name,
                    StartedUtc = phase.StartedUtc,
                    CompletedUtc = phase.CompletedUtc,
                    ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                        phase.StartedTimestamp,
                        phaseEndTimestamp).TotalMilliseconds,
                    Outcome = phase.Outcome,
                    Metrics = [.. phase.Metrics],
                    ExternalProcessAggregates =
                        CreateExternalProcessAggregates(phase.ExternalProcessAggregates),
                    FailureType = phase.FailureType,
                    FailureMessage = phase.FailureMessage
                });
            }

            var operationEndTimestamp = operation.CompletedTimestamp ?? currentTimestamp;
            operations.Add(new PerformanceOperationReport
            {
                OperationId = operation.OperationId,
                SessionId = operation.SessionId,
                ParentOperationId = operation.ParentOperationId,
                Name = operation.Name,
                ProjectType = operation.ProjectType,
                StartedUtc = operation.StartedUtc,
                CompletedUtc = operation.CompletedUtc,
                ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                    operation.StartedTimestamp,
                    operationEndTimestamp).TotalMilliseconds,
                Outcome = operation.Outcome,
                Workload = operation.Workload,
                DriveRoleReferences = operation.DriveRoleReferences
                    .OrderBy(item => item.Key)
                    .Select(item => new PerformanceDriveRoleReference
                    {
                        DriveId = item.Key,
                        Roles = item.Value
                    })
                    .ToList(),
                Metrics = [.. operation.Metrics],
                ExternalProcessAggregates =
                    CreateExternalProcessAggregates(operation.ExternalProcessAggregates),
                FailureType = operation.FailureType,
                FailureMessage = operation.FailureMessage,
                Phases = phases,
                ChildOperationIds = [.. operation.ChildOperationIds]
            });
        }

        foreach (var pair in session.Measurements.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var aggregate = pair.Value;
            List<PerformanceMetric> metrics =
            [
                new() { Name = "Count", Value = aggregate.Count, Unit = "count" },
                new() { Name = "SumDuration", Value = aggregate.Sum, Unit = "ms" },
                new() { Name = "MeanDuration", Value = aggregate.Sum / aggregate.Count, Unit = "ms" },
                new() { Name = "MaxDuration", Value = aggregate.Maximum, Unit = "ms" }
            ];
            metrics.AddRange(aggregate.Counters.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new PerformanceMetric { Name = item.Key, Value = item.Value,
                    Unit = item.Key.EndsWith("Bytes", StringComparison.Ordinal) ? "bytes" : "count" }));
            operations.Add(new PerformanceOperationReport
            {
                SessionId = session.SessionId, OperationId = aggregate.Id, Name = pair.Key,
                StartedUtc = aggregate.StartedUtc, CompletedUtc = currentUtc,
                Outcome = PerformanceOutcome.Success, Metrics = metrics
                // No wall-clock duration: samples can overlap. SumDuration is measured work.
            });
        }

        var sessionEndTimestamp = isFinal
            ? session.CompletedTimestamp ?? currentTimestamp
            : currentTimestamp;

        return new PerformanceSessionReport
        {
            SessionId = session.SessionId,
            StartReason = session.Context.StartReason,
            StartedUtc = session.StartedUtc,
            CompletedUtc = isFinal ? session.CompletedUtc ?? currentUtc : null,
            ElapsedMilliseconds = _timeProvider.GetElapsedTime(
                session.StartedTimestamp,
                sessionEndTimestamp).TotalMilliseconds,
            Outcome = isFinal ? session.Outcome : PerformanceOutcome.Incomplete,
            ApplicationVersion = session.Context.ApplicationVersion,
            BuildIdentifier = session.Context.BuildIdentifier,
            SourceRevision = session.Context.SourceRevision,
            BuildConfiguration = session.Context.BuildConfiguration,
            SelectedParallelJobs = session.Context.SelectedParallelJobs,
            Environment = session.Environment,
            FfmpegContext = session.FfmpegContext,
            FfprobeContext = session.FfprobeContext,
            DriveSnapshots = [.. session.DriveSnapshots],
            Operations = operations
        };
    }

    private static List<PerformanceExternalProcessAggregate> CreateExternalProcessAggregates(
        Dictionary<ExternalProcessKey, ExternalProcessAggregateState> aggregates)
    {
        return aggregates.Values
            .OrderBy(item => item.Tool, StringComparer.Ordinal)
            .ThenBy(item => item.Purpose ?? string.Empty, StringComparer.Ordinal)
            .Select(item => item.ToReport())
            .ToList();
    }

    private void WriteJournalEvent(PerformanceJournalEvent journalEvent)
    {
        try
        {
            _journalEventSink?.Invoke(journalEvent);
        }
        catch
        {
            // Journal persistence is best effort and must never alter business flow.
        }
    }

    private static string NormalizeExternalProcessTool(string tool) =>
        tool.Trim().ToLowerInvariant();

    private static string? NormalizeExternalProcessPurpose(string? purpose) =>
        string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();

    private (long Timestamp, DateTimeOffset Utc) ResolveStart(
        long? startedTimestamp,
        DateTimeOffset? startedUtc)
    {
        var timestamp = startedTimestamp ?? _timeProvider.GetTimestamp();

        if (startedUtc.HasValue)
            return (timestamp, startedUtc.Value);

        if (!startedTimestamp.HasValue)
            return (timestamp, _timeProvider.GetUtcNow());

        var currentTimestamp = _timeProvider.GetTimestamp();
        var currentUtc = _timeProvider.GetUtcNow();
        var elapsedSinceStart = _timeProvider.GetElapsedTime(timestamp, currentTimestamp);
        return (timestamp, currentUtc - elapsedSinceStart);
    }

    private static void SetFailure(OperationState operation, Exception? exception)
    {
        if (exception is null)
            return;

        operation.FailureType = exception.GetType().FullName;
        operation.FailureMessage = NormalizeFailureMessage(exception.Message);
    }

    private static void SetFailure(PhaseState phase, Exception? exception)
    {
        if (exception is null)
            return;

        phase.FailureType = exception.GetType().FullName;
        phase.FailureMessage = NormalizeFailureMessage(exception.Message);
    }

    private static string? NormalizeFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var normalized = message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.Length <= MaxFailureMessageLength
            ? normalized
            : normalized[..MaxFailureMessageLength];
    }

    private sealed class SessionState
    {
        public Dictionary<string, MeasurementState> Measurements { get; } = new(StringComparer.Ordinal);
        public SessionState(
            Guid sessionId,
            PerformanceSessionStartContext context,
            long startedTimestamp,
            DateTimeOffset startedUtc)
        {
            SessionId = sessionId;
            Context = context;
            StartedTimestamp = startedTimestamp;
            StartedUtc = startedUtc;
        }

        public Guid SessionId { get; }
        public PerformanceSessionStartContext Context { get; }
        public long StartedTimestamp { get; }
        public DateTimeOffset StartedUtc { get; }
        public long? CompletedTimestamp { get; set; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public PerformanceOutcome Outcome { get; set; } = PerformanceOutcome.Incomplete;
        public PerformanceEnvironmentSnapshot? Environment { get; set; }
        public PerformanceToolSnapshot? FfmpegContext { get; set; }
        public PerformanceToolSnapshot? FfprobeContext { get; set; }
        public List<PerformanceDriveSnapshot> DriveSnapshots { get; set; } = [];
        public Dictionary<Guid, OperationState> Operations { get; } = [];
        public List<Guid> OperationOrder { get; } = [];
        public Dictionary<Guid, PhaseState> Phases { get; } = [];
        public Dictionary<Guid, ExternalProcessInvocationState> ExternalProcesses { get; } = [];
        public int ActiveOperationCount { get; set; }
        public int ActivePhaseCount { get; set; }
        public int ActiveExternalProcessCount { get; set; }
    }

    private sealed class MeasurementState(Guid id, DateTimeOffset startedUtc)
    {
        public Guid Id { get; } = id;
        public DateTimeOffset StartedUtc { get; } = startedUtc;
        public long Count;
        public double Sum;
        public double Maximum;
        public Dictionary<string, double> Counters { get; } = new(StringComparer.Ordinal);
    }

    private sealed class OperationState
    {
        public OperationState(
            Guid operationId,
            Guid sessionId,
            Guid? parentOperationId,
            string name,
            PerformanceProjectType projectType,
            PerformanceWorkloadSnapshot workload,
            long startedTimestamp,
            DateTimeOffset startedUtc)
        {
            OperationId = operationId;
            SessionId = sessionId;
            ParentOperationId = parentOperationId;
            Name = name;
            ProjectType = projectType;
            Workload = workload;
            StartedTimestamp = startedTimestamp;
            StartedUtc = startedUtc;
        }

        public Guid OperationId { get; }
        public Guid SessionId { get; }
        public Guid? ParentOperationId { get; }
        public string Name { get; }
        public PerformanceProjectType ProjectType { get; }
        public PerformanceWorkloadSnapshot Workload { get; set; }
        public Dictionary<Guid, PerformanceDriveRole> DriveRoleReferences { get; } = [];
        public long StartedTimestamp { get; }
        public DateTimeOffset StartedUtc { get; }
        public long? CompletedTimestamp { get; set; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public PerformanceOutcome Outcome { get; set; } = PerformanceOutcome.Incomplete;
        public string? FailureType { get; set; }
        public string? FailureMessage { get; set; }
        public List<PerformanceMetric> Metrics { get; } = [];
        public Dictionary<ExternalProcessKey, ExternalProcessAggregateState> ExternalProcessAggregates { get; } = [];
        public List<Guid> PhaseIds { get; } = [];
        public List<Guid> ChildOperationIds { get; } = [];
        public bool IsCompleted => CompletedTimestamp.HasValue;
    }

    private readonly record struct ExternalProcessKey(
        string Tool,
        string? Purpose);

    private sealed class ExternalProcessInvocationState
    {
        public ExternalProcessInvocationState(
            Guid invocationId,
            Guid operationId,
            Guid? phaseId,
            ExternalProcessKey key,
            long startedTimestamp)
        {
            InvocationId = invocationId;
            OperationId = operationId;
            PhaseId = phaseId;
            Key = key;
            StartedTimestamp = startedTimestamp;
        }

        public Guid InvocationId { get; }
        public Guid OperationId { get; }
        public Guid? PhaseId { get; }
        public ExternalProcessKey Key { get; }
        public long StartedTimestamp { get; }
    }

    private sealed class ExternalProcessAggregateState
    {
        public ExternalProcessAggregateState(string tool, string? purpose)
        {
            Tool = tool;
            Purpose = purpose;
        }

        public string Tool { get; }
        public string? Purpose { get; }
        public int CallCount { get; private set; }
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }
        public int CancelledCount { get; private set; }
        public double SumElapsedMilliseconds { get; private set; }
        public double? MinElapsedMilliseconds { get; private set; }
        public double? MaxElapsedMilliseconds { get; private set; }
        public int ActiveCount { get; private set; }
        public int MaxConcurrency { get; private set; }

        public void Start()
        {
            ActiveCount++;
            MaxConcurrency = Math.Max(MaxConcurrency, ActiveCount);
        }

        public void Complete(
            PerformanceOutcome outcome,
            double elapsedMilliseconds)
        {
            if (ActiveCount > 0)
                ActiveCount--;

            CallCount++;
            SumElapsedMilliseconds += elapsedMilliseconds;
            MinElapsedMilliseconds = !MinElapsedMilliseconds.HasValue
                ? elapsedMilliseconds
                : Math.Min(MinElapsedMilliseconds.Value, elapsedMilliseconds);
            MaxElapsedMilliseconds = !MaxElapsedMilliseconds.HasValue
                ? elapsedMilliseconds
                : Math.Max(MaxElapsedMilliseconds.Value, elapsedMilliseconds);

            switch (outcome)
            {
                case PerformanceOutcome.Success:
                    SuccessCount++;
                    break;
                case PerformanceOutcome.Cancelled:
                    CancelledCount++;
                    break;
                default:
                    // The aggregate schema has no separate incomplete bucket.
                    // An unfinished process is therefore counted as a failed call.
                    FailureCount++;
                    break;
            }
        }

        public PerformanceExternalProcessAggregate ToReport() =>
            new()
            {
                Tool = Tool,
                Purpose = Purpose,
                CallCount = CallCount,
                SuccessCount = SuccessCount,
                FailureCount = FailureCount,
                CancelledCount = CancelledCount,
                SumElapsedMilliseconds = SumElapsedMilliseconds,
                MinElapsedMilliseconds = MinElapsedMilliseconds,
                MaxElapsedMilliseconds = MaxElapsedMilliseconds,
                MeanElapsedMilliseconds = CallCount == 0
                    ? null
                    : SumElapsedMilliseconds / CallCount,
                MaxConcurrency = MaxConcurrency
            };
    }

    private sealed class PhaseState
    {
        public PhaseState(
            Guid phaseId,
            Guid operationId,
            string name,
            long startedTimestamp,
            DateTimeOffset startedUtc)
        {
            PhaseId = phaseId;
            OperationId = operationId;
            Name = name;
            StartedTimestamp = startedTimestamp;
            StartedUtc = startedUtc;
        }

        public Guid PhaseId { get; }
        public Guid OperationId { get; }
        public string Name { get; }
        public long StartedTimestamp { get; }
        public DateTimeOffset StartedUtc { get; }
        public long? CompletedTimestamp { get; set; }
        public DateTimeOffset? CompletedUtc { get; set; }
        public PerformanceOutcome Outcome { get; set; } = PerformanceOutcome.Incomplete;
        public string? FailureType { get; set; }
        public string? FailureMessage { get; set; }
        public List<PerformanceMetric> Metrics { get; } = [];
        public Dictionary<ExternalProcessKey, ExternalProcessAggregateState> ExternalProcessAggregates { get; } = [];
        public bool IsCompleted => CompletedTimestamp.HasValue;
    }
}
