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
    public const string AudioDiscManifestFileName = "audio-cd-project.json";
    public const string LegacyAudioDiscManifestFileName = "audio-disc-project.json";
    public const string FolderManifestFileName = "folder-project.json";
    public const string Mp3DiscManifestFileName = "mp3-cd-project.json";
    public const string TrackListStateFileName = "track-list-state.json";
    public const string ProjectMetadataFileName = "project-metadata.json";

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

    public static string GetMetadataFolder(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), "metadata");

    public static string GetSourceMetadataSourcesFolder(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), "source-metadata");

    public static string GetFolderManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), FolderManifestFileName);

    public static string GetMp3DiscManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), Mp3DiscManifestFileName);

    public static string ResolveMp3DiscManifestPath(string projectFolder)
    {
        var preferred = GetMp3DiscManifestPath(projectFolder);
        if (File.Exists(preferred)) return preferred;
        var legacy = ResolveWorkManifestPath(projectFolder);
        return File.Exists(legacy) ? legacy : preferred;
    }

    public static string GetWorkManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), WorkManifestFileName);

    public static string GetExportManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), ExportManifestFileName);

    public static string GetAudioDiscManifestPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), AudioDiscManifestFileName);

    public static string GetTrackListStatePath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), TrackListStateFileName);

    public static string GetProjectMetadataPath(string projectFolder) =>
        Path.Combine(GetSettingsFolder(projectFolder), ProjectMetadataFileName);

    public static string GetSourceMetadataSnapshotPath(string projectFolder, Guid sourceId) =>
        Path.Combine(GetSourceMetadataSourcesFolder(projectFolder), $"{sourceId:N}.json");

    public static string ResolveWorkManifestPath(string projectFolder) =>
        ResolveExistingOrPreferred(GetWorkManifestPath(projectFolder), Path.Combine(projectFolder, WorkManifestFileName));

    public static string ResolveExportManifestPath(string projectFolder)
    {
        var preferred = GetExportManifestPath(projectFolder);
        if (File.Exists(preferred)) return preferred;
        var rootExport = Path.Combine(projectFolder, ExportManifestFileName);
        if (File.Exists(rootExport)) return rootExport;
        var legacy = ResolveWorkManifestPath(projectFolder);
        if (File.Exists(legacy))
        {
            try
            {
                lock (AtomicJsonStore.ForPath(legacy))
                {
                    // Match WorkManifestService.ReadOnly: atomic replacement must remain
                    // possible while this read-only format probe holds the previous file.
                    using var stream = new FileStream(legacy, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var document = System.Text.Json.JsonDocument.Parse(stream);
                    var json = document.RootElement;
                    if (json.TryGetProperty("Tracks", out _) || json.TryGetProperty("Export", out _) || json.TryGetProperty("State", out _))
                        return legacy;
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return preferred;
    }

    public static string ResolveAudioDiscManifestPath(string projectFolder)
    {
        var preferred = GetAudioDiscManifestPath(projectFolder);
        if (File.Exists(preferred)) return preferred;
        var legacy = Path.Combine(GetSettingsFolder(projectFolder), LegacyAudioDiscManifestFileName);
        if (File.Exists(legacy)) return legacy;
        legacy = Path.Combine(projectFolder, LegacyAudioDiscManifestFileName);
        return File.Exists(legacy) ? legacy : preferred;
    }

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
        ProjectFolderLayoutDiagnostics.RecordEnsureProjectFolders(projectFolder);
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(GetOriginalsFolder(projectFolder));
        Directory.CreateDirectory(GetConvertedFolder(projectFolder));
        Directory.CreateDirectory(GetMergeFolder(projectFolder));
        Directory.CreateDirectory(GetSettingsFolder(projectFolder));
        Directory.CreateDirectory(GetSourceMetadataSourcesFolder(projectFolder));
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
