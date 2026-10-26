using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_MarksOnlyCurrentlyProbedDriveAsCheckingAndKeepsPreviousStateForOthers()
    {
        var firstGate = new TaskCompletionSource<DiscDriveInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shells = new[]
        {
            Drive(@"G:\", isReady: false, isChecking: true),
            Drive(@"H:\", isReady: false, isChecking: true)
        };
        var previous = new[]
        {
            Drive(@"G:\", isReady: true, isChecking: false),
            Drive(@"H:\", isReady: true, isChecking: false)
        };
        var published = new List<IReadOnlyList<DiscDriveInfo>>();
        var service = new DiscDriveRefreshService(
            _ => Task.FromResult<IReadOnlyList<DiscDriveInfo>>(shells),
            (root, _) => root.StartsWith("G", StringComparison.OrdinalIgnoreCase)
                ? firstGate.Task
                : Task.FromResult<DiscDriveInfo?>(Drive(@"H:\", isReady: false, isChecking: false)));

        var refreshTask = service.RefreshAsync(previous, state =>
        {
            published.Add(state.ToArray());
            return Task.CompletedTask;
        });

        await Task.Yield();
        Assert.False(refreshTask.IsCompleted);
        var checkingFirst = published.Last();
        Assert.True(checkingFirst.Single(item => item.RootPath == @"G:\").IsChecking);
        Assert.False(checkingFirst.Single(item => item.RootPath == @"H:\").IsChecking);
        Assert.True(checkingFirst.Single(item => item.RootPath == @"H:\").IsReady);

        firstGate.SetResult(Drive(@"G:\", isReady: true, isChecking: false));
        await refreshTask;

        Assert.All(published, state => Assert.True(state.Count(item => item.IsChecking) <= 1));
        var final = published.Last();
        Assert.True(final.Single(item => item.RootPath == @"G:\").IsReady);
        Assert.False(final.Single(item => item.RootPath == @"H:\").IsReady);
    }


    [Fact]
    public async Task RefreshAsync_ProbeFailurePreservesPreviousStateAndContinuesWithNextDrive()
    {
        var shells = new[]
        {
            Drive(@"G:\", isReady: false, isChecking: true),
            Drive(@"H:\", isReady: false, isChecking: true)
        };
        var previous = new[]
        {
            Drive(@"G:\", isReady: true, isChecking: false),
            Drive(@"H:\", isReady: true, isChecking: false)
        };
        var probedRoots = new List<string>();
        IReadOnlyList<DiscDriveInfo>? final = null;
        var service = new DiscDriveRefreshService(
            _ => Task.FromResult<IReadOnlyList<DiscDriveInfo>>(shells),
            (root, _) =>
            {
                probedRoots.Add(root);
                if (root.StartsWith("G", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("simulated drive failure");

                return Task.FromResult<DiscDriveInfo?>(Drive(@"H:\", isReady: false, isChecking: false));
            });

        await service.RefreshAsync(previous, state =>
        {
            final = state.ToArray();
            return Task.CompletedTask;
        });

        Assert.Equal([@"G:\", @"H:\"], probedRoots);
        Assert.NotNull(final);
        Assert.True(final!.Single(item => item.RootPath == @"G:\").IsReady);
        Assert.False(final.Single(item => item.RootPath == @"H:\").IsReady);
        Assert.All(final, item => Assert.False(item.IsChecking));
    }

    [Fact]
    public async Task RefreshDriveAsync_ProbesOnlyRequestedDriveAndPreservesOtherRows()
    {
        var gate = new TaskCompletionSource<DiscDriveInfo?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = new[]
        {
            Drive(@"G:\", isReady: true, isChecking: false),
            Drive(@"H:\", isReady: true, isChecking: false)
        };
        var published = new List<IReadOnlyList<DiscDriveInfo>>();
        var probedRoots = new List<string>();
        var service = new DiscDriveRefreshService(
            _ => throw new InvalidOperationException("Targeted refresh must not enumerate drive shells."),
            (root, _) =>
            {
                probedRoots.Add(root);
                return gate.Task;
            });

        var refreshTask = service.RefreshDriveAsync(previous, @"H:\", state =>
        {
            published.Add(state.ToArray());
            return Task.CompletedTask;
        });

        await Task.Yield();
        var checking = published.Last();
        Assert.False(checking.Single(item => item.RootPath == @"G:\").IsChecking);
        Assert.True(checking.Single(item => item.RootPath == @"G:\").IsReady);
        Assert.True(checking.Single(item => item.RootPath == @"H:\").IsChecking);

        gate.SetResult(Drive(@"H:\", isReady: false, isChecking: false));
        await refreshTask;

        Assert.Equal([@"H:\"], probedRoots);
        var final = published.Last();
        Assert.True(final.Single(item => item.RootPath == @"G:\").IsReady);
        Assert.False(final.Single(item => item.RootPath == @"H:\").IsReady);
    }

    private static DiscDriveInfo Drive(string root, bool isReady, bool isChecking) =>
        new(root, root.TrimEnd('\\'), isReady, string.Empty, isReady ? DiscMediaKind.DataDisc : DiscMediaKind.Empty, "Optical", IsChecking: isChecking);
}
