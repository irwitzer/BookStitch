using BookStitch.Models;
using BookStitch.Services;
using System.Windows.Threading;
namespace BookStitch;
public partial class MainWindow
{
    private async Task PublishPreparedTrackCompletionAsync(TrackInfo track, ExportWorkManifestTrack completed, string project, string preset)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (_isShutdownInProgress || _currentProjectWorkFolder != project || SelectedExportPreset != preset || !Tracks.Contains(track)) return;
            TrackPreparedStateRefreshService.ApplyCompletedCheckpoint(track, completed, preset);
        }, DispatcherPriority.Background);
    }
}
