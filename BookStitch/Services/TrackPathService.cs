using BookStitch.Models;
using System;
using System.IO;

namespace BookStitch.Services;

public static class TrackPathService
{
    public static string GetTrackPath(string folderPath, TrackInfo track)
    {
        if (!string.IsNullOrWhiteSpace(track.FilePath) && Path.IsPathRooted(track.FilePath))
            return track.FilePath;

        var relativeFolder = track.RelativeFolder;

        if (string.IsNullOrWhiteSpace(relativeFolder) || relativeFolder == ".")
            return Path.Combine(folderPath, track.FileName);

        return Path.Combine(folderPath, relativeFolder, track.FileName);
    }

    public static bool PathEquals(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            return false;

        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPathInsideFolder(string path, string folder)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullFolder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
