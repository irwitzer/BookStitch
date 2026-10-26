using BookStitch.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed record PerformanceDrivePathRole(
    string? Path,
    PerformanceDriveRole Roles);

/// <summary>
/// Captures a best-effort, one-shot snapshot of the drives relevant to one
/// performance diagnostics session. The service deliberately reuses the
/// existing BookStitch optical-drive metadata instead of probing optical
/// hardware through a second subsystem.
/// </summary>
public sealed class PerformanceDriveContextService
{
    public IReadOnlyList<PerformanceDriveSnapshot> Capture(
        IReadOnlyList<PerformanceDrivePathRole> pathRoles,
        AppSettings? settings = null,
        IReadOnlyList<DiscDriveInfo>? detectedOpticalDrives = null)
    {
        ArgumentNullException.ThrowIfNull(pathRoles);
        detectedOpticalDrives ??= [];

        var configuredOpticalDrives = BuildConfigurationLookup(settings);
        var opticalDrives = BuildOpticalDriveLookup(detectedOpticalDrives);
        var rolesByRoot = new Dictionary<string, PerformanceDriveRole>(StringComparer.OrdinalIgnoreCase);

        foreach (var pathRole in pathRoles)
        {
            if (pathRole.Roles == PerformanceDriveRole.None ||
                string.IsNullOrWhiteSpace(pathRole.Path))
            {
                continue;
            }

            var root = NormalizeRoot(pathRole.Path);
            if (string.IsNullOrWhiteSpace(root))
                continue;

            rolesByRoot.TryGetValue(root, out var existingRoles);
            rolesByRoot[root] = existingRoles | pathRole.Roles;
        }

        return rolesByRoot
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => CaptureDrive(
                pair.Key,
                pair.Value,
                configuredOpticalDrives,
                opticalDrives))
            .ToList();
    }

    public static Guid CreateDriveIdForPath(string? path)
    {
        var root = NormalizeRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return Guid.Empty;

        var bytes = Encoding.UTF8.GetBytes(root.ToUpperInvariant());
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static PerformanceDriveSnapshot CaptureDrive(
        string root,
        PerformanceDriveRole roles,
        IReadOnlyDictionary<string, ConfiguredDiscDrive> configuredOpticalDrives,
        IReadOnlyDictionary<string, DiscDriveInfo> opticalDrives)
    {
        string? driveType = null;
        string? volumeLabel = null;
        string? fileSystem = null;
        long? totalSizeBytes = null;
        long? availableFreeSpaceBytes = null;
        string? deviceModel = null;
        string? mediaType = null;
        string? opticalDriveDisplayName = null;
        var isOptical = false;

        try
        {
            var drive = new DriveInfo(root);
            driveType = drive.DriveType.ToString();

            if (drive.DriveType == DriveType.CDRom)
                isOptical = true;

            if (drive.IsReady)
            {
                volumeLabel = NullIfWhiteSpace(drive.VolumeLabel);
                fileSystem = NullIfWhiteSpace(drive.DriveFormat);
                totalSizeBytes = drive.TotalSize;
                availableFreeSpaceBytes = drive.AvailableFreeSpace;
            }
        }
        catch
        {
            // Drive context is diagnostic-only. An unavailable, sleeping or
            // disconnected drive still produces a useful root/role snapshot.
        }

        if (opticalDrives.TryGetValue(root, out var opticalDrive))
        {
            isOptical = true;
            driveType = DriveType.CDRom.ToString();
            volumeLabel = NullIfWhiteSpace(opticalDrive.VolumeLabel) ?? volumeLabel;
            deviceModel = NullIfWhiteSpace(opticalDrive.DriveName);
            mediaType = opticalDrive.MediaKind.ToString();
            opticalDriveDisplayName = NullIfWhiteSpace(opticalDrive.DiagnosticDriveName);
        }

        if (configuredOpticalDrives.TryGetValue(root, out var configuration))
        {
            isOptical = true;
            driveType = DriveType.CDRom.ToString();

            var configuredDisplayName = NullIfWhiteSpace(configuration.DisplayName);
            if (configuredDisplayName is not null)
                opticalDriveDisplayName = configuredDisplayName;
        }

        if (isOptical && (roles & PerformanceDriveRole.Source) != 0)
            roles |= PerformanceDriveRole.OpticalSource;

        return new PerformanceDriveSnapshot
        {
            DriveId = CreateDriveIdForPath(root),
            DriveRoot = root,
            Roles = roles,
            DriveType = driveType,
            VolumeLabel = volumeLabel,
            FileSystem = fileSystem,
            TotalSizeBytes = totalSizeBytes,
            AvailableFreeSpaceBytes = availableFreeSpaceBytes,
            DeviceModel = deviceModel,
            BusType = null,
            MediaType = mediaType,
            OpticalDriveDisplayName = opticalDriveDisplayName,

            // ConfiguredDiscDrive.DisplayName is currently generated from the
            // Windows drive root/model and is not a user-defined nickname.
            // The future canonical nickname feature can populate this field.
            UserNickname = null
        };
    }

    private static Dictionary<string, ConfiguredDiscDrive> BuildConfigurationLookup(AppSettings? settings)
    {
        if (settings?.DiscDriveOrder is null)
            return new Dictionary<string, ConfiguredDiscDrive>(StringComparer.OrdinalIgnoreCase);

        return settings.DiscDriveOrder
            .Where(configuration => !string.IsNullOrWhiteSpace(configuration.DriveRoot))
            .Select(configuration => new
            {
                Root = NormalizeRoot(configuration.DriveRoot),
                Configuration = configuration
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Root))
            .GroupBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Configuration,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, DiscDriveInfo> BuildOpticalDriveLookup(
        IReadOnlyList<DiscDriveInfo> detectedOpticalDrives)
    {
        return detectedOpticalDrives
            .Where(drive => !string.IsNullOrWhiteSpace(drive.RootPath))
            .Select(drive => new
            {
                Root = NormalizeRoot(drive.RootPath),
                Drive = drive
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Root))
            .GroupBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Drive,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRoot(string? path) =>
        DiscDriveConfigurationService.NormalizeRootPath(path);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
