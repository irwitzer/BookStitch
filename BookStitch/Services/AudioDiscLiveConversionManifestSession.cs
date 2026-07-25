using System.IO;

using BookStitch.Models;

namespace BookStitch.Services;

public sealed class AudioDiscLiveConversionManifestSession
{
    private readonly WorkManifestService _workManifestService;
    private readonly ConvertedTrackReconciliationService _reconciliationService;
    private readonly AudioDiscProjectManifest _audioDiscManifest;
    private readonly ExportPreset _preset;
    private readonly object _syncRoot = new();
    private readonly ExportWorkManifest _manifest;

    public AudioDiscLiveConversionManifestSession(
        WorkManifestService workManifestService,
        AudioDiscProjectManifest audioDiscManifest,
        ExportPreset preset)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _reconciliationService = new ConvertedTrackReconciliationService(_workManifestService);
        _audioDiscManifest = audioDiscManifest ?? throw new ArgumentNullException(nameof(audioDiscManifest));
        _preset = preset ?? throw new ArgumentNullException(nameof(preset));

        ArgumentException.ThrowIfNullOrWhiteSpace(audioDiscManifest.ProjectFolder);

        ProjectFolderLayout.EnsureProjectFolders(audioDiscManifest.ProjectFolder);
        ManifestPath = ProjectFolderLayout.GetWorkManifestPath(audioDiscManifest.ProjectFolder);
        var sourceFolder = ProjectFolderLayout.GetOriginalsFolder(audioDiscManifest.ProjectFolder);

        _manifest = _workManifestService.LoadOrCreate(
            ManifestPath,
            ProjectManifestTypes.AudioCdProject,
            audioDiscManifest.ProjectFolder,
            sourceFolder,
            preset.DisplayName);

        lock (_syncRoot)
        {
            UpdateProjectSnapshotCore();
            _manifest.State.Status = ProjectManifestStatuses.AcquiringSources;
            _workManifestService.MarkConversionPreparationStarted(_manifest);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public string ManifestPath { get; }

    public void SaveProjectSnapshot()
    {
        lock (_syncRoot)
        {
            UpdateProjectSnapshotCore();
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public void MarkSessionCanceled(string reason)
    {
        lock (_syncRoot)
        {
            UpdateProjectSnapshotCore();
            _workManifestService.MarkExportCanceled(_manifest, reason);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public void MarkSessionFailed(string errorSummary)
    {
        lock (_syncRoot)
        {
            UpdateProjectSnapshotCore();
            _workManifestService.MarkExportFailed(_manifest, errorSummary);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }


    public bool ReconcileTrack(AudioDiscLiveConversionPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        lock (_syncRoot)
        {
            var result = _reconciliationService.ReconcileTrack(
                _manifest,
                ProjectManifestTypes.AudioCdProject,
                preparation.Track.Index,
                preparation.Track,
                preparation.SourcePath,
                preparation.ConvertedPath,
                _preset);

            if (result.ManifestChanged)
                _workManifestService.Save(ManifestPath, _manifest);

            return result.CanReuse;
        }
    }

    public void MarkTrackStarted(AudioDiscLiveConversionPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        lock (_syncRoot)
        {
            _workManifestService.MarkTrackStarted(
                _manifest,
                preparation.Track.Index,
                preparation.Track,
                preparation.SourcePath,
                preparation.ConvertedPath,
                _preset,
                ProjectManifestTrackStatuses.Converting);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public void MarkTrackCompleted(AudioDiscLiveConversionPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        lock (_syncRoot)
        {
            _workManifestService.UpdateTrack(
                _manifest,
                preparation.Track.Index,
                preparation.Track,
                preparation.SourcePath,
                preparation.ConvertedPath,
                _preset);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public void MarkTrackFailed(AudioDiscLiveConversionPreparation preparation, string errorSummary)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        lock (_syncRoot)
        {
            _workManifestService.MarkTrackFailed(
                _manifest,
                preparation.Track.Index,
                preparation.Track,
                preparation.SourcePath,
                preparation.ConvertedPath,
                _preset,
                errorSummary);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    public void MarkTrackCanceled(AudioDiscLiveConversionPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        lock (_syncRoot)
        {
            _workManifestService.MarkTrackCanceled(
                _manifest,
                preparation.Track.Index,
                preparation.Track,
                preparation.SourcePath,
                preparation.ConvertedPath,
                _preset);
            _workManifestService.Save(ManifestPath, _manifest);
        }
    }

    private void UpdateProjectSnapshotCore()
    {
        var outputFileName = FileNameTemplateService.BuildOutputFileName(
            _audioDiscManifest.Title,
            _audioDiscManifest.Author,
            _audioDiscManifest.Narrator,
            _audioDiscManifest.FileNameTemplate,
            _audioDiscManifest.OutputExtension,
            _audioDiscManifest.Album);

        _workManifestService.UpdateExportSettings(
            _manifest,
            _preset.DisplayName,
            _audioDiscManifest.OutputFolder,
            outputFileName,
            _audioDiscManifest.OutputExtension,
            _audioDiscManifest.ParallelJobs);

        _workManifestService.UpdateBookMetadata(
            _manifest,
            _audioDiscManifest.Title,
            _audioDiscManifest.Author,
            _audioDiscManifest.Album,
            _audioDiscManifest.Narrator,
            _audioDiscManifest.Genre,
            _audioDiscManifest.CoverSourcePath,
            _audioDiscManifest.ProcessedCoverPath);
    }
}
