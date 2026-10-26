using System.IO;

using BookStitch.Models;

namespace BookStitch.Services;

public sealed class AudioDiscLiveConversionManifestSession : IAsyncDisposable
{
    private readonly WorkManifestService _workManifestService;
    private readonly ConvertedTrackReconciliationService _reconciliationService;
    private readonly ProjectMetadataMigrationService _projectMetadataMigrationService;
    private readonly AudioDiscProjectManifest _audioDiscManifest;
    private readonly ExportPreset _preset;
    private readonly object _syncRoot = new();
    private readonly ExportWorkManifest _manifest;
    private readonly PreparedTrackLifecycleSession _preparedLifecycle;

    public AudioDiscLiveConversionManifestSession(
        WorkManifestService workManifestService,
        AudioDiscProjectManifest audioDiscManifest,
        ExportPreset preset,
        ProjectMetadataMigrationService? projectMetadataMigrationService = null,
        IAudioExportProcessingService? audio = null, string ffprobePath = "", int effectiveParallelism = 1)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _reconciliationService = new ConvertedTrackReconciliationService(_workManifestService);
        _audioDiscManifest = audioDiscManifest ?? throw new ArgumentNullException(nameof(audioDiscManifest));
        _preset = preset ?? throw new ArgumentNullException(nameof(preset));
        _projectMetadataMigrationService = projectMetadataMigrationService ?? new ProjectMetadataMigrationService();

        ArgumentException.ThrowIfNullOrWhiteSpace(audioDiscManifest.ProjectFolder);

        ProjectFolderLayout.EnsureProjectFolders(audioDiscManifest.ProjectFolder);
        ManifestPath = ProjectFolderLayout.GetExportManifestPath(audioDiscManifest.ProjectFolder);
        var sourceFolder = ProjectFolderLayout.GetOriginalsFolder(audioDiscManifest.ProjectFolder);

        _manifest = _workManifestService.LoadOrCreate(
            ManifestPath,
            ProjectManifestTypes.AudioCdProject,
            audioDiscManifest.ProjectFolder,
            sourceFolder,
            preset.DisplayName);

        _preparedLifecycle = new PreparedTrackLifecycleSession(_workManifestService,
            audio ?? new AacExportProcessingService(), _manifest, ManifestPath, _syncRoot,
            ffprobePath, effectiveParallelism);

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


    public bool ReconcileTrack(AudioDiscLiveConversionPreparation preparation, CancellationToken token = default)
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
                _preset, token);

            if (result.ManifestChanged)
                _workManifestService.Save(ManifestPath, _manifest);

            if (result.CanReuse)
            {
                var entry = _manifest.Tracks.Single(entry => TrackPathService.PathEquals(entry.ConvertedPath, preparation.ConvertedPath) &&
                    string.Equals(entry.Preset, _preset.DisplayName, StringComparison.OrdinalIgnoreCase));
                _preparedLifecycle.QueueTiming(entry, token);
            }
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
        }
    }

    public void MarkTrackCompleted(AudioDiscLiveConversionPreparation preparation, CancellationToken token = default)
    {
        _preparedLifecycle.Complete(preparation.Track.Index, preparation.Track,
            preparation.SourcePath, preparation.ConvertedPath, _preset, token);
    }

    public Task DrainTimingAsync() => _preparedLifecycle.DrainAsync();
    public ValueTask DisposeAsync() => _preparedLifecycle.DisposeAsync();

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
        var metadata = ProjectMetadataMigrationService.CreateProjection(
            _projectMetadataMigrationService.EnsureMigrated(
                _audioDiscManifest.ProjectFolder,
                mp3DiscManifest: null,
                exportManifest: _manifest,
                audioDiscManifest: _audioDiscManifest));

        var outputFileName = FileNameTemplateService.BuildOutputFileName(
            metadata.Title,
            metadata.Author,
            metadata.Narrator,
            _audioDiscManifest.FileNameTemplate,
            _audioDiscManifest.OutputExtension,
            metadata.Album);

        _workManifestService.UpdateExportSettings(
            _manifest,
            _preset.DisplayName,
            _audioDiscManifest.OutputFolder,
            outputFileName,
            _audioDiscManifest.OutputExtension,
            _audioDiscManifest.ParallelJobs);

        _workManifestService.UpdateCoverMetadata(
            _manifest,
            _audioDiscManifest.CoverSourcePath,
            _audioDiscManifest.ProcessedCoverPath);
    }
}
