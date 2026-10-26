using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

internal sealed class ProjectFolderMigrationService
{
    private readonly Action<string, string> _copyFile;
    public ProjectFolderMigrationService() : this((source, target) =>
        SourceFileCopyService.CopyAtomicallyAsync(source, target, CancellationToken.None).GetAwaiter().GetResult()) { }
    internal ProjectFolderMigrationService(Action<string, string> copyFile) => _copyFile = copyFile;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly AtomicJsonStore JsonStore = new();

    internal bool UpgradeLegacyLayout(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;

        if (!IsRecognizedBookStitchProjectFolder(projectFolder))
            return false;

        EnsureSafeTree(projectFolder);

        foreach (var fileName in new[] { ProjectFolderLayout.WorkManifestFileName,
                     ProjectFolderLayout.ExportManifestFileName, ProjectFolderLayout.LegacyAudioDiscManifestFileName })
        foreach (var path in new[] { Path.Combine(projectFolder, fileName),
                     Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), fileName) })
            if (File.Exists(path))
            {
                try { PersistentJsonSchema.Read(File.ReadAllText(path), "BookStitch project manifest", path); }
                catch (JsonException ex) { throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex); }
            }

        var changed = false;
        // Fail closed: partially published sources must not be resolved until migration succeeds.
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);

        changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.WorkManifestFileName);
        changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.ExportManifestFileName);
        changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.LegacyAudioDiscManifestFileName);
        changed |= MoveRootManifest(projectFolder, ProjectFolderLayout.TrackListStateFileName);

        changed |= MoveLegacyMp3DiscFolders(projectFolder);
        changed |= MoveLegacyAudioSources(projectFolder);
        changed |= NormalizeMp3Manifest(projectFolder);
        changed |= NormalizeAudioManifest(projectFolder);
        changed |= NormalizeExportManifest(projectFolder);
        return changed;
    }

    public static bool IsRecognizedBookStitchProjectFolder(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;

        return HasRecognizedCurrentManifest(projectFolder) ||
               HasRecognizedLegacyManifest(projectFolder);
    }

    private static bool HasRecognizedCurrentManifest(string projectFolder)
    {
        var settingsFolder = ProjectFolderLayout.GetSettingsFolder(projectFolder);
        if (!Directory.Exists(settingsFolder))
            return false;

        return IsRecognizedWorkManifest(ProjectFolderLayout.GetWorkManifestPath(projectFolder)) ||
               IsRecognizedMp3DiscManifest(ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder)) ||
               IsRecognizedAudioManifest(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder)) ||
               IsRecognizedExportManifest(ProjectFolderLayout.GetExportManifestPath(projectFolder)) ||
               IsRecognizedAudioManifest(Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), ProjectFolderLayout.LegacyAudioDiscManifestFileName));
    }

    private static bool HasRecognizedLegacyManifest(string projectFolder)
    {
        return IsRecognizedWorkManifest(Path.Combine(projectFolder, ProjectFolderLayout.WorkManifestFileName)) ||
               IsRecognizedExportManifest(Path.Combine(projectFolder, ProjectFolderLayout.ExportManifestFileName)) ||
               IsRecognizedAudioManifest(Path.Combine(projectFolder, ProjectFolderLayout.LegacyAudioDiscManifestFileName));
    }

    private static bool IsRecognizedWorkManifest(string path) =>
        IsRecognizedExportManifest(path) || IsRecognizedMp3DiscManifest(path);

    private static bool IsRecognizedExportManifest(string path)
    {
        var root = TryReadJsonRoot(path);
        if (root is null)
            return false;

        var element = root.Value;
        if (!element.TryGetProperty(nameof(ExportWorkManifest.ProjectType), out var projectTypeProperty))
            return false;

        var projectType = projectTypeProperty.GetString() ?? string.Empty;
        if (!string.Equals(projectType, ProjectManifestTypes.FolderProject, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(projectType, ProjectManifestTypes.AudioCdProject, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return element.TryGetProperty(nameof(ExportWorkManifest.Tracks), out _) ||
               element.TryGetProperty(nameof(ExportWorkManifest.Export), out _) ||
               element.TryGetProperty(nameof(ExportWorkManifest.State), out _);
    }

    private static bool IsRecognizedMp3DiscManifest(string path)
    {
        var root = TryReadJsonRoot(path);
        if (root is null)
            return false;

        var element = root.Value;
        if (!element.TryGetProperty(nameof(Mp3DiscProjectManifest.ProjectType), out var projectTypeProperty))
            return false;

        var projectType = projectTypeProperty.GetString() ?? string.Empty;
        if (!projectType.Contains("Mp3", StringComparison.OrdinalIgnoreCase))
            return false;

        return element.TryGetProperty(nameof(Mp3DiscProjectManifest.ImportedDiscs), out _) ||
               element.TryGetProperty(nameof(Mp3DiscProjectManifest.TotalDiscs), out _);
    }

    private static bool IsRecognizedAudioManifest(string path)
    {
        var root = TryReadJsonRoot(path);
        if (root is null)
            return false;

        var element = root.Value;
        return element.TryGetProperty(nameof(AudioDiscProjectManifest.Discs), out _) ||
               element.TryGetProperty(nameof(AudioDiscProjectManifest.DiscIdentity), out _) ||
               element.TryGetProperty(nameof(AudioDiscProjectManifest.WorkingFormat), out _);
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
            if (!FilesHaveSameContent(legacy, target))
                throw new ProjectUpgradeConflictException($"Legacy-Manifest steht im Konflikt mit der aktuellen Projektdatei: {target}");

            File.Delete(legacy);
        }
        return true;
    }

    private bool MoveLegacyMp3DiscFolders(string projectFolder)
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

    private bool MoveLegacyAudioSources(string projectFolder)
    {
        var legacyRipped = Path.Combine(projectFolder, "ripped");
        if (!Directory.Exists(legacyRipped))
            return false;

        var audioManifest = TryRead<AudioDiscProjectManifest>(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder));
        if (audioManifest is null)
            return MergeDirectory(legacyRipped, ProjectFolderLayout.GetOriginalsFolder(projectFolder));

        EnsureSafeTree(legacyRipped);
        var changed = false;
        var publishedSources = new List<string>();
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
                PublishFile(source, target);
                publishedSources.Add(source);
                changed = true;
            }
        }

        foreach (var source in publishedSources) File.Delete(source);
        if (Directory.Exists(legacyRipped))
            changed |= MergeDirectory(legacyRipped, ProjectFolderLayout.GetOriginalsFolder(projectFolder));
        return changed;
    }

    private static bool NormalizeMp3Manifest(string projectFolder)
    {
        var path = ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder);
        if (!IsRecognizedMp3DiscManifest(path)) return false;
        var manifest = TryRead<Mp3DiscProjectManifest>(path);
        if (manifest is null ||
            string.IsNullOrWhiteSpace(manifest.ProjectType) ||
            !manifest.ProjectType.Contains("Mp3", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var changed = !string.Equals(manifest.ProjectFolder, projectFolder, StringComparison.Ordinal);
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
        if (!IsRecognizedAudioManifest(path)) return false;
        var manifest = TryRead<AudioDiscProjectManifest>(path);
        if (manifest is null)
            return false;

        var changed = !string.Equals(manifest.ProjectFolder, projectFolder, StringComparison.Ordinal);
        manifest.ProjectFolder = projectFolder;
        foreach (var track in manifest.Discs.SelectMany(disc => disc.Tracks))
        {
            var expected = Path.Combine(
                ProjectFolderLayout.OriginalsFolderName,
                $"CD {Math.Max(1, track.DiscNumber):00}",
                track.FileName);
            if (!File.Exists(Path.Combine(projectFolder, expected)))
            {
                var existing = Path.Combine(projectFolder, track.RelativePath);
                var flatOriginal = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectFolder), track.FileName);
                if (File.Exists(existing)) expected = track.RelativePath;
                else if (File.Exists(flatOriginal)) expected = Path.GetRelativePath(projectFolder, flatOriginal);
            }
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
            // Coordinate the entire read/modify/write, not only the final replace.
            lock (AtomicJsonStore.ForPath(path))
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

        return TryMapLegacyDiscPath(projectFolder, full, out var mappedDiscPath)
            ? mappedDiscPath
            : full;
    }

    private static bool TryMapLegacyDiscPath(
        string projectFolder,
        string fullPath,
        out string mappedPath)
    {
        mappedPath = string.Empty;

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(projectFolder, fullPath);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.Equals(relativePath, ".", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var separatorIndex = relativePath.IndexOf(Path.DirectorySeparatorChar);
        if (separatorIndex < 0 &&
            Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
        {
            separatorIndex = relativePath.IndexOf(Path.AltDirectorySeparatorChar);
        }

        var firstSegment = separatorIndex < 0
            ? relativePath
            : relativePath[..separatorIndex];

        if (!firstSegment.StartsWith("CD ", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(firstSegment[3..].Trim(), out var discNumber) ||
            discNumber is < 1 or > 999)
        {
            return false;
        }

        var targetFolder =
            ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, discNumber);

        if (separatorIndex < 0 ||
            separatorIndex == relativePath.Length - 1)
        {
            mappedPath = targetFolder;
            return true;
        }

        mappedPath = Path.Combine(
            targetFolder,
            relativePath[(separatorIndex + 1)..]);
        return true;
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

    private bool MergeDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
            return false;
        EnsureSafeTree(source);
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(target);
        Directory.CreateDirectory(target);
        EnsureSafeTree(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            PublishFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        Directory.Delete(source, recursive: true);
        return true;
    }

    private void PublishFile(string source, string target)
    {
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(source);
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            _copyFile(source, target);
            return;
        }

        if (new FileInfo(source).Length != new FileInfo(target).Length)
            throw new ProjectUpgradeConflictException($"Zieldatei existiert mit abweichender Größe: {target}");

        if (!FilesHaveSameContent(source, target))
            throw new ProjectUpgradeConflictException($"Zieldatei existiert mit abweichendem Inhalt: {target}");

        // Keep legacy sources until the entire directory has been published successfully.
    }

    private static void EnsureSafeTree(string root) =>
        SafeFileSystemEnumeration.EnsureSafeTree(root);

    private static bool FilesHaveSameContent(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        const int BufferSize = 1024 * 128;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftBuffer = new byte[BufferSize];
        var rightBuffer = new byte[BufferSize];

        while (true)
        {
            var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
                return false;

            if (leftRead == 0)
                return true;

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                return false;
        }
    }


    private static ExportWorkManifest? TryReadExportManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            PersistentJsonSchema.Read(
                json,
                "Work-/Export-Manifest",
                path,
                ExportWorkManifestVersions.CurrentSchemaVersion);
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
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? TryReadJsonRoot(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
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
            if (!File.Exists(path))
                return default;

            var json = File.ReadAllText(path);
            if (typeof(T) == typeof(Mp3DiscProjectManifest))
            {
                PersistentJsonSchema.Read(
                    json,
                    "MP3-CD-Projektmanifest",
                    path,
                    Mp3DiscManifestVersions.CurrentSchemaVersion);
            }
            else if (typeof(T) == typeof(AudioDiscProjectManifest))
            {
                PersistentJsonSchema.Read(
                    json,
                    "Audio-CD-Projektmanifest",
                    path,
                    AudioDiscProjectManifestVersions.CurrentSchemaVersion);
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        JsonStore.Save(path, value, JsonOptions);
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
