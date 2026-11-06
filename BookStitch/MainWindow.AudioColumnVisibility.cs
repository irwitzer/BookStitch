using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch;

public partial class MainWindow
{
    private AudioColumnMode _audioColumnMode = AudioColumnMode.Input;

    private Visibility AudioColumnVisibility(DataGridColumn column) =>
        AudioColumnVisibilityService.GetVisibility(GetTrackGridColumnKey(column), _audioColumnMode) ?? Visibility.Visible;

    private void SetAudioColumnMode(AudioColumnMode mode)
    {
        _audioColumnMode = mode;
        ApplyAutomaticAudioColumnVisibility();
    }

    private void ApplyAutomaticAudioColumnVisibility()
    {
        if (TracksGrid is null || _settings.TrackGridCustomColumnVisibility) return;
        var changed = TracksGrid.Columns.Any(column =>
            AudioColumnVisibilityService.GetVisibility(GetTrackGridColumnKey(column), _audioColumnMode) is { } expected && column.Visibility != expected);
        if (!changed) return;
        var previous = _isApplyingTrackGridColumnLayout;
        _isApplyingTrackGridColumnLayout = true;
        try
        {
            AudioColumnVisibilityService.Apply(TracksGrid.Columns, GetTrackGridColumnKey, _audioColumnMode, _settings.TrackGridCustomColumnVisibility);
        }
        finally { _isApplyingTrackGridColumnLayout = previous; }
        RefreshEmbeddedChapterRowColumnLayouts(force: true);
        AutoFitTrackColumnsAfterRender();
    }
}
