using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public static class PreparedConvertedTrackReuseService
{
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
}
