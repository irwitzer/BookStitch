using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class Mp3DiscTrackReconciliationService
{
    public IReadOnlyList<TrackInfo> CreatePreviewTracksFromKnownFiles(
        string sourceFolder,
        IEnumerable<string> filePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(filePaths);

        return filePaths
            .Select(filePath => new
            {
                FilePath = filePath,
                RelativePath = TryGetRelativePath(sourceFolder, filePath) ?? Path.GetFileName(filePath)
            })
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select((item, index) => new TrackInfo
            {
                Index = index + 1,
                FilePath = item.FilePath,
                FileName = Path.GetFileName(item.FilePath),
                RelativeFolder = Path.GetDirectoryName(item.RelativePath) ?? string.Empty,
                Extension = Path.GetExtension(item.FilePath).TrimStart('.').ToUpperInvariant(),
                HideFolderDisplay = true
            })
            .ToList();
    }

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
            if (existingTrack.DiscNumber.HasValue)
                existingTrack.HideFolderDisplay = true;

            var identity = BuildIdentity(existingTrack, sourceFolder, discNumber);
            if (!string.IsNullOrWhiteSpace(identity))
                identities.Add(identity);
        }

        var added = 0;
        foreach (var previewTrack in previewTracks)
        {
            previewTrack.DiscNumber = discNumber;
            previewTrack.HideFolderDisplay = true;
            var identity = BuildIdentity(previewTrack, sourceFolder, discNumber);
            if (!string.IsNullOrWhiteSpace(identity) && !identities.Add(identity))
                continue;

            existingTracks.Add(previewTrack);
            added++;
        }

        return added;
    }


    public void NormalizeProjectDiscNumber(
        IEnumerable<TrackInfo> tracks,
        int discNumber)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        if (discNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(discNumber));

        foreach (var track in tracks)
            track.DiscNumber = discNumber;
    }


    public int ApplyPlannedSourceIds(
        IEnumerable<TrackInfo> tracks,
        string sourceFolder,
        int discNumber,
        IReadOnlyCollection<Mp3DiscPlannedSourceIdentity> plannedSources)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(plannedSources);

        var plannedByPath = plannedSources
            .Where(source => source.SourceId != Guid.Empty && !string.IsNullOrWhiteSpace(source.RelativePath))
            .GroupBy(source => Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(source.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().SourceId, StringComparer.OrdinalIgnoreCase);

        var changed = 0;
        foreach (var track in tracks.Where(track => track.DiscNumber == discNumber))
        {
            var relativePath = TryGetRelativePath(sourceFolder, track.FilePath)
                ?? BuildImportedRelativePath(track.FilePath, discNumber)
                ?? BuildTrackRelativePath(track);
            var normalizedRelativePath = Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(relativePath);
            if (string.IsNullOrWhiteSpace(normalizedRelativePath) ||
                !plannedByPath.TryGetValue(normalizedRelativePath, out var sourceId))
            {
                continue;
            }

            track.DiscNumber = discNumber;
            track.SourceId = sourceId;
            track.HideFolderDisplay = true;
            changed++;
        }

        return changed;
    }

    public void ApplyManifestDiscOrder(
        IList<TrackInfo> tracks,
        string projectFolder,
        Mp3DiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(manifest);

        if (tracks.Count == 0)
            return;

        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var manifestSourcesByPath = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .SelectMany(disc => disc.SourceFiles.Select(sourceFile => new
            {
                disc.DiscNumber,
                SourceFile = sourceFile,
                FullPath = string.IsNullOrWhiteSpace(sourceFile.RelativePath)
                    ? string.Empty
                    : NormalizeFullPath(Path.Combine(originalsFolder, sourceFile.RelativePath))
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.FullPath))
            .GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var resolved = new List<(TrackInfo Track, int OriginalPosition, int DiscNumber, Guid SourceId)>(tracks.Count);
        var unmatched = new List<string>();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var fullPath = string.IsNullOrWhiteSpace(track.FilePath)
                ? string.Empty
                : NormalizeFullPath(track.FilePath);
            if (string.IsNullOrWhiteSpace(fullPath) ||
                !manifestSourcesByPath.TryGetValue(fullPath, out var manifestSource))
            {
                if (unmatched.Count < 3 && !string.IsNullOrWhiteSpace(track.FilePath))
                    unmatched.Add(track.FilePath);
                continue;
            }

            resolved.Add((
                track,
                index,
                manifestSource.DiscNumber,
                manifestSource.SourceFile.SourceId));
        }

        if (resolved.Count != tracks.Count)
        {
            var suffix = unmatched.Count == 0
                ? string.Empty
                : $" Beispiele: {string.Join(" | ", unmatched)}";
            throw new InvalidOperationException(
                $"Die MP3-CD-Trackliste konnte nicht vollständig gegen die importierten Discs abgeglichen werden " +
                $"({resolved.Count}/{tracks.Count}).{suffix}");
        }

        var orderedTracks = resolved
            .OrderBy(item => item.DiscNumber)
            .ThenBy(item => item.OriginalPosition)
            .ToList();

        tracks.Clear();
        for (var index = 0; index < orderedTracks.Count; index++)
        {
            var item = orderedTracks[index];
            item.Track.Index = index + 1;
            item.Track.AutomaticOrderPosition = index + 1;
            item.Track.DiscNumber = item.DiscNumber;
            if (item.SourceId != Guid.Empty)
                item.Track.SourceId = item.SourceId;
            item.Track.HideFolderDisplay = true;
            tracks.Add(item.Track);
        }
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
            track.HideFolderDisplay = true;

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

    private static string NormalizeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
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
