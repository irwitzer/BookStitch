using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class ProjectFolderMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool MigrateIfNeeded(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;

        var changed = false;
        try
        {
            ProjectFolderLayout.EnsureProjectFolders(projectFolder);

            changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.WorkManifestFileName);
            changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.ExportManifestFileName);
            changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.AudioDiscManifestFileName);
            changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.TrackListStateFileName);

            changed |= MoveLegacyMp3DiscFolders(projectFolder);
            changed |= MoveLegacyAudioSources(projectFolder);
            changed |= NormalizeMp3Manifest(projectFolder);
            changed |= NormalizeAudioManifest(projectFolder);
            changed |= NormalizeExportManifest(projectFolder);
        }
        catch (IOException)
        {
            // A partially locked legacy project remains readable through the compatibility resolvers.
        }
        catch (UnauthorizedAccessException)
        {
            // Do not block project discovery when a legacy file is temporarily inaccessible.
        }

        return changed;
    }

    private static bool MoveRootManifest(string projectFolder, string fileName)
    {
        var legacy = Path.Combine(projectFolder, fileName);
        var target = Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), fileName);
        if (!File.Exists(legacy))
            return false;

        if (!File.Exists(target))
        {
            File.Move(legacy, target);
        }
        else
        {
            var legacyWrite = File.GetLastWriteTimeUtc(legacy);
            var targetWrite = File.GetLastWriteTimeUtc(target);
            if (legacyWrite > targetWrite)
                File.Move(legacy, target, overwrite: true);
            else
                File.Delete(legacy);
        }
        return true;
    }

    private static bool MoveLegacyMp3DiscFolders(string projectFolder)
    {
        var changed = false;
        foreach (var folder in Directory.EnumerateDirectories(projectFolder, "CD *", SearchOption.TopDirectoryOnly))
        {
            var folderName = Path.GetFileName(folder);
            if (folderName.Length <= 3 ||
                !int.TryParse(folderName[3..].Trim(), out var discNumber) ||
                discNumber < 1)
            {
                continue;
            }

            var target = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, discNumber);
            changed |= MergeDirectory(folder, target);
        }
        return changed;
    }

    private static bool MoveLegacyAudioSources(string projectFolder)
    {
        var legacyRipped = Path.Combine(projectFolder, "ripped");
        if (!Directory.Exists(legacyRipped))
            return false;

        var audioManifest = TryRead<AudioDiscProjectManifest>(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder));
        if (audioManifest is null)
            return MergeDirectory(legacyRipped, ProjectFolderLayout.GetOriginalsFolder(projectFolder));

        var changed = false;
        foreach (var track in audioManifest.Discs.SelectMany(disc => disc.Tracks))
        {
            var source = ResolveAudioSourcePath(projectFolder, legacyRipped, track);
            if (!File.Exists(source))
                continue;

            var targetFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, Math.Max(1, track.DiscNumber));
            Directory.CreateDirectory(targetFolder);
            var target = Path.Combine(targetFolder, track.FileName);
            if (!PathsEqual(source, target))
            {
                MoveFile(source, target);
                changed = true;
            }
        }

        if (Directory.Exists(legacyRipped))
            changed |= MergeDirectory(legacyRipped, ProjectFolderLayout.GetOriginalsFolder(projectFolder));
        return changed;
    }

    private static bool NormalizeMp3Manifest(string projectFolder)
    {
        var path = ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);
        var manifest = TryRead<Mp3DiscProjectManifest>(path);
        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.ProjectType) ||
            !manifest.ProjectType.Contains("Mp3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = false;
        manifest.ProjectFolder = projectFolder;
        foreach (var disc in manifest.ImportedDiscs)
        {
            var expected = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, disc.DiscNumber);
            if (!PathsEqual(disc.LocalFolder, expected))
            {
                disc.LocalFolder = expected;
                changed = true;
            }
        }

        if (changed)
            Write(path, manifest);
        return changed;
    }

    private static bool NormalizeAudioManifest(string projectFolder)
    {
        var path = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder);
        var manifest = TryRead<AudioDiscProjectManifest>(path);
        if (manifest is null)
            return false;

        var changed = false;
        manifest.ProjectFolder = projectFolder;
        foreach (var track in manifest.Discs.SelectMany(disc => disc.Tracks))
        {
            var expected = Path.Combine(
                ProjectFolderLayout.OriginalsFolderName,
                $"CD {Math.Max(1, track.DiscNumber):00}",
                track.FileName);
            if (!string.Equals(NormalizeRelative(track.RelativePath), NormalizeRelative(expected), StringComparison.OrdinalIgnoreCase))
            {
                track.RelativePath = expected;
                changed = true;
            }
        }

        if (changed)
            Write(path, manifest);
        return changed;
    }

    private static bool NormalizeExportManifest(string projectFolder)
    {
        var paths = new[]
        {
            ProjectFolderLayout.ResolveExportManifestPath(projectFolder),
            ProjectFolderLayout.ResolveWorkManifestPath(projectFolder)
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        var changedAny = false;
        foreach (var path in paths)
        {
            var manifest = TryReadExportManifest(path);
            if (manifest is null ||
                !(string.Equals(manifest.ProjectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(manifest.ProjectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(manifest.ProjectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var changed = false;
            if (!PathsEqual(manifest.ProjectWorkFolder, projectFolder))
            {
                manifest.ProjectWorkFolder = projectFolder;
                changed = true;
            }
            var normalizedSourceFolder = MapLegacySourcePath(projectFolder, manifest.SourceFolder);
            if (!PathsEqual(manifest.SourceFolder, normalizedSourceFolder))
            {
                manifest.SourceFolder = normalizedSourceFolder;
                changed = true;
            }

            foreach (var track in manifest.Tracks)
            {
                var source = MapLegacySourcePath(projectFolder, track.SourcePath);
                if (!PathsEqual(track.SourcePath, source))
                {
                    track.SourcePath = source;
                    changed = true;
                }
            }

            if (changed)
            {
                Write(path, manifest);
                changedAny = true;
            }
        }
        return changedAny;
    }

    private static string MapLegacySourcePath(string projectFolder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;

        string full;
        try { full = Path.GetFullPath(path); }
        catch { return path; }

        var legacyRipped = Path.Combine(projectFolder, "ripped");
        if (IsUnder(full, legacyRipped))
        {
            var relative = Path.GetRelativePath(legacyRipped, full);
            if (string.Equals(relative, ".", StringComparison.Ordinal))
                return ProjectFolderLayout.GetOriginalsFolder(projectFolder);

            var direct = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectFolder), relative);
            if (File.Exists(direct) || Directory.Exists(direct))
                return direct;

            var fileName = Path.GetFileName(full);
            var matches = Directory.Exists(ProjectFolderLayout.GetOriginalsFolder(projectFolder))
                ? Directory.EnumerateFiles(ProjectFolderLayout.GetOriginalsFolder(projectFolder), fileName, SearchOption.AllDirectories)
                    .Take(2)
                    .ToArray()
                : [];
            return matches.Length == 1 ? matches[0] : direct;
        }

        for (var disc = 1; disc <= 999; disc++)
        {
            var legacyDisc = Path.Combine(projectFolder, $"CD {disc:00}");
            if (!IsUnder(full, legacyDisc))
                continue;
            return Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, disc), Path.GetRelativePath(legacyDisc, full));
        }

        return full;
    }

    private static string ResolveAudioSourcePath(string projectFolder, string legacyRipped, AudioDiscProjectManifestTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track.RelativePath))
        {
            var fromRelative = Path.Combine(projectFolder, track.RelativePath);
            if (File.Exists(fromRelative))
                return fromRelative;
        }
        return Path.Combine(legacyRipped, track.FileName);
    }

    private static bool MergeDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
            return false;
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            MoveFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        Directory.Delete(source, recursive: true);
        return true;
    }

    private static void MoveFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            File.Move(source, target);
            return;
        }

        if (new FileInfo(source).Length != new FileInfo(target).Length)
            throw new IOException($"Zieldatei existiert mit abweichender Größe: {target}");

        File.Delete(source);
    }


    private static ExportWorkManifest? TryReadExportManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty(nameof(ExportWorkManifest.Tracks), out _) &&
                !root.TryGetProperty(nameof(ExportWorkManifest.Export), out _) &&
                !root.TryGetProperty(nameof(ExportWorkManifest.State), out _))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ExportWorkManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static T? TryRead<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) : default;
        }
        catch { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".migration.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static bool IsUnder(string path, string root)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || PathsEqual(path, root);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private static string NormalizeRelative(string? value) =>
        (value ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
}
