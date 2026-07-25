using BookStitch.Models;
using System.Globalization;
using System.IO;

namespace BookStitch.Services;

public sealed record DeveloperMp3DiscTestPreparation(
    Mp3DiscProjectManifest Manifest,
    string TemplateProjectFolder,
    string WorkingProjectFolder,
    string CoverFilePath,
    string Title,
    string Author,
    string Album,
    string Narrator,
    string Genre,
    string SelectedPreset,
    string ParallelJobs,
    string ExpectedDiscSignature,
    int DiscNumber,
    int ResetTrackCount,
    int FirstResetTrackNumber);

public sealed class DeveloperMp3DiscTestProjectService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".aac", ".m4a", ".m4b", ".wav", ".flac" };

    private readonly Mp3DiscProjectService _mp3DiscProjectService;
    private readonly WorkManifestService _workManifestService;

    public DeveloperMp3DiscTestProjectService(
        Mp3DiscProjectService? mp3DiscProjectService = null,
        WorkManifestService? workManifestService = null)
    {
        _mp3DiscProjectService = mp3DiscProjectService ?? new Mp3DiscProjectService();
        _workManifestService = workManifestService ?? new WorkManifestService();
    }

    public string? ResolveProjectFolder(string selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
            return null;
        var full = Path.GetFullPath(selectedFolder);
        if (_mp3DiscProjectService.TryLoad(full) is not null)
            return full;
        var matches = Directory.EnumerateDirectories(full)
            .Select(Path.GetFullPath)
            .Where(folder => _mp3DiscProjectService.TryLoad(folder) is not null)
            .Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public Mp3DiscProjectManifest? TryLoadTemplate(string selectedFolder)
    {
        var resolved = ResolveProjectFolder(selectedFolder);
        return resolved is null ? null : _mp3DiscProjectService.TryLoad(resolved);
    }

    public DeveloperMp3DiscTestPreparation Prepare(
        string selectedProjectFolder,
        string workingCopiesRoot,
        int discNumber,
        int resetLastTracks,
        int totalDiscs,
        DiscDriveInfo drive)
    {
        var templateFolder = ResolveProjectFolder(selectedProjectFolder)
            ?? throw new InvalidOperationException("Im ausgewählten Ordner wurde kein eindeutiges gültiges MP3-CD-Projekt gefunden.");
        var templateManifest = _mp3DiscProjectService.TryLoad(templateFolder)
            ?? throw new InvalidOperationException("Das ausgewählte MP3-CD-Testprojekt konnte nicht geladen werden.");
        var templateWorkManifest = _workManifestService.LoadOrCreate(
            ProjectFolderLayout.ResolveWorkManifestPath(templateFolder),
            ProjectManifestTypes.Mp3DiscProject,
            templateFolder,
            ProjectFolderLayout.GetOriginalsFolder(templateFolder),
            templateManifest.ExportPreset);
        var selectedPreset = FirstNonEmpty(
            templateWorkManifest.Export.SelectedPreset,
            templateWorkManifest.SelectedPreset,
            templateManifest.ExportPreset);
        var parallelJobs = FirstNonEmpty(
            templateWorkManifest.Export.ParallelJobs,
            templateManifest.ParallelJobs);

        var workingFolder = CreateWorkingCopy(templateFolder, workingCopiesRoot);
        var manifest = _mp3DiscProjectService.TryLoad(workingFolder)
            ?? throw new InvalidOperationException("Die Arbeitskopie des MP3-CD-Testprojekts konnte nicht geladen werden.");
        var disc = manifest.ImportedDiscs.FirstOrDefault(item => item.DiscNumber == discNumber)
            ?? throw new InvalidOperationException($"MP3-CD {discNumber} ist im Testprojekt nicht vorhanden.");
        if (string.IsNullOrWhiteSpace(disc.Signature))
            throw new InvalidOperationException($"Für MP3-CD {discNumber} ist keine Disc-Signatur gespeichert. Das Testprojekt muss einmal vollständig über den normalen MP3-CD-Import vorbereitet werden.");

        var discFolder = ResolvePreparedDiscFolder(disc, workingFolder, discNumber);
        if (!Directory.Exists(discFolder))
            throw new InvalidOperationException($"Der vorbereitete Originalordner für MP3-CD {discNumber} wurde nicht gefunden. Erwartet wurde insbesondere: {ProjectFolderLayout.ResolveDiscOriginalsFolder(workingFolder, discNumber)}");

        var files = Directory.EnumerateFiles(discFolder, "*", SearchOption.AllDirectories)
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetRelativePath(discFolder, path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("Die gewählte MP3-CD enthält keine vorbereiteten Audiodateien.");

        resetLastTracks = Math.Clamp(resetLastTracks, 1, files.Count);
        totalDiscs = Math.Max(Math.Max(2, totalDiscs), discNumber + 1);
        var selected = files.TakeLast(resetLastTracks).ToList();
        manifest.ExportPreset = selectedPreset;
        manifest.ParallelJobs = parallelJobs;
        ResetWorkManifest(workingFolder, templateFolder, discFolder, manifest, selected, selectedPreset, parallelJobs);
        RebaseCopiedConvertedFilesForPreset(workingFolder, selectedPreset, selected);
        foreach (var file in selected)
        {
            DeleteRequiredFile(file, "vorbereitete MP3-Originaldatei");
            DeleteOptionalFile(file + ".copying");
        }

        manifest.ProjectFolder = workingFolder;
        manifest.SourceFolder = drive.RootPath;
        manifest.TotalDiscs = totalDiscs;
        manifest.PipelineState = ProjectPipelineState.AcquiringSources.ToManifestValue();
        manifest.SourceDriveRoot = drive.RootPath;
        manifest.SourceDriveName = drive.DiagnosticDriveName;
        manifest.SourceDriveDevicePath = drive.DevicePath;
        manifest.SourceVolumeLabel = drive.VolumeLabel;
        manifest.ImportedDiscs.RemoveAll(item => item.DiscNumber == discNumber);
        foreach (var item in manifest.ImportedDiscs)
        {
            item.LocalFolder = RebasePath(item.LocalFolder, workingFolder, templateFolder);
            item.SourcePath = item.DiscNumber == discNumber ? drive.RootPath : RebasePath(item.SourcePath, workingFolder, templateFolder);
        }
        _mp3DiscProjectService.Save(manifest);

        return new DeveloperMp3DiscTestPreparation(
            manifest, templateFolder, workingFolder, ResolveSingleCoverFile(workingFolder, manifest),
            manifest.Title, manifest.Author, manifest.Album, manifest.Narrator, manifest.Genre,
            selectedPreset, parallelJobs, disc.Signature,
            discNumber, resetLastTracks, files.Count - resetLastTracks + 1);
    }

    private void ResetWorkManifest(
        string folder,
        string templateFolder,
        string discFolder,
        Mp3DiscProjectManifest manifest,
        IReadOnlyList<string> selectedFiles,
        string selectedPreset,
        string parallelJobs)
    {
        var path = ProjectFolderLayout.ResolveWorkManifestPath(folder);
        var work = _workManifestService.LoadOrCreate(path, ProjectManifestTypes.Mp3DiscProject, folder,
            ProjectFolderLayout.GetOriginalsFolder(folder), manifest.ExportPreset);
        var copiedManifestRoot = ResolveStoredProjectRoot(work.Tracks, templateFolder);
        work.SelectedPreset = selectedPreset;
        work.Export.SelectedPreset = selectedPreset;
        work.Export.ParallelJobs = parallelJobs;
        work.ProjectWorkFolder = folder;
        work.SourceFolder = ProjectFolderLayout.GetOriginalsFolder(folder);
        work.Metadata.Title = manifest.Title;
        work.Metadata.Author = manifest.Author;
        work.Metadata.Album = manifest.Album;
        work.Metadata.Narrator = manifest.Narrator;
        work.Metadata.Genre = manifest.Genre;
        work.Metadata.CoverSourcePath = RebasePath(manifest.CoverSourcePath, folder, copiedManifestRoot, templateFolder);
        work.Metadata.ProcessedCoverPath = RebasePath(manifest.ProcessedCoverPath, folder, copiedManifestRoot, templateFolder);
        foreach (var track in work.Tracks)
            RebaseTrackPathsAndRefreshMetadata(track, folder, copiedManifestRoot, templateFolder);

        var resetTracks = ResolveResetTracks(work.Tracks, selectedFiles, discFolder);
        foreach (var track in resetTracks)
        {
            DeleteOptionalFile(track.ConvertedPath);
            DeleteOptionalFile(track.ConvertedPath + ".part");
        }

        DeleteSelectedConvertedFilesBySourceName(folder, selectedPreset, selectedFiles);
        work.Tracks.RemoveAll(track => resetTracks.Contains(track));
        work.State.Status = ProjectManifestStatuses.AcquiringSources;
        work.State.LastSuccessfulStep = "DeveloperMp3DiscTestPrepared";
        work.State.LastStartedTrackIndex = work.Tracks.Count == 0 ? 0 : work.Tracks.Max(item => item.TrackIndex);
        work.State.LastCompletedTrackIndex = work.State.LastStartedTrackIndex;
        work.State.CancelRequestedUtc = null;
        work.State.LastErrorUtc = null;
        work.State.LastErrorSummary = string.Empty;
        work.Resume.CanResume = true;
        work.Resume.Reason = "MP3-CD-Entwickler-Kurztest vorbereitet.";
        _workManifestService.Save(ProjectFolderLayout.GetWorkManifestPath(folder), work);
    }


    internal static int CountReusablePreparedConvertedFiles(string projectFolder, ExportPreset preset)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || preset is null)
            return 0;

        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        if (!Directory.Exists(originalsFolder) || !Directory.Exists(convertedFolder))
            return 0;

        return Directory.EnumerateFiles(originalsFolder, "*", SearchOption.AllDirectories)
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
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

    internal static void RebaseCopiedConvertedFilesForPreset(
        string projectFolder,
        string selectedPreset,
        IReadOnlyList<string>? resetSourcePaths = null)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || string.IsNullOrWhiteSpace(selectedPreset))
            return;

        var preset = ExportPreset.Parse(selectedPreset);
        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        if (!Directory.Exists(originalsFolder) || !Directory.Exists(convertedFolder))
            return;

        var resetSet = (resetSourcePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in Directory.EnumerateFiles(originalsFolder, "*", SearchOption.AllDirectories)
                     .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fullSourcePath = Path.GetFullPath(sourcePath);
            var expectedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                convertedFolder,
                fullSourcePath,
                CreateTrackForSourcePath(fullSourcePath));

            if (resetSet.Contains(fullSourcePath))
            {
                DeleteOptionalFile(expectedConvertedPath);
                foreach (var candidate in FindConvertedCandidates(convertedFolder, fullSourcePath))
                    DeleteOptionalFile(candidate);
                continue;
            }

            if (File.Exists(expectedConvertedPath))
                continue;

            var candidates = FindConvertedCandidates(convertedFolder, fullSourcePath)
                .Where(candidate => !usedCandidates.Contains(candidate))
                .ToList();
            if (candidates.Count != 1)
                continue;

            var candidatePath = candidates[0];
            Directory.CreateDirectory(Path.GetDirectoryName(expectedConvertedPath)!);
            if (!File.Exists(expectedConvertedPath))
            {
                File.Move(candidatePath, expectedConvertedPath);
                usedCandidates.Add(expectedConvertedPath);
            }
        }
    }

    private static IReadOnlyList<string> FindConvertedCandidates(string convertedFolder, string sourcePath)
    {
        if (!Directory.Exists(convertedFolder))
            return [];

        var sourceStem = FileNameTemplateService.CleanWindowsFileName(Path.GetFileNameWithoutExtension(sourcePath));
        if (string.IsNullOrWhiteSpace(sourceStem))
            sourceStem = "Track";

        return Directory.EnumerateFiles(convertedFolder, "*.m4a", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith(sourceStem + "_", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileNameWithoutExtension(fileName), sourceStem, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            ProcessingAction = "Konvertieren"
        };
    }



    private static void DeleteSelectedConvertedFilesBySourceName(
        string projectFolder,
        string selectedPreset,
        IReadOnlyList<string> selectedFiles)
    {
        var convertedRoot = ProjectFolderLayout.GetConvertedFolder(projectFolder);
        if (!Directory.Exists(convertedRoot))
            return;

        var presetFolder = ProjectFolderLayout.GetConvertedPresetFolder(
            projectFolder,
            ExportPreset.Parse(selectedPreset).GetFolderName());
        if (!Directory.Exists(presetFolder))
            return;

        var sourceStems = selectedFiles
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(stem => !string.IsNullOrWhiteSpace(stem))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(presetFolder, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            var matched = sourceStems.Any(stem =>
                fileName.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(fileName), stem, StringComparison.OrdinalIgnoreCase));
            if (matched)
                DeleteOptionalFile(path);
        }
    }

    private static List<ExportWorkManifestTrack> ResolveResetTracks(
        IReadOnlyList<ExportWorkManifestTrack> tracks,
        IReadOnlyList<string> selectedFiles,
        string discFolder)
    {
        var remaining = new HashSet<ExportWorkManifestTrack>(tracks);
        var result = new List<ExportWorkManifestTrack>(selectedFiles.Count);

        foreach (var selectedFile in selectedFiles)
        {
            var selectedFullPath = Path.GetFullPath(selectedFile);
            var selectedFileName = Path.GetFileName(selectedFullPath);
            var selectedRelativePath = NormalizeRelativePath(Path.GetRelativePath(discFolder, selectedFullPath));

            var candidates = remaining
                .Select(track => new
                {
                    Track = track,
                    Score = GetSourceMatchScore(track, selectedFullPath, selectedFileName, selectedRelativePath)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ToList();

            if (candidates.Count == 0)
                continue;

            var bestScore = candidates[0].Score;
            var bestCandidates = candidates.Where(item => item.Score == bestScore).ToList();
            if (bestCandidates.Count != 1)
                continue;

            var match = bestCandidates[0].Track;
            remaining.Remove(match);
            result.Add(match);
        }

        return result;
    }

    private static int GetSourceMatchScore(
        ExportWorkManifestTrack track,
        string selectedFullPath,
        string selectedFileName,
        string selectedRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(track.SourcePath))
        {
            try
            {
                var trackFullPath = Path.GetFullPath(track.SourcePath);
                if (string.Equals(trackFullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase))
                    return 400;

                var normalizedTrackPath = NormalizeRelativePath(track.SourcePath);
                if (!string.IsNullOrWhiteSpace(selectedRelativePath)
                    && normalizedTrackPath.EndsWith(selectedRelativePath, StringComparison.OrdinalIgnoreCase))
                    return 300;

                if (string.Equals(Path.GetFileName(trackFullPath), selectedFileName, StringComparison.OrdinalIgnoreCase))
                    return 200;
            }
            catch
            {
                // A stale or malformed manifest path may still be recoverable through SourceFileName.
            }
        }

        if (!string.IsNullOrWhiteSpace(track.SourceFileName)
            && string.Equals(Path.GetFileName(track.SourceFileName), selectedFileName, StringComparison.OrdinalIgnoreCase))
            return 100;

        return 0;
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string ResolvePreparedDiscFolder(
        Mp3DiscProjectManifestDisc disc,
        string workingFolder,
        int discNumber)
    {
        var workingRoot = Path.GetFullPath(workingFolder);
        var candidates = new[]
        {
            ProjectFolderLayout.ResolveDiscOriginalsFolder(workingRoot, discNumber),
            ProjectFolderLayout.GetDiscOriginalsFolder(workingRoot, discNumber),
            Path.Combine(ProjectFolderLayout.ResolveOriginalsFolder(workingRoot), $"CD {discNumber:00}"),
            RebaseManifestLocalFolder(disc.LocalFolder, workingRoot, discNumber)
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => IsPathInsideRoot(path, workingRoot))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(Directory.Exists)
            ?? ProjectFolderLayout.ResolveDiscOriginalsFolder(workingRoot, discNumber);
    }

    private static string RebaseManifestLocalFolder(string? localFolder, string workingFolder, int discNumber)
    {
        if (string.IsNullOrWhiteSpace(localFolder))
            return ProjectFolderLayout.GetDiscOriginalsFolder(workingFolder, discNumber);

        if (!Path.IsPathRooted(localFolder))
            return Path.Combine(workingFolder, localFolder);

        var originalsMarker = $"{Path.DirectorySeparatorChar}originals{Path.DirectorySeparatorChar}";
        var markerIndex = localFolder.IndexOf(originalsMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            return Path.Combine(workingFolder, localFolder[(markerIndex + 1)..]);

        return ProjectFolderLayout.GetDiscOriginalsFolder(workingFolder, discNumber);
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelectedSourcePath(string? sourcePath, HashSet<string> selected)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        try
        {
            return selected.Contains(Path.GetFullPath(sourcePath));
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveSingleCoverFile(string projectFolder, Mp3DiscProjectManifest manifest)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
        if (!string.IsNullOrWhiteSpace(manifest.CoverSourcePath))
        {
            var candidate = Path.Combine(projectFolder, Path.GetFileName(manifest.CoverSourcePath));
            if (File.Exists(candidate) && extensions.Contains(Path.GetExtension(candidate))) return candidate;
        }
        var files = Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => extensions.Contains(Path.GetExtension(path))).ToList();
        return files.Count switch { 0 => string.Empty, 1 => files[0], _ => throw new InvalidOperationException("Im Testprojekt liegen mehrere mögliche Coverdateien auf oberster Ebene.") };
    }

    private static string CreateWorkingCopy(string templateFolder, string root)
    {
        Directory.CreateDirectory(root);
        var name = Path.GetFileName(templateFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destination = Path.Combine(Path.GetFullPath(root), (string.IsNullOrWhiteSpace(name) ? "MP3-CD-Test" : name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        foreach (var directory in Directory.EnumerateDirectories(templateFolder, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(templateFolder, directory)));
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(templateFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(templateFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false);
        }
        return destination;
    }

    internal static string ResolveStoredProjectRoot(
        IReadOnlyList<ExportWorkManifestTrack> tracks,
        string fallbackRoot)
    {
        foreach (var track in tracks)
        {
            var root = TryResolveProjectRoot(track.SourcePath, ProjectFolderLayout.OriginalsFolderName)
                ?? TryResolveProjectRoot(track.ConvertedPath, ProjectFolderLayout.ConvertedFolderName);
            if (!string.IsNullOrWhiteSpace(root))
                return root;
        }

        return fallbackRoot;
    }

    private static string? TryResolveProjectRoot(string? path, string folderName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return null;

        var fullPath = Path.GetFullPath(path);
        var marker = Path.DirectorySeparatorChar + folderName + Path.DirectorySeparatorChar;
        var markerIndex = fullPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return markerIndex > 0 ? fullPath[..markerIndex] : null;
    }

    private static void RebaseTrackPathsAndRefreshMetadata(
        ExportWorkManifestTrack track,
        string newRoot,
        params string?[] oldRoots)
    {
        track.SourcePath = RebasePath(track.SourcePath, newRoot, oldRoots);
        track.ConvertedPath = RebasePath(track.ConvertedPath, newRoot, oldRoots);

        if (File.Exists(track.SourcePath))
        {
            var sourceInfo = new FileInfo(track.SourcePath);
            track.SourceSizeBytes = sourceInfo.Length;
            track.SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks;
        }

        if (File.Exists(track.ConvertedPath))
        {
            var convertedInfo = new FileInfo(track.ConvertedPath);
            track.ConvertedSizeBytes = convertedInfo.Length;
            track.ConvertedLastWriteUtcTicks = convertedInfo.LastWriteTimeUtc.Ticks;
        }
    }

    internal static string RebasePath(string? path, string newRoot, params string?[] oldRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path ?? string.Empty;

        if (!Path.IsPathRooted(path))
            return Path.GetFullPath(Path.Combine(newRoot, path));

        var fullPath = Path.GetFullPath(path);
        var fullNewRoot = Path.GetFullPath(newRoot);
        var newPrefix = fullNewRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(newPrefix, StringComparison.OrdinalIgnoreCase))
            return fullPath;

        foreach (var oldRoot in oldRoots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var fullOldRoot = Path.GetFullPath(oldRoot!);
            var oldPrefix = fullOldRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(fullNewRoot, Path.GetRelativePath(fullOldRoot, fullPath));
        }

        return fullPath;
    }

    private static void DeleteRequiredFile(string path, string description)
    {
        DeleteFile(path);
        if (File.Exists(path))
            throw new IOException($"Die {description} konnte nicht aus der Arbeitskopie entfernt werden: {path}");
    }

    private static void DeleteOptionalFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        DeleteFile(path);
    }

    private static void DeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }
}
