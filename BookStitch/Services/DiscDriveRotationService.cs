using BookStitch.Models;

namespace BookStitch.Services;

public sealed class DiscDriveRotationService
{
    public IReadOnlyList<string> BuildRound(
        IReadOnlyList<DiscDriveConfigurationItem> configuredDrives,
        string lastProcessedDriveRoot)
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

        if (activeRoots.Count == 0)
            return [];

        var lastRoot = DiscDriveConfigurationService.NormalizeRootPath(lastProcessedDriveRoot);
        var index = activeRoots.FindIndex(root => string.Equals(root, lastRoot, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return activeRoots;

        return activeRoots
            .Skip(index + 1)
            .Concat(activeRoots.Take(index + 1))
            .ToList();
    }
}
