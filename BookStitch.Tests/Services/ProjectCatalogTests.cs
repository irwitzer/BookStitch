using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectCatalogTests
{
    [Fact]
    public async Task Concurrent_startup_browser_and_manual_requests_share_index_and_publish_damage_later()
    {
        using var root = new TemporaryFolder();
        using var indexGate = new ManualResetEventSlim();
        using var damageGate = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        using var catalog = new ProjectCatalog((_, _, token) =>
        {
            Interlocked.Increment(ref scans); started.SetResult(); indexGate.Wait(token);
            return new ProjectCatalogIndex([], [], []);
        }, (_, _, token) => { damageGate.Wait(token); return []; });
        var preload = catalog.RefreshAsync(root.Path, 90);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(preload, catalog.RefreshAsync(root.Path, 90));
        Assert.Same(preload, catalog.RefreshAsync(root.Path, 90, force: true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preload.WaitAsync(cancellation.Token));
        indexGate.Set();
        var first = await preload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.DamageComplete);
        Assert.Equal(1, scans);
        damageGate.Set();
        var final = await catalog.WaitForDamageAsync(first.Generation, CancellationToken.None);
        Assert.True(final!.DamageComplete);
        Assert.Equal(first.Generation, final.Generation);
        Assert.Same(final, await catalog.RefreshAsync(root.Path, 90));
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task Dirty_generation_suppresses_old_damage_and_serializes_the_next_scan()
    {
        using var root = new TemporaryFolder();
        using var gate = new ManualResetEventSlim();
        var damageStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        using var catalog = new ProjectCatalog((_, _, _) =>
            { Interlocked.Increment(ref scans); return new ProjectCatalogIndex([], [], []); },
            (_, _, token) => { if (scans == 1) { damageStarted.SetResult(); gate.Wait(token); } return []; });
        var first = await catalog.RefreshAsync(root.Path, 60);
        await damageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        catalog.MarkDirty();
        Assert.True(catalog.Snapshot!.IsDirty);
        var next = catalog.RefreshAsync(root.Path, 60);
        Assert.Equal(1, scans);
        gate.Set();
        var second = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(second.Generation > first.Generation);
        Assert.Null(await catalog.WaitForDamageAsync(first.Generation, CancellationToken.None));
        Assert.Equal(second.Generation, catalog.Snapshot!.Generation);
        Assert.Equal(2, scans);
    }

    [Fact]
    public async Task Changing_working_root_suppresses_old_generation_results()
    {
        using var firstRoot = new TemporaryFolder();
        using var secondRoot = new TemporaryFolder();
        using var gate = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var catalog = new ProjectCatalog((_, _, _) => new ProjectCatalogIndex([], [], []),
            (root, _, token) =>
            {
                if (root == firstRoot.Path) { started.SetResult(); gate.Wait(token); }
                return [];
            });
        var first = await catalog.RefreshAsync(firstRoot.Path, 90);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = catalog.RefreshAsync(secondRoot.Path, 90);
        Assert.Null(catalog.Snapshot);
        gate.Set();
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(second.Generation > first.Generation);
        Assert.Equal(secondRoot.Path, catalog.Snapshot!.WorkingRoot);
        Assert.Null(await catalog.WaitForDamageAsync(first.Generation, CancellationToken.None));
    }

    [Fact]
    public async Task Failed_damage_phase_retains_index_but_marks_snapshot_dirty_for_retry()
    {
        using var root = new TemporaryFolder();
        using var gate = new ManualResetEventSlim();
        var fail = true;
        using var catalog = new ProjectCatalog((_, _, _) => new ProjectCatalogIndex([], [], []),
            (_, _, token) => { gate.Wait(token); if (fail) throw new IOException("damage fixture"); return []; });
        var first = await catalog.RefreshAsync(root.Path, 30);
        gate.Set();
        Assert.Null(await catalog.WaitForDamageAsync(first.Generation, CancellationToken.None));
        Assert.True(catalog.Snapshot!.IsDirty);
        fail = false;
        var retry = await catalog.RefreshAsync(root.Path, 30);
        Assert.True(retry.Generation > first.Generation);
        Assert.NotNull(await catalog.WaitForDamageAsync(retry.Generation, CancellationToken.None));
        Assert.False(catalog.Snapshot!.IsDirty);
    }

}
