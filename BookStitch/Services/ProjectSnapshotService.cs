using BookStitch.Models;

using System.IO;

namespace BookStitch.Services;

public sealed class ProjectSnapshotService
{
    private readonly Mp3DiscProjectService _mp3DiscProjectService;
    private readonly AudioDiscProjectService _audioDiscProjectService;
    private readonly WorkManifestService _workManifestService;
    private readonly ProjectMetadataMigrationService _projectMetadataMigrationService;

    public ProjectSnapshotService(
        Mp3DiscProjectService mp3DiscProjectService,
        AudioDiscProjectService audioDiscProjectService,
        WorkManifestService workManifestService,
        ProjectMetadataMigrationService? projectMetadataMigrationService = null)
    {
        _mp3DiscProjectService = mp3DiscProjectService ?? throw new ArgumentNullException(nameof(mp3DiscProjectService));
        _audioDiscProjectService = audioDiscProjectService ?? throw new ArgumentNullException(nameof(audioDiscProjectService));
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _projectMetadataMigrationService = projectMetadataMigrationService ?? new ProjectMetadataMigrationService();
    }

    public void SaveEditableProjectSnapshot(string projectFolder, string projectType,
        ProjectSnapshotUiState snapshot, IReadOnlyList<TrackInfo> tracks, string finalOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        if (!Directory.Exists(projectFolder)) throw new DirectoryNotFoundException(projectFolder);
        if (projectType == ProjectManifestTypes.Mp3DiscProject)
        {
            var manifest = _mp3DiscProjectService.TryLoad(projectFolder)
                ?? throw new InvalidDataException("MP3 disc project manifest is unavailable.");
            SaveMp3DiscProjectSnapshot(manifest, snapshot);
        }
        else if (projectType == ProjectManifestTypes.AudioCdProject)
        {
            var manifest = _audioDiscProjectService.TryLoad(projectFolder)
                ?? throw new InvalidDataException("Audio disc project manifest is unavailable.");
            SaveAudioDiscProjectSnapshot(manifest, snapshot, force: true);
        }
        var exportPath = ProjectFolderLayout.ResolveExportManifestPath(projectFolder);
        if (File.Exists(exportPath))
        {
            var sourceFolder = _workManifestService.ReadOnly(exportPath)!.SourceFolder;
            var manifest = _workManifestService.LoadOrCreate(exportPath, projectType,
                projectFolder, sourceFolder, snapshot.SelectedExportPreset);
            SaveExportSnapshot(exportPath, manifest, snapshot,
                Path.GetDirectoryName(finalOutputPath)!, Path.GetFileName(finalOutputPath));
        }
        new TrackListStateService().Save(projectFolder, tracks);
    }

    public void UpdateMp3DiscProjectSnapshot(
        Mp3DiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        _mp3DiscProjectService.UpdateSettingsSnapshot(
            manifest,
            snapshot.SelectedExportPreset,
            snapshot.ParallelJobs,
            snapshot.OutputExtension,
            snapshot.OutputFolder,
            snapshot.FileNameTemplate);

        _mp3DiscProjectService.UpdateCoverAndOutputSnapshot(
            manifest,
            snapshot.CoverSourcePath,
            snapshot.ProcessedCoverPath,
            snapshot.OutputFileNamePreview);
    }

    public void SaveMp3DiscProjectSnapshot(
        Mp3DiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot)
    {
        UpdateMp3DiscProjectSnapshot(manifest, CreateAuthoritativeSnapshot(manifest.ProjectFolder, snapshot));
        _mp3DiscProjectService.Save(manifest);
    }


    public bool UpdateAudioDiscProjectSnapshot(
        AudioDiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        return _audioDiscProjectService.UpdateSnapshot(manifest, snapshot);
    }

    public bool SaveAudioDiscProjectSnapshot(
        AudioDiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot,
        bool force = false)
    {
        var changed = UpdateAudioDiscProjectSnapshot(manifest, CreateAuthoritativeSnapshot(manifest.ProjectFolder, snapshot));
        if (changed || force)
            _audioDiscProjectService.Save(manifest);

        return changed;
    }

    public void UpdateExportManifestSnapshot(
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        _workManifestService.UpdateExportSettings(
            manifest,
            snapshot.SelectedExportPreset,
            finalOutputFolder,
            finalOutputFileName,
            snapshot.OutputExtension,
            snapshot.ParallelJobs);

        _workManifestService.UpdateCoverMetadata(
            manifest,
            snapshot.CoverSourcePath,
            snapshot.ProcessedCoverPath);
    }

    public void SaveExportSnapshot(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkExportStarted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkExportStarted(manifest);
        _workManifestService.PruneInvalidEntries(manifest);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkConversionCompleted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkConversionCompleted(manifest);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkMergingStarted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkMergingStarted(manifest);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkExportCompleted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkExportCompleted(manifest);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkExportCanceled(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName,
        string reason)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkExportCanceled(manifest, reason);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkExportFailed(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName,
        string errorSummary)
    {
        UpdateExportManifestSnapshot(
            manifest,
            CreateAuthoritativeSnapshot(ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath), snapshot),
            finalOutputFolder,
            finalOutputFileName);
        _workManifestService.MarkExportFailed(manifest, errorSummary);
        _workManifestService.Save(manifestPath, manifest);
    }

    private ProjectSnapshotUiState CreateAuthoritativeSnapshot(string projectFolder, ProjectSnapshotUiState snapshot)
    {
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        var metadata = File.Exists(metadataPath)
            ? _projectMetadataMigrationService.LoadAuthoritativeProjection(projectFolder)
            : _projectMetadataMigrationService.SaveCurrentProjectMetadata(
                projectFolder,
                snapshot.BookTitle,
                snapshot.Author,
                snapshot.Album,
                snapshot.Narrator,
                snapshot.Genre);

        return snapshot with
        {
            BookTitle = metadata.Title,
            Author = metadata.Author,
            Album = metadata.Album,
            Narrator = metadata.Narrator,
            Genre = metadata.Genre
        };
    }
}
