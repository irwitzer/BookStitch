using BookStitch.Models;

namespace BookStitch.Services;

public sealed class DiscDriveRotationService
{
    public IReadOnlyList<string> BuildRound(
        IReadOnlyList<DiscDriveConfigurationItem> configuredDrives,
        string lastProcessedDriveRoot,
        bool startWithSelectedDrive = false)
    {
        configuredDrives ??= [];
        var activeRoots = configuredDrives
            .Where(item => item.Configuration.IsEnabled && item.IsConnected)
            .OrderBy(item => item.Configuration.Order)
            .Select(item => DiscDriveConfigurationService.NormalizeRootPath(item.Configuration.DriveRoot))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(DiscDriveConfigurationService.MaximumActiveDrives)
            .ToList();

        var lastRoot = DiscDriveConfigurationService.NormalizeRootPath(lastProcessedDriveRoot);
        var index = activeRoots.FindIndex(root => string.Equals(root, lastRoot, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return startWithSelectedDrive && lastRoot.Length > 0
                ? new[] { lastRoot }.Concat(activeRoots).ToArray()
                : activeRoots;

        if (startWithSelectedDrive)
            return activeRoots.Skip(index).Concat(activeRoots.Take(index)).ToArray();

        return activeRoots
            .Skip(index + 1)
            .Concat(activeRoots.Take(index + 1))
            .ToList();
    }
}
