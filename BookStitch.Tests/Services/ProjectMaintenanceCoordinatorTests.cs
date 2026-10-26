using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMaintenanceCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
    private static ProjectMaintenanceRequest Request(string mode = ProjectRetentionModes.Days) => new("root", "covers", 37, mode, Now);

    [Theory]
    [InlineData(MaintenanceSafeSpace.None, false, false, false, false)]
    [InlineData(MaintenanceSafeSpace.GlobalSettings, false, false, false, true)]
    [InlineData(MaintenanceSafeSpace.Metadata, false, false, false, true)]
    [InlineData(MaintenanceSafeSpace.Complete, false, false, false, true)]
    [InlineData(MaintenanceSafeSpace.Complete, true, false, false, false)]
    [InlineData(MaintenanceSafeSpace.Metadata, false, true, false, false)]
    [InlineData(MaintenanceSafeSpace.GlobalSettings, false, false, true, false)]
    public async Task Only_explicit_safe_spaces_without_active_work_authorize_background_maintenance(
        MaintenanceSafeSpace space, bool active, bool transition, bool operation, bool expected)
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var workerThread = 0;
        using var started = new ManualResetEventSlim();
        var reservationWritten = false;
        using var coordinator = new ProjectMaintenanceCoordinator((request, _) =>
        {
            Assert.True(reservationWritten);
            Assert.Equal(37, request.RetentionDays);
            workerThread = Environment.CurrentManagedThreadId;
            started.Set();
            return new(0, 0, []);
        }, (_, _) => []);
        coordinator.SetEligibility(new(space, active, transition, operation));
        var run = coordinator.TryStartAsync(Request(), null, _ => reservationWritten = true);
        if (expected) Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(expected, await run);
        Assert.Equal(expected, reservationWritten);
        if (expected) Assert.NotEqual(callerThread, workerThread);
    }

    [Fact]
    public async Task Daily_reservation_is_persistent_and_prevents_duplicate_runs_across_sessions()
    {
        var runs = 0;
        DateTime? persisted = null;
        using var coordinator = new ProjectMaintenanceCoordinator((_, _) => { ++runs; return new(0, 0, []); }, (_, _) => []);
        coordinator.SetEligibility(new(MaintenanceSafeSpace.Complete, false, false, false));
        Assert.True(await coordinator.TryStartAsync(Request(), persisted, stamp => { persisted = stamp; return true; }));
        Assert.False(await coordinator.TryStartAsync(Request(), null, _ => throw new Exception("No second reservation")));
        Assert.False(coordinator.IsDue(persisted, Now.AddHours(23.99)));
        Assert.True(coordinator.IsDue(persisted, Now.AddHours(24)));
        using var nextSession = new ProjectMaintenanceCoordinator((_, _) => { ++runs; return new(0, 0, []); }, (_, _) => []);
        nextSession.SetEligibility(new(MaintenanceSafeSpace.Metadata, false, false, false));
        Assert.False(await nextSession.TryStartAsync(Request(), persisted, _ => true));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Never_retention_skips_project_deletion_but_preserves_cover_cleanup()
    {
        var coverRuns = 0;
        using var coordinator = new ProjectMaintenanceCoordinator((_, _) => throw new Exception("Never deletes projects"),
            (request, _) => { Assert.Equal(37, request.RetentionDays); ++coverRuns; return []; });
        coordinator.SetEligibility(new(MaintenanceSafeSpace.GlobalSettings, false, false, false));
        Assert.True(await coordinator.TryStartAsync(Request(ProjectRetentionModes.Never), null, _ => true));
        Assert.Equal(1, coverRuns);
    }

    [Fact]
    public async Task Starting_pipeline_work_cancels_current_unit_and_prevents_later_cover_cleanup()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coverRuns = 0;
        using var coordinator = new ProjectMaintenanceCoordinator((_, token) =>
        {
            started.SetResult(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested();
            return new(0, 0, []);
        }, (_, _) => { ++coverRuns; return []; });
        coordinator.SetEligibility(new(MaintenanceSafeSpace.Complete, false, false, false));
        var run = coordinator.TryStartAsync(Request(), null, _ => true);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(await coordinator.TryStartAsync(Request(), null, _ => throw new Exception("No concurrent run")));
        coordinator.SetEligibility(new(MaintenanceSafeSpace.Complete, true, false, false));
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, coverRuns);
        Assert.False(coordinator.IsDue(null, Now));
    }

    [Fact]
    public async Task Failed_reservation_prevents_work_and_per_item_failures_do_not_abort_other_cleanup()
    {
        var projectRuns = 0;
        var coverRuns = 0;
        var failures = new List<string>();
        using var coordinator = new ProjectMaintenanceCoordinator((_, _) => { ++projectRuns; return new(1, 0, ["project failure"]); },
            (_, _) => { ++coverRuns; return ["cover failure"]; });
        coordinator.LogFailure = failures.Add;
        coordinator.SetEligibility(new(MaintenanceSafeSpace.Complete, false, false, false));
        Assert.False(await coordinator.TryStartAsync(Request(), null, _ => false));
        Assert.Equal(0, projectRuns);
        Assert.True(await coordinator.TryStartAsync(Request(), null, _ => true));
        Assert.Equal(1, projectRuns);
        Assert.Equal(1, coverRuns);
        Assert.Equal(new[] { "project failure", "cover failure" }, failures);
    }
}
