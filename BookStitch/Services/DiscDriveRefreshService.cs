namespace BookStitch.Services;

public sealed class DiscDriveRefreshService
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<DiscDriveInfo>>> _getShellsAsync;
    private readonly Func<string, CancellationToken, Task<DiscDriveInfo?>> _probeDriveAsync;

    public DiscDriveRefreshService(DiscDriveService discDriveService)
        : this(
            cancellationToken => (discDriveService ?? throw new ArgumentNullException(nameof(discDriveService)))
                .GetCdDriveShellsAsync(cancellationToken),
            (driveRoot, cancellationToken) => discDriveService.ProbeDriveAsync(
                driveRoot,
                operationName: "dialog.refresh",
                cancellationToken))
    {
    }

    internal DiscDriveRefreshService(
        Func<CancellationToken, Task<IReadOnlyList<DiscDriveInfo>>> getShellsAsync,
        Func<string, CancellationToken, Task<DiscDriveInfo?>> probeDriveAsync)
    {
        _getShellsAsync = getShellsAsync ?? throw new ArgumentNullException(nameof(getShellsAsync));
        _probeDriveAsync = probeDriveAsync ?? throw new ArgumentNullException(nameof(probeDriveAsync));
    }

    public async Task RefreshDriveAsync(
        IReadOnlyList<DiscDriveInfo> previous,
        string driveRoot,
        Func<IReadOnlyList<DiscDriveInfo>, Task> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        previous ??= [];

        var current = previous.ToList();
        var index = current.FindIndex(item => SameRoot(item.RootPath, driveRoot));
        if (index < 0)
            return;

        var previousDrive = current[index];
        current[index] = previousDrive with { IsChecking = true };
        await publish(current.ToList()).ConfigureAwait(false);

        try
        {
            var probed = await _probeDriveAsync(previousDrive.RootPath, cancellationToken).ConfigureAwait(false);
            current[index] = probed ?? previousDrive with { IsChecking = false };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            current[index] = previousDrive with { IsChecking = false };
        }

        await publish(current.ToList()).ConfigureAwait(false);
    }

    public async Task RefreshAsync(
        IReadOnlyList<DiscDriveInfo> previous,
        Func<IReadOnlyList<DiscDriveInfo>, Task> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publish);
        previous ??= [];

        var shells = await _getShellsAsync(cancellationToken).ConfigureAwait(false);
        var current = shells
            .Select(shell => previous.FirstOrDefault(item => SameRoot(item.RootPath, shell.RootPath)) is { } existing
                ? existing with { IsChecking = false }
                : shell with { IsChecking = false })
            .ToList();

        await publish(current).ConfigureAwait(false);

        foreach (var shell in shells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = current.FindIndex(item => SameRoot(item.RootPath, shell.RootPath));
            if (index < 0)
                continue;

            current[index] = current[index] with { IsChecking = true };
            await publish(current.ToList()).ConfigureAwait(false);

            try
            {
                var probed = await _probeDriveAsync(shell.RootPath, cancellationToken).ConfigureAwait(false);
                current[index] = probed ?? shell with { IsChecking = false };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                current[index] = current[index] with { IsChecking = false };
            }

            await publish(current.ToList()).ConfigureAwait(false);
        }
    }

    private static bool SameRoot(string left, string right) => string.Equals(
        DiscDriveConfigurationService.NormalizeRootPath(left),
        DiscDriveConfigurationService.NormalizeRootPath(right),
        StringComparison.OrdinalIgnoreCase);
}
