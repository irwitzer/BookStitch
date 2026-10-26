using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class DiscDriveConfigurationService
{
    public IReadOnlyList<DiscDriveConfigurationItem> Reset(
        AppSettings settings, IReadOnlyList<DiscDriveInfo> detectedDrives)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(detectedDrives);
        settings.DiscDriveOrder = [];
        return Synchronize(settings, detectedDrives);
    }

    public const int MaximumActiveDrives = 5;
    public const int MaximumUserNameLength = 23;

    public IReadOnlyList<DiscDriveConfigurationItem> Synchronize(
        AppSettings settings,
        IReadOnlyList<DiscDriveInfo> detectedDrives,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        detectedDrives ??= [];
        settings.DiscDriveOrder ??= [];

        var timestamp = nowUtc ?? DateTime.UtcNow;
        var existing = NormalizeExisting(settings.DiscDriveOrder);
        var detected = detectedDrives
            .Where(drive => !string.IsNullOrWhiteSpace(drive.RootPath))
            .GroupBy(drive => NormalizeRootPath(drive.RootPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(drive => drive.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reliableIdentityCounts = detected
            .Where(HasReliableIdentity)
            .GroupBy(drive => drive.StableId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var weakFingerprintCounts = detected
            .Where(drive => !string.IsNullOrWhiteSpace(drive.HardwareFingerprint))
            .GroupBy(drive => drive.HardwareFingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var unmatched = new HashSet<ConfiguredDiscDrive>(existing, ReferenceEqualityComparer.Instance);
        var connected = new Dictionary<ConfiguredDiscDrive, DiscDriveInfo>(ReferenceEqualityComparer.Instance);

        foreach (var drive in detected)
        {
            var root = NormalizeRootPath(drive.RootPath);
            var hasUniqueReliableIdentity = HasReliableIdentity(drive) &&
                reliableIdentityCounts.TryGetValue(drive.StableId, out var identityCount) && identityCount == 1;

            ConfiguredDiscDrive? configuration = null;
            if (hasUniqueReliableIdentity)
            {
                configuration = unmatched.FirstOrDefault(item =>
                    string.Equals(item.StableId, drive.StableId, StringComparison.OrdinalIgnoreCase));
            }

            if (configuration is null && hasUniqueReliableIdentity)
            {
                // The current root is used only to migrate a legacy/local entry when a strong ID first appears.
                configuration = unmatched.FirstOrDefault(item =>
                    string.Equals(NormalizeRootPath(item.DriveRoot), root, StringComparison.OrdinalIgnoreCase) &&
                    !HasReliableConfigurationId(item.StableId));
            }
            else if (configuration is null && !string.IsNullOrWhiteSpace(drive.HardwareFingerprint))
            {
                var weakCandidates = unmatched.Where(item =>
                    !HasReliableConfigurationId(item.StableId) &&
                    string.Equals(item.HardwareFingerprint, drive.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)).ToList();
                var fingerprintIsUnique = weakFingerprintCounts.TryGetValue(drive.HardwareFingerprint, out var fingerprintCount) &&
                    fingerprintCount == 1 && weakCandidates.Count == 1;
                configuration = fingerprintIsUnique
                    ? weakCandidates[0]
                    : weakCandidates.FirstOrDefault(item =>
                        string.Equals(NormalizeRootPath(item.DriveRoot), root, StringComparison.OrdinalIgnoreCase));

                // A root-only entry is legacy data, not a hardware match. It may be adopted once,
                // but two model-identical fingerprint entries are never collapsed into one.
                configuration ??= unmatched.FirstOrDefault(item =>
                    string.IsNullOrWhiteSpace(item.StableId) &&
                    string.Equals(NormalizeRootPath(item.DriveRoot), root, StringComparison.OrdinalIgnoreCase));
            }
            else if (configuration is null)
            {
                configuration = unmatched.FirstOrDefault(item =>
                    !HasReliableConfigurationId(item.StableId) &&
                    string.IsNullOrWhiteSpace(item.HardwareFingerprint) &&
                    string.Equals(NormalizeRootPath(item.DriveRoot), root, StringComparison.OrdinalIgnoreCase));
            }

            if (configuration is null)
            {
                configuration = new ConfiguredDiscDrive
                {
                    StableId = hasUniqueReliableIdentity
                        ? drive.StableId
                        : CreateLocalConfigurationId(drive.HardwareFingerprint),
                    IsEnabled = true,
                    Order = existing.Count
                };
                existing.Add(configuration);
            }
            else
            {
                unmatched.Remove(configuration);
                if (hasUniqueReliableIdentity)
                    configuration.StableId = drive.StableId;
                else if (string.IsNullOrWhiteSpace(configuration.StableId))
                    configuration.StableId = CreateLocalConfigurationId(drive.HardwareFingerprint);
            }

            UpdateRuntimeDiagnostics(configuration, drive, timestamp);
            connected[configuration] = drive;
        }

        EnsureUniqueConfigurationIds(existing);
        for (var index = 0; index < existing.Count; index++)
            existing[index].Order = index;

        settings.DiscDriveOrder = existing;
        return existing
            .Select(configuration => new DiscDriveConfigurationItem(
                configuration,
                connected.TryGetValue(configuration, out var driveInfo),
                driveInfo))
            .ToList();
    }

    public IReadOnlyList<DiscDriveConfigurationItem> GetActiveConnectedDrives(
        AppSettings settings,
        IReadOnlyList<DiscDriveInfo> detectedDrives) =>
        Synchronize(settings, detectedDrives)
            .Where(item => item.Configuration.IsEnabled && item.IsConnected)
            .Take(MaximumActiveDrives)
            .ToList();

    public void MoveDrive(AppSettings settings, string stableId, int direction)
    {
        var ordered = GetOrdered(settings);
        var index = FindByStableId(ordered, stableId);
        if (index < 0)
            return;

        var targetIndex = Math.Clamp(index + Math.Sign(direction), 0, ordered.Count - 1);
        if (targetIndex == index)
            return;

        (ordered[index], ordered[targetIndex]) = (ordered[targetIndex], ordered[index]);
        SaveOrder(settings, ordered);
    }

    public void MoveDriveToIndex(AppSettings settings, string stableId, int targetIndex)
    {
        var ordered = GetOrdered(settings);
        var sourceIndex = FindByStableId(ordered, stableId);
        if (sourceIndex < 0 || ordered.Count == 0)
            return;

        targetIndex = Math.Clamp(targetIndex, 0, ordered.Count - 1);
        if (sourceIndex != targetIndex)
        {
            var item = ordered[sourceIndex];
            ordered.RemoveAt(sourceIndex);
            ordered.Insert(targetIndex, item);
        }
        SaveOrder(settings, ordered);
    }

    public void SetEnabled(AppSettings settings, string stableId, bool isEnabled)
    {
        var item = FindConfiguration(settings, stableId);
        if (item is not null)
            item.IsEnabled = isEnabled;
    }

    public void RenameDrive(AppSettings settings, string stableId, string? userName)
    {
        var item = FindConfiguration(settings, stableId);
        if (item is not null)
            item.UserName = NormalizeUserName(userName);
    }

    public static string NormalizeUserName(string? userName)
    {
        var normalized = userName?.Trim() ?? string.Empty;
        return normalized.Length <= MaximumUserNameLength ? normalized : normalized[..MaximumUserNameLength];
    }

    public static string NormalizeRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim();
        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return $"{char.ToUpperInvariant(trimmed[0])}:\\";

        try
        {
            var root = Path.GetPathRoot(trimmed) ?? trimmed;
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch
        {
            return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }

    private static List<ConfiguredDiscDrive> NormalizeExisting(IEnumerable<ConfiguredDiscDrive> configurations)
    {
        var result = new List<ConfiguredDiscDrive>();
        foreach (var item in configurations.Where(item => item is not null).OrderBy(item => item.Order))
        {
            item.StableId = item.StableId?.Trim() ?? string.Empty;
            item.DriveRoot = NormalizeRootPath(item.DriveRoot);
            item.UserName = NormalizeUserName(item.UserName);
            if (string.IsNullOrWhiteSpace(item.StableId) && result.Any(existing =>
                    string.IsNullOrWhiteSpace(existing.StableId) &&
                    string.Equals(existing.DriveRoot, item.DriveRoot, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            result.Add(item);
        }
        return result;
    }

    private static bool HasReliableIdentity(DiscDriveInfo drive) =>
        !string.IsNullOrWhiteSpace(drive.StableId) &&
        drive.IdentityKind is DiscDriveIdentityKind.StorageSerialNumber or DiscDriveIdentityKind.PnpDeviceInstance;

    private static bool HasReliableConfigurationId(string? stableId) =>
        stableId?.StartsWith("serial:", StringComparison.OrdinalIgnoreCase) == true ||
        stableId?.StartsWith("pnp:", StringComparison.OrdinalIgnoreCase) == true;

    private static string CreateLocalConfigurationId(string? fingerprint)
    {
        var prefix = string.IsNullOrWhiteSpace(fingerprint) ? "unknown" : fingerprint.Trim();
        return $"local:{prefix}:{Guid.NewGuid():N}";
    }

    private static void UpdateRuntimeDiagnostics(ConfiguredDiscDrive target, DiscDriveInfo drive, DateTime timestamp)
    {
        target.DriveRoot = NormalizeRootPath(drive.RootPath);
        target.DisplayName = string.IsNullOrWhiteSpace(drive.DriveName) ? "CD-Laufwerk" : drive.DriveName.Trim();
        target.DevicePath = drive.DevicePath?.Trim() ?? string.Empty;
        target.StorageSerialNumber = drive.StorageSerialNumber?.Trim() ?? string.Empty;
        target.PnpDeviceInstanceId = drive.PnpDeviceInstanceId?.Trim() ?? string.Empty;
        target.Vendor = drive.Vendor?.Trim() ?? string.Empty;
        target.Product = drive.Product?.Trim() ?? string.Empty;
        target.Revision = drive.Revision?.Trim() ?? string.Empty;
        target.HardwareFingerprint = drive.HardwareFingerprint?.Trim() ?? string.Empty;
        target.LastSeenUtc = timestamp;
    }

    private static void EnsureUniqueConfigurationIds(IReadOnlyList<ConfiguredDiscDrive> configurations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in configurations)
        {
            if (!string.IsNullOrWhiteSpace(item.StableId) && seen.Add(item.StableId))
                continue;

            item.StableId = CreateLocalConfigurationId(item.HardwareFingerprint);
            seen.Add(item.StableId);
        }
    }

    private static List<ConfiguredDiscDrive> GetOrdered(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DiscDriveOrder ??= [];
        return settings.DiscDriveOrder.OrderBy(item => item.Order).ToList();
    }

    private static int FindByStableId(IReadOnlyList<ConfiguredDiscDrive> configurations, string stableId)
    {
        for (var index = 0; index < configurations.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(stableId) &&
                string.Equals(configurations[index].StableId, stableId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }

    private static ConfiguredDiscDrive? FindConfiguration(AppSettings settings, string stableId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.DiscDriveOrder ??= [];
        return settings.DiscDriveOrder.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(stableId) &&
            string.Equals(item.StableId, stableId, StringComparison.OrdinalIgnoreCase));
    }

    private static void SaveOrder(AppSettings settings, List<ConfiguredDiscDrive> ordered)
    {
        for (var index = 0; index < ordered.Count; index++)
            ordered[index].Order = index;
        settings.DiscDriveOrder = ordered;
    }
}
