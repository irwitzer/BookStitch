using System.IO;

namespace BookStitch.Services;

public static class ProjectFolderLayout
{
    public const string OriginalsFolderName = "originals";
    public const string ConvertedFolderName = "converted";
    public const string MergeFolderName = "merge";
    public const string SettingsFolderName = "project-settings";

    public const string WorkManifestFileName = "project.json";
    public const string ExportManifestFileName = "export-project.json";
    public const string AudioDiscManifestFileName = "audio-disc-project.json";
    public const string TrackListStateFileName = "track-list-state.json";

    public static string GetOriginalsFolder(string projectFolder) =>
        Path.Combine(projectFolder, OriginalsFolderName);

    public static string GetDiscOriginalsFolder(string projectFolder, int discNumber) =>
        Path.Combine(GetOriginalsFolder(projectFolder), $"CD {discNumber:00}");

    public static string GetConvertedFolder(string projectFolder) =>
        Path.Combine(projectFolder, ConvertedFolderName);

    public static string GetConvertedPresetFolder(string projectFolder, string presetFolderName) =>
        Path.Combine(GetConvertedFolder(projectFolder), presetFolderName);

    public static string GetMergeFolder(string projectFolder) =>
        Path.Combine(projectFolder, MergeFolderName);

    public static string GetSettingsFolder(string projectFolder) =>
        Path.Combine(projectFolder, SettingsFolderName);

    public static string GetWorkManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), WorkManifestFileName);

    public static string GetExportManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), ExportManifestFileName);

    public static string GetAudioDiscManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), AudioDiscManifestFileName);

    public static string GetTrackListStatePath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), TrackListStateFileName);

    public static string ResolveWorkManifestPath(string projectFolder) =>
        ResolveExistingOrPreferred(GetWorkManifestPath(projectFolder), Path.Combine(projectFolder, WorkManifestFileName));

    public static string ResolveExportManifestPath(string projectFolder) =>
        ResolveExistingOrPreferred(GetExportManifestPath(projectFolder), Path.Combine(projectFolder, ExportManifestFileName));

    public static string ResolveAudioDiscManifestPath(string projectFolder) =>
        ResolveExistingOrPreferred(GetAudioDiscManifestPath(projectFolder), Path.Combine(projectFolder, AudioDiscManifestFileName));

    public static string ResolveTrackListStatePath(string projectFolder) =>
        ResolveExistingOrPreferred(GetTrackListStatePath(projectFolder), Path.Combine(projectFolder, TrackListStateFileName));

    public static string ResolveOriginalsFolder(string projectFolder)
    {
        var preferred = GetOriginalsFolder(projectFolder);
        var legacyRipped = Path.Combine(projectFolder, "ripped");
        if (Directory.Exists(legacyRipped) &&
            (!Directory.Exists(preferred) || !Directory.EnumerateFileSystemEntries(preferred).Any()))
        {
            return legacyRipped;
        }
        return preferred;
    }

    public static string ResolveDiscOriginalsFolder(string projectFolder, int discNumber)
    {
        var preferred = GetDiscOriginalsFolder(projectFolder, discNumber);
        var legacy = Path.Combine(projectFolder, $"CD {discNumber:00}");
        if (Directory.Exists(legacy) &&
            (!Directory.Exists(preferred) || !Directory.EnumerateFileSystemEntries(preferred).Any()))
        {
            return legacy;
        }
        return preferred;
    }

    public static void EnsureProjectFolders(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(GetOriginalsFolder(projectFolder));
        Directory.CreateDirectory(GetConvertedFolder(projectFolder));
        Directory.CreateDirectory(GetMergeFolder(projectFolder));
        Directory.CreateDirectory(GetSettingsFolder(projectFolder));
    }

    public static string GetProjectFolderFromManifestPath(string manifestPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty;
        return string.Equals(Path.GetFileName(directory), SettingsFolderName, StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(directory)?.FullName ?? directory
            : directory;
    }

    private static string ResolveExistingOrPreferred(string preferred, string legacy)
    {
        if (File.Exists(preferred))
            return preferred;
        return File.Exists(legacy) ? legacy : preferred;
    }
}
