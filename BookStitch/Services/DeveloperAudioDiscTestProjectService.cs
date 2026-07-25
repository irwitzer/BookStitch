using BookStitch.Models;
using System.Globalization;
using System.IO;

namespace BookStitch.Services;

public sealed record DeveloperAudioDiscTestPreparation(
    AudioDiscProjectManifest Manifest,
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
    int DiscNumber,
    int ResetTrackCount,
    int FirstResetTrackNumber);

public sealed class DeveloperAudioDiscTestProjectService
{
    private readonly AudioDiscProjectService _audioDiscProjectService;
    private readonly WorkManifestService _workManifestService;

    public DeveloperAudioDiscTestProjectService(
        AudioDiscProjectService? audioDiscProjectService = null,
        WorkManifestService? workManifestService = null)
    {
        _audioDiscProjectService = audioDiscProjectService ?? new AudioDiscProjectService();
        _workManifestService = workManifestService ?? new WorkManifestService();
    }

    public string? ResolveProjectFolder(string selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder) || !Directory.Exists(selectedFolder))
            return null;

        var full = Path.GetFullPath(selectedFolder);
        if (_audioDiscProjectService.TryLoad(full) is not null)
            return full;

        var matches = Directory.EnumerateDirectories(full)
            .Select(Path.GetFullPath)
            .Where(folder => _audioDiscProjectService.TryLoad(folder) is not null)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public AudioDiscProjectManifest? TryLoadTemplate(string selectedFolder)
    {
        var resolved = ResolveProjectFolder(selectedFolder);
        return resolved is null ? null : _audioDiscProjectService.TryLoad(resolved);
    }

    public DeveloperAudioDiscTestPreparation Prepare(
        string selectedProjectFolder,
        string workingCopiesRoot,
        int discNumber,
        int resetLastTracks,
        int totalDiscs,
        DiscDriveInfo drive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedProjectFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopiesRoot);
        ArgumentNullException.ThrowIfNull(drive);

        var templateFolder = ResolveProjectFolder(selectedProjectFolder)
            ?? throw new InvalidOperationException("Im ausgewählten Ordner wurde kein eindeutiges gültiges Audio-CD-Projekt gefunden.");
        var templateManifest = _audioDiscProjectService.TryLoad(templateFolder)
            ?? throw new InvalidOperationException("Das ausgewählte Audio-CD-Testprojekt konnte nicht geladen werden.");
        var templateWorkManifest = _workManifestService.LoadOrCreate(
            ProjectFolderLayout.ResolveWorkManifestPath(templateFolder),
            ProjectManifestTypes.AudioCdProject,
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
        var manifest = _audioDiscProjectService.TryLoad(workingFolder)
            ?? throw new InvalidOperationException("Die Arbeitskopie des Audio-CD-Testprojekts konnte nicht geladen werden.");
        var coverFilePath = ResolveSingleCoverFile(workingFolder, manifest);
        var disc = manifest.Discs.FirstOrDefault(item => item.DiscNumber == discNumber)
            ?? throw new InvalidOperationException($"Audio-CD {discNumber} ist im Testprojekt nicht vorhanden.");
        if (disc.Tracks.Count == 0)
            throw new InvalidOperationException("Die gewählte Audio-CD enthält keine vorbereiteten Tracks.");

        resetLastTracks = Math.Clamp(resetLastTracks, 1, disc.Tracks.Count);
        totalDiscs = Math.Max(Math.Max(2, totalDiscs), discNumber + 1);
        var tracks = disc.Tracks.OrderByDescending(item => item.TrackNumber).Take(resetLastTracks).ToList();
        var selectedIndexes = tracks.Select(item => item.GlobalIndex).ToHashSet();

        foreach (var track in tracks)
        {
            DeleteRequiredFile(Path.Combine(workingFolder, track.RelativePath), "vorbereitete FLAC-Datei");
            DeleteOptionalFile(Path.Combine(workingFolder, track.RelativePath) + ".part");
            track.Status = AudioDiscTrackStatus.Pending;
            track.CompletedUtc = null;
            track.OutputSizeBytes = null;
            track.ErrorMessage = string.Empty;
        }

        DeleteConvertedFiles(workingFolder, selectedIndexes);
        manifest.ExportPreset = selectedPreset;
        manifest.ParallelJobs = parallelJobs;
        ResetWorkManifest(workingFolder, templateFolder, manifest, selectedIndexes, selectedPreset, parallelJobs);

        disc.Status = AudioDiscStatus.Pending;
        disc.CompletedUtc = null;
        disc.RipDuration = null;
        disc.ErrorMessage = string.Empty;
        disc.SourceDriveRoot = drive.RootPath;
        disc.SourceDriveName = drive.DriveName;
        disc.SourceDriveDevicePath = drive.DevicePath;
        disc.SourceVolumeLabel = drive.VolumeLabel;

        manifest.ProjectFolder = workingFolder;
        manifest.TotalDiscs = totalDiscs;
        manifest.PipelineState = ProjectPipelineState.AcquiringSources.ToManifestValue();
        manifest.Status = AudioDiscProjectStatus.AwaitingRip;
        manifest.CompletedUtc = null;
        manifest.ErrorMessage = string.Empty;
        manifest.SourceDriveRoot = drive.RootPath;
        manifest.SourceDriveName = drive.DriveName;
        manifest.SourceDriveDevicePath = drive.DevicePath;
        manifest.SourceVolumeLabel = drive.VolumeLabel;
        _audioDiscProjectService.Save(manifest);

        return new DeveloperAudioDiscTestPreparation(
            manifest,
            templateFolder,
            workingFolder,
            coverFilePath,
            manifest.Title,
            manifest.Author,
            manifest.Album,
            manifest.Narrator,
            manifest.Genre,
            selectedPreset,
            parallelJobs,
            discNumber,
            resetLastTracks,
            tracks.Min(item => item.TrackNumber));
    }

    private static string ResolveSingleCoverFile(string projectFolder, AudioDiscProjectManifest manifest)
    {
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        if (!string.IsNullOrWhiteSpace(manifest.CoverSourcePath))
        {
            var referencedName = Path.GetFileName(manifest.CoverSourcePath);
            if (!string.IsNullOrWhiteSpace(referencedName))
            {
                var referencedFile = Path.Combine(projectFolder, referencedName);
                if (File.Exists(referencedFile) && supportedExtensions.Contains(Path.GetExtension(referencedFile)))
                    return referencedFile;
            }
        }

        var candidates = Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
            .ToList();

        return candidates.Count switch
        {
            0 => string.Empty,
            1 => candidates[0],
            _ => throw new InvalidOperationException(
                "Im Testprojekt liegen mehrere mögliche Coverdateien auf oberster Ebene. Bitte lasse dort nur ein JPG-, JPEG-, PNG- oder WebP-Cover liegen.")
        };
    }

    private static string CreateWorkingCopy(string templateFolder, string workingCopiesRoot)
    {
        Directory.CreateDirectory(workingCopiesRoot);
        var templateName = Path.GetFileName(templateFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var safeName = string.IsNullOrWhiteSpace(templateName) ? "Audio-CD-Test" : templateName;
        var destination = Path.Combine(
            Path.GetFullPath(workingCopiesRoot),
            safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        CopyDirectory(templateFolder, destination);
        return destination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private void ResetWorkManifest(
        string folder,
        string templateFolder,
        AudioDiscProjectManifest audioManifest,
        HashSet<int> indexes,
        string selectedPreset,
        string parallelJobs)
    {
        var path = ProjectFolderLayout.ResolveWorkManifestPath(folder);
        var work = _workManifestService.LoadOrCreate(
            path,
            ProjectManifestTypes.AudioCdProject,
            folder,
            ProjectFolderLayout.GetOriginalsFolder(folder),
            audioManifest.ExportPreset);
        var copiedManifestRoot = ResolveStoredProjectRoot(work.Tracks, templateFolder);
        work.ProjectWorkFolder = folder;
        work.SourceFolder = ProjectFolderLayout.GetOriginalsFolder(folder);
        work.SelectedPreset = selectedPreset;
        work.Export.SelectedPreset = selectedPreset;
        work.Export.ParallelJobs = parallelJobs;
        work.Metadata.Title = audioManifest.Title;
        work.Metadata.Author = audioManifest.Author;
        work.Metadata.Album = audioManifest.Album;
        work.Metadata.Narrator = audioManifest.Narrator;
        work.Metadata.Genre = audioManifest.Genre;
        work.Metadata.CoverSourcePath = RebasePath(audioManifest.CoverSourcePath, folder, copiedManifestRoot, templateFolder);
        work.Metadata.ProcessedCoverPath = RebasePath(audioManifest.ProcessedCoverPath, folder, copiedManifestRoot, templateFolder);
        foreach (var track in work.Tracks)
            RebaseTrackPathsAndRefreshMetadata(track, folder, copiedManifestRoot, templateFolder);
        work.Tracks.RemoveAll(item => indexes.Contains(item.TrackIndex));
        work.State.Status = ProjectManifestStatuses.AcquiringSources;
        work.State.LastSuccessfulStep = "DeveloperAudioDiscTestPrepared";
        work.State.LastStartedTrackIndex = work.Tracks.Count == 0 ? 0 : work.Tracks.Max(item => item.TrackIndex);
        work.State.LastCompletedTrackIndex = work.State.LastStartedTrackIndex;
        work.State.CancelRequestedUtc = null;
        work.State.LastErrorUtc = null;
        work.State.LastErrorSummary = string.Empty;
        work.Resume.CanResume = true;
        work.Resume.Reason = "Audio-CD-Entwickler-Kurztest vorbereitet.";
        _workManifestService.Save(ProjectFolderLayout.GetWorkManifestPath(folder), work);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
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

    private static void DeleteConvertedFiles(string folder, HashSet<int> indexes)
    {
        var converted = ProjectFolderLayout.GetConvertedFolder(folder);
        if (!Directory.Exists(converted))
            return;
        foreach (var file in Directory.EnumerateFiles(converted, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (indexes.Any(index => name.StartsWith($"{index:000}_", StringComparison.OrdinalIgnoreCase)))
                DeleteOptionalFile(file);
        }
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
