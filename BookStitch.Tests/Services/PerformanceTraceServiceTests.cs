using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceTraceServiceTests
{
    [Theory]
    [InlineData(true, PerformanceOutcome.Paused)]
    [InlineData(false, PerformanceOutcome.Cancelled)]
    public void Cancellation_semantics_preserve_workload_and_journal_outcomes(bool pause, PerformanceOutcome expected)
    {
        var events = new List<PerformanceJournalEvent>();
        var trace = new PerformanceTraceService(true, journalEventSink: events.Add);
        trace.TryStartSession(CreateContext());
        using var operation = trace.StartOperation("disc.mp3.import");
        using var phase = trace.StartPhase(operation.OperationId, "file.copy");
        trace.UpdateOperationWorkload(operation.OperationId,
            new PerformanceWorkloadSnapshot { DiscCount = 1, FileCount = 23, TrackCount = 23 });
        phase.Cancel(pause);
        operation.Cancel(pause);
        var report = trace.CompleteSession(PerformanceOutcome.Success)!;
        var recorded = Assert.Single(report.Operations);
        Assert.Equal(expected, recorded.Outcome);
        Assert.Equal(expected, Assert.Single(recorded.Phases).Outcome);
        Assert.Equal(23, recorded.Workload.FileCount);
        Assert.Equal(23, recorded.Workload.TrackCount);
        Assert.Contains(events, item => item.Event == "operation.completed" && item.Outcome == expected);
        Assert.Contains(events, item => item.Event == "phase.completed" && item.Outcome == expected);
    }

    [Theory]
    [InlineData("20", 20, "jobs")]
    [InlineData("Auto", 0, "Auto")]
    public void Parallelism_is_operation_specific_and_preserves_session_start(string requested, int value, string unit)
    {
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(CreateContext() with { SelectedParallelJobs = "24" });
        using var operation = trace.StartOperation("disc.mp3.import");
        trace.RecordParallelism(operation.OperationId, requested, 4);
        operation.Complete();
        var report = trace.CompleteSession(PerformanceOutcome.Success)!;
        Assert.Equal("24", report.SelectedParallelJobs);
        var metrics = Assert.Single(report.Operations).Metrics;
        Assert.Contains(metrics, item => item.Name == "RequestedParallelJobs" && item.Value == value && item.Unit == unit);
        Assert.Contains(metrics, item => item.Name == "EffectiveWorkerLimit" && item.Value == 4);
    }

    [Fact]
    public void Measurements_aggregate_without_per_sample_journal_or_active_scopes()
    {
        var events = new List<PerformanceJournalEvent>();
        var trace = new PerformanceTraceService(true, journalEventSink: events.Add);
        trace.TryStartSession(CreateContext());
        var session = trace.ActiveSessionId!.Value;
        Parallel.For(0, 1000, index => trace.RecordMeasurement(session, "manifest.read", index % 2 == 0 ? 2 : 4,
            new() { ["Bytes"] = 10 }));
        Assert.Equal(0, trace.ActiveScopeCount);
        var report = trace.CompleteSession(PerformanceOutcome.Success)!;
        var metrics = Assert.Single(report.Operations).Metrics.ToDictionary(item => item.Name, item => item.Value);
        Assert.Equal(1000, metrics["Count"]);
        Assert.Equal(3000, metrics["SumDuration"]);
        Assert.Equal(3, metrics["MeanDuration"]);
        Assert.Equal(4, metrics["MaxDuration"]);
        Assert.Equal(10000, metrics["Bytes"]);
        Assert.Equal(2, events.Count); // Session lifecycle only.
        trace.TryStartSession(CreateContext());
        trace.RecordMeasurement(session, "old.sample", 1, []);
        Assert.Empty(trace.CompleteSession(PerformanceOutcome.Success)!.Operations);
    }

    [Fact]
    public void Disabled_measurements_are_no_ops_even_without_a_session()
    {
        var trace = new PerformanceTraceService(false);
        Assert.Null(PerformanceMeasurement.Start("manifest.save", trace));
        trace.RecordParallelism(Guid.NewGuid(), "20", 20);
        default(PerformanceOperationScope).Pause();
        default(PerformancePhaseScope).Cancel(true);
        Assert.Null(trace.GetCurrentSnapshot());
    }

    [Fact]
    public void DisabledMode_CreatesNoSessionAndReturnsNoOpScopes()
    {
        var journalEvents = new List<PerformanceJournalEvent>();
        var service = new PerformanceTraceService(
            enabled: false,
            timeProvider: new ManualTimeProvider(),
            journalEventSink: journalEvents.Add);

        var started = service.TryStartSession(CreateContext());
        var operation = service.StartOperation("project.resume");
        var phase = service.StartPhase(Guid.NewGuid(), "audio.probe");

        Assert.False(started);
        Assert.Null(service.ActiveSessionId);
        Assert.Equal(0, service.ActiveScopeCount);
        Assert.False(operation.IsActive);
        Assert.False(phase.IsActive);
        Assert.Null(service.GetCurrentSnapshot());
        Assert.Empty(journalEvents);
    }

    [Fact]
    public void SessionOperationAndPhase_UseMonotonicElapsedTime()
    {
        var time = new ManualTimeProvider();
        var service = new PerformanceTraceService(enabled: true, timeProvider: time);

        Assert.True(service.TryStartSession(CreateContext()));
        var operation = service.StartOperation("project.resume", projectType: PerformanceProjectType.Resume);

        time.Advance(TimeSpan.FromMilliseconds(100));
        var phase = service.StartPhase(operation.OperationId, "audio.probe");

        time.Advance(TimeSpan.FromMilliseconds(250));
        phase.Complete();

        time.Advance(TimeSpan.FromMilliseconds(50));
        operation.Complete();

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(400d, report!.ElapsedMilliseconds!.Value);
        Assert.Equal(PerformanceOutcome.Success, report.Outcome);
        Assert.Single(report.Operations);
        Assert.Equal(400d, report.Operations[0].ElapsedMilliseconds!.Value);
        Assert.Single(report.Operations[0].Phases);
        Assert.Equal(250d, report.Operations[0].Phases[0].ElapsedMilliseconds!.Value);
        Assert.Equal(PerformanceOutcome.Success, report.Operations[0].Phases[0].Outcome);
        Assert.Equal(0, service.ActiveScopeCount);
    }

    [Fact]
    public void BootstrapTimestamp_CanIncludeTimeBeforeSettingsLoad()
    {
        var time = new ManualTimeProvider();
        var bootstrapTimestamp = time.GetTimestamp();
        var bootstrapUtc = time.GetUtcNow();

        time.Advance(TimeSpan.FromMilliseconds(120));

        var service = new PerformanceTraceService(enabled: true, timeProvider: time);
        Assert.True(service.TryStartSession(CreateContext(), bootstrapTimestamp, bootstrapUtc));

        time.Advance(TimeSpan.FromMilliseconds(80));
        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(200d, report!.ElapsedMilliseconds!.Value);
        Assert.Equal(bootstrapUtc, report.StartedUtc);
    }

    [Fact]
    public void BootstrapTimestamp_WithoutUtcBackdatesAbsoluteStart()
    {
        var time = new ManualTimeProvider();
        var bootstrapTimestamp = time.GetTimestamp();
        var expectedStartedUtc = time.GetUtcNow();

        time.Advance(TimeSpan.FromMilliseconds(120));

        var service = new PerformanceTraceService(enabled: true, timeProvider: time);
        Assert.True(service.TryStartSession(CreateContext(), bootstrapTimestamp));

        time.Advance(TimeSpan.FromMilliseconds(80));
        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(200d, report!.ElapsedMilliseconds!.Value);
        Assert.Equal(expectedStartedUtc, report.StartedUtc);
    }

    [Fact]
    public void BootstrapOperationTimestamp_CanIncludeTimeBeforeOperationRegistration()
    {
        var time = new ManualTimeProvider();
        var bootstrapTimestamp = time.GetTimestamp();
        var expectedStartedUtc = time.GetUtcNow();

        time.Advance(TimeSpan.FromMilliseconds(120));

        var service = new PerformanceTraceService(enabled: true, timeProvider: time);
        Assert.True(service.TryStartSession(CreateContext(), bootstrapTimestamp));
        var operation = service.StartOperation("app.startup", startedTimestamp: bootstrapTimestamp);

        time.Advance(TimeSpan.FromMilliseconds(80));
        operation.Complete();

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        var startup = Assert.Single(report!.Operations);
        Assert.Equal("app.startup", startup.Name);
        Assert.Equal(200d, startup.ElapsedMilliseconds!.Value);
        Assert.Equal(expectedStartedUtc, startup.StartedUtc);
        Assert.Equal(report.StartedUtc, startup.StartedUtc);
    }

    [Fact]
    public void ScopeOutcomes_PreserveFailureCancellationAndIncompleteDispose()
    {
        var time = new ManualTimeProvider();
        var service = new PerformanceTraceService(enabled: true, timeProvider: time);
        Assert.True(service.TryStartSession(CreateContext()));

        var failed = service.StartOperation("failed.operation");
        failed.Fail(new InvalidOperationException("expected failure"));

        var cancelled = service.StartOperation("cancelled.operation");
        cancelled.Cancel();

        var incomplete = service.StartOperation("incomplete.operation");
        incomplete.Dispose();

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(3, report!.Operations.Count);
        Assert.Equal(PerformanceOutcome.Failed, report.Operations[0].Outcome);
        Assert.Equal(typeof(InvalidOperationException).FullName, report.Operations[0].FailureType);
        Assert.Equal("expected failure", report.Operations[0].FailureMessage);
        Assert.Equal(PerformanceOutcome.Cancelled, report.Operations[1].Outcome);
        Assert.Equal(PerformanceOutcome.Incomplete, report.Operations[2].Outcome);
    }

    [Fact]
    public void ChildOperation_PreservesParentRelationship()
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: new ManualTimeProvider());
        Assert.True(service.TryStartSession(CreateContext()));

        var parent = service.StartOperation("disc.audio.run");
        var child = service.StartOperation("disc.audio.rip", parent.OperationId);
        child.Complete();
        parent.Complete();

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(parent.OperationId, report!.Operations[1].ParentOperationId);
        Assert.Contains(child.OperationId, report.Operations[0].ChildOperationIds);
    }

    [Fact]
    public void Disable_BlocksNewScopesButAllowsExistingScopeToComplete()
    {
        var time = new ManualTimeProvider();
        var service = new PerformanceTraceService(enabled: true, timeProvider: time);
        Assert.True(service.TryStartSession(CreateContext()));

        var existing = service.StartOperation("export.run");
        Assert.True(existing.IsActive);

        service.SetEnabled(false);
        var blocked = service.StartOperation("must.not.start");

        Assert.False(blocked.IsActive);
        Assert.Equal(1, service.ActiveScopeCount);

        time.Advance(TimeSpan.FromSeconds(1));
        existing.Complete();

        Assert.Equal(0, service.ActiveScopeCount);
        var report = service.CompleteSession(PerformanceOutcome.Success);
        Assert.NotNull(report);
        Assert.Equal(PerformanceOutcome.Success, report!.Operations[0].Outcome);
    }

    [Fact]
    public void CompletingSession_MarksStillOpenScopesIncomplete()
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: new ManualTimeProvider());
        Assert.True(service.TryStartSession(CreateContext()));

        var operation = service.StartOperation("project.local.import");
        _ = service.StartPhase(operation.OperationId, "project.scan");

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(PerformanceOutcome.Incomplete, report!.Outcome);
        Assert.Equal(PerformanceOutcome.Incomplete, report.Operations[0].Outcome);
        Assert.Equal(PerformanceOutcome.Incomplete, report.Operations[0].Phases[0].Outcome);
        Assert.Equal(0, service.ActiveScopeCount);
    }

    [Fact]
    public void ParallelOperationScopes_AreThreadSafe()
    {
        var service = new PerformanceTraceService(enabled: true);
        Assert.True(service.TryStartSession(CreateContext()));

        Parallel.For(0, 64, index =>
        {
            var scope = service.StartOperation($"parallel.{index}");
            scope.Complete();
        });

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(64, report!.Operations.Count);
        Assert.All(report.Operations, operation => Assert.Equal(PerformanceOutcome.Success, operation.Outcome));
        Assert.Equal(0, service.ActiveScopeCount);
    }

    [Fact]
    public void Metrics_AreRecordedOnlyForActiveScopes()
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: new ManualTimeProvider());
        Assert.True(service.TryStartSession(CreateContext()));

        var operation = service.StartOperation("export.run");
        var phase = service.StartPhase(operation.OperationId, "export.convert");

        service.RecordMetric(operation.OperationId, "track.count", 12, "tracks");
        service.RecordPhaseMetric(phase.PhaseId, "input.bytes", 4096, "bytes");

        phase.Complete();
        operation.Complete();

        service.RecordMetric(operation.OperationId, "ignored", 1);
        service.RecordPhaseMetric(phase.PhaseId, "ignored", 1);

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Single(report!.Operations[0].Metrics);
        Assert.Equal("track.count", report.Operations[0].Metrics[0].Name);
        Assert.Single(report.Operations[0].Phases[0].Metrics);
        Assert.Equal("input.bytes", report.Operations[0].Phases[0].Metrics[0].Name);
    }

    [Fact]
    public void ActiveOperation_ContextCanUpdateWorkloadAndMergeDriveRoles()
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: new ManualTimeProvider());
        Assert.True(service.TryStartSession(CreateContext()));

        var operation = service.StartOperation(
            "project.local.import",
            projectType: PerformanceProjectType.LocalFolder);
        var driveId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        service.UpdateOperationWorkload(
            operation.OperationId,
            new PerformanceWorkloadSnapshot
            {
                ProjectType = PerformanceProjectType.LocalFolder,
                TrackCount = 12,
                FileCount = 12,
                InputBytes = 4096,
                InputAudioDurationMilliseconds = 123456
            });
        service.RecordDriveRoleReference(
            operation.OperationId,
            driveId,
            PerformanceDriveRole.Source);
        service.RecordDriveRoleReference(
            operation.OperationId,
            driveId,
            PerformanceDriveRole.ProjectWorking);

        operation.Complete();
        service.UpdateOperationWorkload(
            operation.OperationId,
            new PerformanceWorkloadSnapshot { TrackCount = 99 });
        service.RecordDriveRoleReference(
            operation.OperationId,
            Guid.NewGuid(),
            PerformanceDriveRole.Output);

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        var operationReport = Assert.Single(report!.Operations);
        Assert.Equal(PerformanceProjectType.LocalFolder, operationReport.Workload.ProjectType);
        Assert.Equal(12, operationReport.Workload.TrackCount);
        Assert.Equal(12, operationReport.Workload.FileCount);
        Assert.Equal(4096, operationReport.Workload.InputBytes);
        Assert.Equal(123456d, operationReport.Workload.InputAudioDurationMilliseconds);

        var driveReference = Assert.Single(operationReport.DriveRoleReferences);
        Assert.Equal(driveId, driveReference.DriveId);
        Assert.Equal(
            PerformanceDriveRole.Source | PerformanceDriveRole.ProjectWorking,
            driveReference.Roles);
    }

    [Fact]
    public void DisabledDuringActiveOperation_AllowsFinalContextUpdateBeforeCompletion()
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: new ManualTimeProvider());
        Assert.True(service.TryStartSession(CreateContext()));
        var operation = service.StartOperation("project.local.import");
        var driveId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        service.SetEnabled(false);
        service.UpdateOperationWorkload(
            operation.OperationId,
            new PerformanceWorkloadSnapshot
            {
                ProjectType = PerformanceProjectType.LocalFolder,
                TrackCount = 3
            });
        service.RecordDriveRoleReference(
            operation.OperationId,
            driveId,
            PerformanceDriveRole.Source);
        operation.Complete();

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        var operationReport = Assert.Single(report!.Operations);
        Assert.Equal(3, operationReport.Workload.TrackCount);
        Assert.Equal(driveId, Assert.Single(operationReport.DriveRoleReferences).DriveId);
    }


    [Fact]
    public void JournalSink_ReceivesStableLifecycleEventsAndSessionContextIsReported()
    {
        var events = new List<PerformanceJournalEvent>();
        var sessionId = Guid.NewGuid();
        var service = new PerformanceTraceService(
            enabled: true,
            timeProvider: new ManualTimeProvider(),
            journalEventSink: events.Add);

        Assert.True(service.TryStartSession(CreateContext(), sessionId: sessionId));
        var operation = service.StartOperation("export.run");
        var phase = service.StartPhase(operation.OperationId, "export.convert");
        phase.Complete();
        operation.Complete();
        service.SetSessionContext(
            new PerformanceEnvironmentSnapshot { LogicalProcessorCount = 8 },
            new PerformanceToolSnapshot { Available = true, Version = "ffmpeg-test" },
            new PerformanceToolSnapshot { Available = true, Version = "ffprobe-test" },
            [new PerformanceDriveSnapshot { DriveId = Guid.NewGuid(), DriveRoot = "D:\\" }]);

        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        Assert.Equal(sessionId, report!.SessionId);
        Assert.Equal(8, report.Environment!.LogicalProcessorCount);
        Assert.Equal("ffmpeg-test", report.FfmpegContext!.Version);
        Assert.Single(report.DriveSnapshots);
        Assert.Equal("session.started", events.First().Event);
        Assert.Contains(events, item => item.Event == "operation.started");
        Assert.Contains(events, item => item.Event == "phase.completed");
        Assert.Equal("session.completed", events.Last().Event);
    }


    [Fact]
    public void DriveMeasurement_UsesExistingJournalPipeline()
    {
        var events = new List<PerformanceJournalEvent>();
        var service = new PerformanceTraceService(enabled: true, journalEventSink: events.Add);
        Assert.True(service.TryStartSession(CreateContext()));

        service.RecordDriveMeasurement(new DiscDriveMeasurement(
            @"D:\",
            "D:",
            "IOCTL_STORAGE_CHECK_VERIFY2",
            DateTimeOffset.UtcNow,
            123.4,
            "OK",
            true,
            false,
            false,
            true,
            true,
            true));

        var item = Assert.Single(events, item => item.Event == "drive.measurement");
        Assert.Equal("IOCTL_STORAGE_CHECK_VERIFY2", item.ProbeType);
        Assert.Equal(123.4, item.ElapsedMilliseconds);
        Assert.True(item.Success);
        Assert.True(item.FirstTouchInSession);
    }

    private static PerformanceSessionStartContext CreateContext()
    {
        return new PerformanceSessionStartContext
        {
            ApplicationVersion = "1.0.1",
            SourceRevision = "29b85ef5c73780b03ef1f909d5bbf3d8684ffb76",
            BuildConfiguration = "Debug",
            SelectedParallelJobs = "Auto"
        };
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private const long Frequency = TimeSpan.TicksPerSecond;
        private readonly DateTimeOffset _originUtc = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => Frequency;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow()
        {
            return _originUtc + TimeSpan.FromTicks(_timestamp);
        }

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
        }
    }
}
