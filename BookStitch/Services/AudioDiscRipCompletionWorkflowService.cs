using System.Globalization;
using System.IO;

using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioDiscRipCompletionWorkflowRequest(
    AudioDiscProjectManifest Manifest,
    ICollection<TrackInfo> Tracks,
    AudioDiscLiveConversionSessionSnapshot LiveConversionSnapshot,
    Func<string, Task> LoadRippedFolderAsync,
    Action<string> ApplyPersistedTrackListState,
    Action UpdateIndexes,
    Action RefreshTrackView,
    Action RefreshExportPreview,
    Action NotifyExportUiStateChanged);

public sealed record AudioDiscRipCompletionWorkflowResult(
    string RippedFolder,
    string StatusText,
    string ProgressText);

public sealed class AudioDiscRipCompletionWorkflowService
{
    private readonly AudioDiscProjectService _projectService;

    public AudioDiscRipCompletionWorkflowService(AudioDiscProjectService projectService)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
    }

    public async Task<AudioDiscRipCompletionWorkflowResult> RunAsync(
        AudioDiscRipCompletionWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Tracks);
        ArgumentNullException.ThrowIfNull(request.LoadRippedFolderAsync);

        var manifest = request.Manifest;
        var rippedFolder = ProjectFolderLayout.GetOriginalsFolder(manifest.ProjectFolder);

        await request.LoadRippedFolderAsync(rippedFolder);

        _projectService.ApplyManifestMetadataToRippedTracks(manifest, request.Tracks);
        request.ApplyPersistedTrackListState(manifest.ProjectFolder);
        request.UpdateIndexes();
        request.RefreshTrackView();
        request.RefreshExportPreview();
        request.NotifyExportUiStateChanged();

        var durationText = manifest.RipDuration?.ToString(
            @"hh\:mm\:ss",
            CultureInfo.InvariantCulture) ?? "unbekannt";
        var snapshot = request.LiveConversionSnapshot;
        var progressText = snapshot.FailedCount == 0 && snapshot.CanceledCount == 0
            ? $"Ripping abgeschlossen in {durationText}. {snapshot.CompletedCount} AAC-Track(s) wurden parallel vorbereitet; " +
              $"{snapshot.ReusedCount} vorhandene AAC-Track(s) wurden wiederverwendet."
            : $"Ripping abgeschlossen in {durationText}. {snapshot.CompletedCount} AAC-Track(s) vorbereitet; " +
              $"{snapshot.FailedCount + snapshot.CanceledCount} werden im Export erneut versucht.";

        return new AudioDiscRipCompletionWorkflowResult(
            rippedFolder,
            $"Alle {manifest.TotalDiscs} Audio-CDs vollständig nach FLAC gerippt.",
            progressText);
    }
}
