using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public static class PreparedConvertedTrackReuseService
{
    // Locates legacy prepared-file candidates for resume/retention. Export reuse still
    // requires a matching processing action and fingerprint in the work manifest.
    public static bool CanReuseForDiscProject(
        string projectType,
        string sourcePath,
        string convertedPath)
    {
        var isSupportedDiscProject =
            string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase);

        if (!isSupportedDiscProject)
            return false;

        if (!File.Exists(sourcePath) || !File.Exists(convertedPath))
            return false;

        if (convertedPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            convertedPath.EndsWith(".copying", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceInfo = new FileInfo(sourcePath);
        var convertedInfo = new FileInfo(convertedPath);

        if (sourceInfo.Length <= 0 || convertedInfo.Length <= 0)
            return false;

        return convertedInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc;
    }

    internal static int CountReusablePreparedConvertedFiles(string projectFolder, ExportPreset preset)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || preset is null)
            return 0;

        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        if (!Directory.Exists(originalsFolder) || !Directory.Exists(convertedFolder))
            return 0;

        return SafeFileSystemEnumeration.EnumerateFiles(originalsFolder)
            .Where(path => AudioInputFormatCatalog.IsDataDiscImportExtension(Path.GetExtension(path)))
            .Count(sourcePath =>
            {
                var track = CreateTrackForSourcePath(sourcePath);
                var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
                return PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                    ProjectManifestTypes.Mp3DiscProject,
                    sourcePath,
                    convertedPath);
            });
    }

    private static TrackInfo CreateTrackForSourcePath(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).TrimStart('.');
        return new TrackInfo
        {
            FilePath = sourcePath,
            FileName = Path.GetFileName(sourcePath),
            Extension = extension,
            Codec = extension.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "MP3" : string.Empty,
            ProcessingAction = ProcessingActionIds.Convert
        };
    }
}
