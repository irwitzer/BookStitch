using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceExternalProcessInfrastructureTests
{
    [Fact]
    public void ExternalProcessAggregates_TrackOutcomesElapsedAndConcurrencyForOperationAndPhase()
    {
        var time = new ManualTimeProvider();
        var service = CreateStartedService(time);
        var operation = service.StartOperation("export.run");
        var phase = service.StartPhase(operation.OperationId, "export.convert");

        var first = service.StartExternalProcess(
            operation.OperationId,
            phase.PhaseId,
            "ffmpeg",
            "export.convert");

        time.Advance(TimeSpan.FromMilliseconds(100));

        var second = service.StartExternalProcess(
            operation.OperationId,
            phase.PhaseId,
            "ffmpeg",
            "export.convert");

        time.Advance(TimeSpan.FromMilliseconds(50));
        first.Complete(exitCode: 0);

        time.Advance(TimeSpan.FromMilliseconds(50));
        second.Complete(exitCode: 1);

        var probe = service.StartExternalProcess(
            operation.OperationId,
            phase.PhaseId,
            "ffprobe",
            "audio.probe");

        time.Advance(TimeSpan.FromMilliseconds(25));
        probe.Cancel();

        phase.Complete();
        operation.Complete();

        var report = service.CompleteSession(PerformanceOutcome.Success);
        Assert.NotNull(report);

        var operationReport = Assert.Single(report!.Operations);
        Assert.Equal(2, operationReport.ExternalProcessAggregates.Count);

        var ffmpeg = Assert.Single(
            operationReport.ExternalProcessAggregates,
            item => item.Tool == "ffmpeg" && item.Purpose == "export.convert");
        Assert.Equal(2, ffmpeg.CallCount);
        Assert.Equal(1, ffmpeg.SuccessCount);
        Assert.Equal(1, ffmpeg.FailureCount);
        Assert.Equal(0, ffmpeg.CancelledCount);
        Assert.Equal(250d, ffmpeg.SumElapsedMilliseconds);
        Assert.Equal(100d, ffmpeg.MinElapsedMilliseconds!.Value);
        Assert.Equal(150d, ffmpeg.MaxElapsedMilliseconds!.Value);
        Assert.Equal(125d, ffmpeg.MeanElapsedMilliseconds!.Value);
        Assert.Equal(2, ffmpeg.MaxConcurrency);

        var ffprobe = Assert.Single(
            operationReport.ExternalProcessAggregates,
            item => item.Tool == "ffprobe" && item.Purpose == "audio.probe");
        Assert.Equal(1, ffprobe.CallCount);
        Assert.Equal(0, ffprobe.SuccessCount);
        Assert.Equal(0, ffprobe.FailureCount);
        Assert.Equal(1, ffprobe.CancelledCount);
        Assert.Equal(25d, ffprobe.SumElapsedMilliseconds);
        Assert.Equal(1, ffprobe.MaxConcurrency);

        var phaseReport = Assert.Single(operationReport.Phases);
        Assert.Equal(2, phaseReport.ExternalProcessAggregates.Count);
        Assert.Equal(
            ffmpeg,
            Assert.Single(
                phaseReport.ExternalProcessAggregates,
                item => item.Tool == "ffmpeg" && item.Purpose == "export.convert"));
    }

    [Fact]
    public void DisabledMode_ReturnsNoOpExternalProcessScope()
    {
        var service = new PerformanceTraceService(
            enabled: false,
            timeProvider: new ManualTimeProvider());

        var scope = service.StartExternalProcess(
            Guid.NewGuid(),
            null,
            "ffmpeg",
            "export.convert");

        Assert.False(scope.IsActive);
        Assert.Equal(0, service.ActiveScopeCount);
        Assert.Null(service.ActiveSessionId);
    }

    [Fact]
    public void Disable_BlocksNewExternalProcessesAndAllowsExistingCompletion()
    {
        var time = new ManualTimeProvider();
        var service = CreateStartedService(time);
        var operation = service.StartOperation("export.run");
        var active = service.StartExternalProcess(
            operation.OperationId,
            null,
            "ffmpeg",
            "export.merge");

        Assert.True(active.IsActive);
        Assert.Equal(2, service.ActiveScopeCount);

        service.SetEnabled(false);

        var blocked = service.StartExternalProcess(
            operation.OperationId,
            null,
            "ffmpeg",
            "export.merge");

        Assert.False(blocked.IsActive);

        time.Advance(TimeSpan.FromMilliseconds(40));
        active.Complete(exitCode: 0);

        Assert.Equal(1, service.ActiveScopeCount);

        operation.Complete();
        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        var aggregate = Assert.Single(report!.Operations[0].ExternalProcessAggregates);
        Assert.Equal(1, aggregate.CallCount);
        Assert.Equal(1, aggregate.SuccessCount);
    }

    [Fact]
    public void PerformanceContext_UsesDefaultPurposeAndExplicitOverride()
    {
        var service = CreateStartedService(new ManualTimeProvider());
        var operation = service.StartOperation("project.local.import");

        var defaultContext = new PerformanceExternalProcessContext(
            service,
            operation.OperationId);
        var defaultScope = defaultContext.Start("ffprobe", "audio.probe");
        defaultScope.Complete(exitCode: 0);

        var overrideContext = new PerformanceExternalProcessContext(
            service,
            operation.OperationId,
            Purpose: "project.scan.probe");
        var overrideScope = overrideContext.Start("ffprobe", "audio.probe");
        overrideScope.Complete(exitCode: 0);

        operation.Complete();
        var report = service.CompleteSession(PerformanceOutcome.Success);

        Assert.NotNull(report);
        var aggregates = report!.Operations[0].ExternalProcessAggregates;
        Assert.Contains(aggregates, item => item.Purpose == "audio.probe");
        Assert.Contains(aggregates, item => item.Purpose == "project.scan.probe");
    }

    [Fact]
    public void CompletingOperation_ClosesOutstandingExternalProcessAsFailureAggregate()
    {
        var time = new ManualTimeProvider();
        var service = CreateStartedService(time);
        var operation = service.StartOperation("export.run");
        var process = service.StartExternalProcess(
            operation.OperationId,
            null,
            "ffmpeg",
            "export.finalize");

        time.Advance(TimeSpan.FromMilliseconds(30));
        operation.Complete();

        Assert.Equal(0, service.ActiveScopeCount);

        // Late completion from a process wrapper must be harmless and idempotent.
        process.Complete(exitCode: 0);

        var report = service.CompleteSession(PerformanceOutcome.Success);
        Assert.NotNull(report);

        var aggregate = Assert.Single(report!.Operations[0].ExternalProcessAggregates);
        Assert.Equal(1, aggregate.CallCount);
        Assert.Equal(0, aggregate.SuccessCount);
        Assert.Equal(1, aggregate.FailureCount);
        Assert.Equal(30d, aggregate.SumElapsedMilliseconds);
    }

    private static PerformanceTraceService CreateStartedService(TimeProvider timeProvider)
    {
        var service = new PerformanceTraceService(enabled: true, timeProvider: timeProvider);
        Assert.True(service.TryStartSession(
            new PerformanceSessionStartContext
            {
                ApplicationVersion = "test",
                BuildConfiguration = "Debug"
            }));
        return service;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private const long Frequency = TimeSpan.TicksPerSecond;
        private readonly DateTimeOffset _originUtc =
            new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => Frequency;

        public override long GetTimestamp() => _timestamp;

        public override DateTimeOffset GetUtcNow() =>
            _originUtc + TimeSpan.FromTicks(_timestamp);

        public void Advance(TimeSpan duration) =>
            _timestamp += duration.Ticks;
    }
}
