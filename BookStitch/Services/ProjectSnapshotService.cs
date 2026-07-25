using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ProjectSnapshotService
{
    private readonly Mp3DiscProjectService _mp3DiscProjectService;
    private readonly AudioDiscProjectService _audioDiscProjectService;
    private readonly WorkManifestService _workManifestService;

    public ProjectSnapshotService(
        Mp3DiscProjectService mp3DiscProjectService,
        AudioDiscProjectService audioDiscProjectService,
        WorkManifestService workManifestService)
    {
        _mp3DiscProjectService = mp3DiscProjectService ?? throw new ArgumentNullException(nameof(mp3DiscProjectService));
        _audioDiscProjectService = audioDiscProjectService ?? throw new ArgumentNullException(nameof(audioDiscProjectService));
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
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

        _mp3DiscProjectService.UpdateMetadataSnapshot(
            manifest,
            snapshot.BookTitle,
            snapshot.Author,
            snapshot.Album,
            snapshot.Narrator,
            snapshot.Genre,
            snapshot.CoverSourcePath,
            snapshot.ProcessedCoverPath,
            snapshot.OutputFileNamePreview);
    }

    public void SaveMp3DiscProjectSnapshot(
        Mp3DiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot)
    {
        UpdateMp3DiscProjectSnapshot(manifest, snapshot);
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
        var changed = UpdateAudioDiscProjectSnapshot(manifest, snapshot);
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
            snapshot.OutputFolder,
            finalOutputFileName,
            snapshot.OutputExtension,
            snapshot.ParallelJobs);

        _workManifestService.UpdateBookMetadata(
            manifest,
            snapshot.BookTitle,
            snapshot.Author,
            snapshot.Album,
            snapshot.Narrator,
            snapshot.Genre,
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
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkExportStarted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
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
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
        _workManifestService.MarkConversionCompleted(manifest, snapshot.SelectedExportPreset);
        _workManifestService.Save(manifestPath, manifest);
    }

    public void MarkMergingStarted(
        string manifestPath,
        ExportWorkManifest manifest,
        ProjectSnapshotUiState snapshot,
        string finalOutputFolder,
        string finalOutputFileName)
    {
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
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
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
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
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
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
        UpdateExportManifestSnapshot(manifest, snapshot, finalOutputFolder, finalOutputFileName);
        _workManifestService.MarkExportFailed(manifest, errorSummary);
        _workManifestService.Save(manifestPath, manifest);
    }
}

public sealed record ProjectSnapshotUiState(
    string SelectedExportPreset,
    string ParallelJobs,
    string OutputExtension,
    string OutputFolder,
    string FileNameTemplate,
    string BookTitle,
    string Author,
    string Album,
    string Narrator,
    string Genre,
    string CoverSourcePath,
    string ProcessedCoverPath,
    string OutputFileNamePreview);
