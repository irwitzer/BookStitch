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
