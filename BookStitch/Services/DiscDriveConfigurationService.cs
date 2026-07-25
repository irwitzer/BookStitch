using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class DiscDriveConfigurationService
{
    public const int MaximumActiveDrives = 5;

    public IReadOnlyList<DiscDriveConfigurationItem> Synchronize(
        AppSettings settings,
        IReadOnlyList<DiscDriveInfo> detectedDrives,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        detectedDrives ??= [];

        settings.DiscDriveOrder ??= [];
        var timestamp = nowUtc ?? DateTime.UtcNow;
        var detectedByRoot = detectedDrives
            .Where(drive => !string.IsNullOrWhiteSpace(drive.RootPath))
            .GroupBy(drive => NormalizeRootPath(drive.RootPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var normalizedExisting = settings.DiscDriveOrder
            .Where(item => !string.IsNullOrWhiteSpace(item.DriveRoot))
            .GroupBy(item => NormalizeRootPath(item.DriveRoot), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Order).First())
            .OrderBy(item => item.Order)
            .ToList();

        foreach (var existing in normalizedExisting)
        {
            existing.DriveRoot = NormalizeRootPath(existing.DriveRoot);
            if (!detectedByRoot.TryGetValue(existing.DriveRoot, out var detected))
                continue;

            existing.DisplayName = BuildDisplayName(detected);
            existing.DevicePath = detected.DevicePath ?? string.Empty;
            existing.LastSeenUtc = timestamp;
        }

        var knownRoots = normalizedExisting
            .Select(item => NormalizeRootPath(item.DriveRoot))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var detected in detectedByRoot.Values.OrderBy(drive => drive.DriveLetter, StringComparer.OrdinalIgnoreCase))
        {
            var root = NormalizeRootPath(detected.RootPath);
            if (knownRoots.Contains(root))
                continue;

            normalizedExisting.Add(new ConfiguredDiscDrive
            {
                DriveRoot = root,
                DisplayName = BuildDisplayName(detected),
                DevicePath = detected.DevicePath ?? string.Empty,
                IsEnabled = true,
                LastSeenUtc = timestamp
            });
            knownRoots.Add(root);
        }

        for (var index = 0; index < normalizedExisting.Count; index++)
            normalizedExisting[index].Order = index;

        settings.DiscDriveOrder = normalizedExisting;

        return normalizedExisting
            .Select(item =>
            {
                detectedByRoot.TryGetValue(NormalizeRootPath(item.DriveRoot), out var driveInfo);
                return new DiscDriveConfigurationItem(
                    item,
                    IsConnected: driveInfo is not null,
                    DriveInfo: driveInfo);
            })
            .ToList();
    }

    public IReadOnlyList<DiscDriveConfigurationItem> GetActiveConnectedDrives(
        AppSettings settings,
        IReadOnlyList<DiscDriveInfo> detectedDrives)
    {
        return Synchronize(settings, detectedDrives)
            .Where(item => item.Configuration.IsEnabled && item.IsConnected)
            .Take(MaximumActiveDrives)
            .ToList();
    }

    public void MoveDrive(AppSettings settings, string driveRoot, int direction)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DiscDriveOrder ??= [];
        var root = NormalizeRootPath(driveRoot);
        var ordered = settings.DiscDriveOrder.OrderBy(item => item.Order).ToList();
        var index = ordered.FindIndex(item => string.Equals(NormalizeRootPath(item.DriveRoot), root, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        var targetIndex = Math.Clamp(index + Math.Sign(direction), 0, ordered.Count - 1);
        if (targetIndex == index)
            return;

        (ordered[index], ordered[targetIndex]) = (ordered[targetIndex], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;
        settings.DiscDriveOrder = ordered;
    }

    public void SetEnabled(AppSettings settings, string driveRoot, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DiscDriveOrder ??= [];
        var root = NormalizeRootPath(driveRoot);
        var item = settings.DiscDriveOrder.FirstOrDefault(entry =>
            string.Equals(NormalizeRootPath(entry.DriveRoot), root, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            item.IsEnabled = isEnabled;
    }

    public static string NormalizeRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            var root = Path.GetPathRoot(path.Trim()) ?? path.Trim();
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }

    private static string BuildDisplayName(DiscDriveInfo drive)
    {
        var root = NormalizeRootPath(drive.RootPath);
        var name = string.IsNullOrWhiteSpace(drive.DriveName)
            ? "CD-Laufwerk"
            : drive.DriveName.Trim();
        return string.IsNullOrWhiteSpace(root)
            ? name
            : $"{root} {name}".Trim();
    }
}

public sealed record DiscDriveConfigurationItem(
    ConfiguredDiscDrive Configuration,
    bool IsConnected,
    DiscDriveInfo? DriveInfo);
