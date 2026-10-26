using BookStitch.Models;

namespace BookStitch.Services;

public sealed record DiscDriveSelectionPresentation(
    DiscDriveInfo DriveInfo,
    string DriveRoot,
    string DriveLetter,
    string DisplayName,
    string StatusText,
    string UserName,
    int Order,
    bool IsEnabled);

public sealed class DiscDriveSelectionPresentationService
{
    private readonly DiscDriveConfigurationService _configurationService = new();

    public IReadOnlyList<DiscDriveSelectionPresentation> Create(
        IReadOnlyList<DiscDriveInfo> detectedDrives,
        IReadOnlyList<ConfiguredDiscDrive>? configuredDrives,
        bool respectDriveCircleEnabled = true)
    {
        detectedDrives ??= [];
        var scratch = new AppSettings
        {
            DiscDriveOrder = (configuredDrives ?? [])
                .Select(CloneConfiguration)
                .ToList()
        };

        return _configurationService.Synchronize(scratch, detectedDrives)
            .Where(item => item.IsConnected && item.DriveInfo is not null)
            .Select(item => (DriveInfo: item.DriveInfo!, Configuration: ResolveDisplayConfiguration(
                item.Configuration, item.DriveInfo!, configuredDrives)))
            .OrderBy(item => item.Configuration.Order)
            .ThenBy(item => item.DriveInfo.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .Select(item => new DiscDriveSelectionPresentation(
                item.DriveInfo,
                item.DriveInfo.RootPath,
                item.DriveInfo.DriveLetter,
                item.DriveInfo.DisplayName,
                (!respectDriveCircleEnabled || item.Configuration.IsEnabled) ? item.DriveInfo.StatusText : LocalizationText.Get("Disc.Drive.DisabledInDriveCircle"),
                item.Configuration.UserName,
                item.Configuration.Order,
                !respectDriveCircleEnabled || item.Configuration.IsEnabled))
            .ToList();
    }

    private static ConfiguredDiscDrive ResolveDisplayConfiguration(
        ConfiguredDiscDrive synchronized,
        DiscDriveInfo drive,
        IReadOnlyList<ConfiguredDiscDrive>? saved)
    {
        // A shell has no strong hardware identity yet. Use its unique saved root for display only;
        // Synchronize still owns persisted identity and the fully probed matching rules.
        if (drive.IdentityKind is (DiscDriveIdentityKind.StorageSerialNumber or DiscDriveIdentityKind.PnpDeviceInstance) &&
            !string.IsNullOrWhiteSpace(drive.StableId))
            return synchronized;

        if ((saved ?? []).Any(item => string.Equals(item.StableId, synchronized.StableId, StringComparison.OrdinalIgnoreCase)))
            return synchronized;

        var root = DiscDriveConfigurationService.NormalizeRootPath(drive.RootPath);
        var matches = (saved ?? [])
            .Where(item => string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(item.DriveRoot), root,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : synchronized;
    }

    public static DiscDriveSelectionPresentation? SelectPreferred(
        IReadOnlyList<DiscDriveSelectionPresentation> drives,
        string? preferredDriveRoot)
    {
        if (!string.IsNullOrWhiteSpace(preferredDriveRoot))
        {
            var normalized = DiscDriveConfigurationService.NormalizeRootPath(preferredDriveRoot);
            var preferred = drives.FirstOrDefault(item => item.IsEnabled && string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(item.DriveRoot),
                normalized,
                StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }
        return drives.FirstOrDefault(item => item.IsEnabled);
    }

    public static string FormatStatusDriveLabel(string? driveRoot, IReadOnlyList<ConfiguredDiscDrive>? configuredDrives)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            return string.Empty;

        var root = DiscDriveConfigurationService.NormalizeRootPath(driveRoot);
        var letter = root.TrimEnd('\\', '/');
        var matches = (configuredDrives ?? [])
            .Where(item => string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(item.DriveRoot), root,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        var name = matches.Length == 1 ? matches[0].UserName?.Trim() : null;
        return string.IsNullOrWhiteSpace(name) ? letter : $"{letter} {name}";
    }

    private static ConfiguredDiscDrive CloneConfiguration(ConfiguredDiscDrive source) => new()
    {
        StableId = source.StableId,
        DriveRoot = source.DriveRoot,
        DisplayName = source.DisplayName,
        UserName = source.UserName,
        DevicePath = source.DevicePath,
        StorageSerialNumber = source.StorageSerialNumber,
        PnpDeviceInstanceId = source.PnpDeviceInstanceId,
        Vendor = source.Vendor,
        Product = source.Product,
        Revision = source.Revision,
        HardwareFingerprint = source.HardwareFingerprint,
        IsEnabled = source.IsEnabled,
        Order = source.Order,
        LastSeenUtc = source.LastSeenUtc
    };
}
