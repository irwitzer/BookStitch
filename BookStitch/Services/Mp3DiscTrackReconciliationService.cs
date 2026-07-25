using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class Mp3DiscTrackReconciliationService
{
    public int AppendMissingPreviewTracks(
        IList<TrackInfo> existingTracks,
        IEnumerable<TrackInfo> previewTracks,
        string sourceFolder,
        int discNumber,
        bool clearExistingTracks)
    {
        if (clearExistingTracks)
            existingTracks.Clear();

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existingTrack in existingTracks)
        {
            var identity = BuildIdentity(existingTrack, sourceFolder, discNumber);
            if (!string.IsNullOrWhiteSpace(identity))
                identities.Add(identity);
        }

        var added = 0;
        foreach (var previewTrack in previewTracks)
        {
            previewTrack.DiscNumber = discNumber;
            var identity = BuildIdentity(previewTrack, sourceFolder, discNumber);
            if (!string.IsNullOrWhiteSpace(identity) && !identities.Add(identity))
                continue;

            existingTracks.Add(previewTrack);
            added++;
        }

        return added;
    }

    public int ReconcileImportedTrackPathsForExistingDiscs(
        IList<TrackInfo> tracks,
        string sourceFolder,
        string projectFolder,
        int totalDiscs)
    {
        if (totalDiscs <= 0)
            return 0;

        var changed = 0;
        for (var discNumber = 1; discNumber <= totalDiscs; discNumber++)
        {
            changed += ReconcileImportedTrackPaths(
                tracks,
                sourceFolder,
                projectFolder,
                discNumber);
        }

        return changed;
    }

    public int ReconcileImportedTrackPaths(
        IList<TrackInfo> tracks,
        string sourceFolder,
        string projectFolder,
        int discNumber)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) ||
            string.IsNullOrWhiteSpace(projectFolder) ||
            discNumber <= 0)
        {
            return 0;
        }

        var changed = 0;
        var discFolder = ProjectFolderLayout.ResolveDiscOriginalsFolder(projectFolder, discNumber);

        foreach (var track in tracks.Where(track => track.DiscNumber == discNumber))
        {
            var relativePath = TryGetRelativePath(sourceFolder, track.FilePath);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            var importedPath = Path.Combine(discFolder, relativePath);
            if (!File.Exists(importedPath) || PathsEqual(track.FilePath, importedPath))
                continue;

            track.FilePath = importedPath;
            track.FileName = Path.GetFileName(importedPath);
            track.RelativeFolder = Path.GetDirectoryName(relativePath) ?? string.Empty;
            changed++;
        }

        return changed;
    }

    public TrackInfo? FindTrackForCopiedFile(
        IEnumerable<TrackInfo> tracks,
        DiscCopiedFile copiedFile)
    {
        var relativePath = BuildImportedRelativePath(copiedFile.ImportedFile, copiedFile.DiscNumber);
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        return tracks.FirstOrDefault(track =>
            track.DiscNumber == copiedFile.DiscNumber &&
            string.Equals(
                NormalizeRelativePath(BuildImportedRelativePath(track.FilePath, copiedFile.DiscNumber) ?? BuildTrackRelativePath(track)),
                NormalizeRelativePath(relativePath),
                StringComparison.OrdinalIgnoreCase));
    }

    public string GetImportedRelativeFolder(DiscCopiedFile copiedFile)
    {
        var relativePath = BuildImportedRelativePath(copiedFile.ImportedFile, copiedFile.DiscNumber);
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : Path.GetDirectoryName(relativePath) ?? string.Empty;
    }

    private static string BuildIdentity(TrackInfo track, string sourceFolder, int fallbackDiscNumber)
    {
        var discNumber = track.DiscNumber.GetValueOrDefault(fallbackDiscNumber);
        var relativePath = TryGetRelativePath(sourceFolder, track.FilePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            relativePath = BuildImportedRelativePath(track.FilePath, discNumber)
                ?? BuildTrackRelativePath(track)
                ?? string.Empty;
        }

        relativePath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : $"disc:{discNumber}:path:{relativePath}";
    }


    private static string? BuildTrackRelativePath(TrackInfo track)
    {
        if (string.IsNullOrWhiteSpace(track.FileName))
            return null;

        return string.IsNullOrWhiteSpace(track.RelativeFolder)
            ? track.FileName
            : Path.Combine(track.RelativeFolder, track.FileName);
    }

    private static string? BuildImportedRelativePath(string? filePath, int discNumber)
    {
        if (string.IsNullOrWhiteSpace(filePath) || discNumber <= 0)
            return null;

        var marker = $"CD {discNumber:00}{Path.DirectorySeparatorChar}";
        var normalized = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var markerIndex = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        return normalized[(markerIndex + marker.Length)..];
    }

    private static string? TryGetRelativePath(string sourceFolder, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            var sourceRoot = Path.GetFullPath(sourceFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.GetRelativePath(sourceRoot, fullPath);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
