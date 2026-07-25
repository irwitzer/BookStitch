using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class TrackWorkspaceFilterService
{
    public IReadOnlyList<TrackInfo> GetMp3DiscGeneratedTracks(IEnumerable<TrackInfo> tracks)
    {
        return tracks
            .Where(track => IsMp3DiscGeneratedFolder(track.RelativeFolder))
            .ToList();
    }

    public IReadOnlyList<TrackInfo> GetGeneratedOrWorkTracks(
        string sourceFolder,
        IEnumerable<TrackInfo> tracks,
        string expectedOutputInSourceFolder,
        string? workRoot)
    {
        return tracks
            .Where(track => ShouldIgnoreScannedTrack(sourceFolder, track, expectedOutputInSourceFolder, workRoot))
            .ToList();
    }


    public bool IsMp3DiscGeneratedPath(string projectFolder, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            var relativePath = Path.GetRelativePath(projectFolder, filePath);
            if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            return IsMp3DiscGeneratedFolder(Path.GetDirectoryName(relativePath));
        }
        catch
        {
            return false;
        }
    }

    public bool IsMp3DiscGeneratedFolder(string? relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder))
            return false;

        var firstSegment = relativeFolder
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";

        return firstSegment.Equals(ProjectFolderLayout.ConvertedFolderName, StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals(ProjectFolderLayout.MergeFolderName, StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals(ProjectFolderLayout.SettingsFolderName, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldIgnoreScannedTrack(string sourceFolder, TrackInfo track, string expectedOutputInSourceFolder, string? workRoot)
    {
        var path = TrackPathService.GetTrackPath(sourceFolder, track);
        var extension = Path.GetExtension(path);

        if (ConvertedFileCleanupService.IsPartFilePath(path))
            return true;

        if (TrackPathService.PathEquals(path, expectedOutputInSourceFolder))
            return true;

        if (!string.IsNullOrWhiteSpace(workRoot) && TrackPathService.IsPathInsideFolder(path, workRoot))
            return true;

        var relativeFolder = track.RelativeFolder ?? "";

        if (relativeFolder.Contains("_BookStitch_Work", StringComparison.OrdinalIgnoreCase))
            return true;

        if (relativeFolder.Contains($"BookStitch{Path.DirectorySeparatorChar}Work", StringComparison.OrdinalIgnoreCase) ||
            relativeFolder.Contains($"BookStitch{Path.AltDirectorySeparatorChar}Work", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return extension.Equals(".part", StringComparison.OrdinalIgnoreCase);
    }
}
