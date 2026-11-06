using BookStitch.Services;

namespace BookStitch;

public partial class MainWindow
{
    private Task? _preparedAudioColumnTask;
    private CancellationTokenSource _preparedAudioColumnCancellation = new();

    private void ResetPreparedAudioColumns()
    {
        var previous = _preparedAudioColumnCancellation;
        previous.Cancel();
        if (_preparedAudioColumnTask is { IsCompleted: false } task)
            _ = task.ContinueWith(_ => previous.Dispose(), TaskScheduler.Default);
        else previous.Dispose();
        _preparedAudioColumnCancellation = new();
        _preparedAudioColumnTask = null;
        _preparedAudioColumnProject = "";
        _preparedAudioColumnCache = new();
    }
    private string _preparedAudioColumnProject = "";
    private AudioEvidenceCache _preparedAudioColumnCache = new();

    private void QueuePreparedAudioColumns()
    {
        if (_isWorkspaceTransitioning || _isProjectCloseInProgress || _isWorkspaceReplacementInProgress || IsBusy || IsExporting || IsDiscImporting || _isResumeLoading || _isShutdownInProgress || !_ffmpegStatus.IsComplete ||
            _preparedAudioColumnTask is { IsCompleted: false } || string.IsNullOrWhiteSpace(_currentProjectWorkFolder) ||
            !Tracks.Any(track => track.HasReusableConvertedFile && track.PreparedAudio is null)) return;
        if (_preparedAudioColumnProject != _currentProjectWorkFolder)
        {
            ResetPreparedAudioColumns();
            _preparedAudioColumnProject = _currentProjectWorkFolder;
            _preparedAudioColumnCache = AudioEvidenceCache.Load(_currentProjectWorkFolder, "prepared-audio-evidence.json");
        }
        _preparedAudioColumnTask = ReadPreparedAudioColumnsAsync(_currentProjectWorkFolder, SelectedExportPreset,
            _preparedAudioColumnCache, _preparedAudioColumnCancellation.Token);
    }

    private async Task ReadPreparedAudioColumnsAsync(string project, string preset, AudioEvidenceCache cache, CancellationToken token)
    {
        try
        {
            var results = await new PreparedAudioColumnService().ReadAsync(Tracks.ToArray(), project, preset,
                _ffmpegStatus.FfprobePath!, cache, token);
            token.ThrowIfCancellationRequested();
            if (_currentProjectWorkFolder != project || SelectedExportPreset != preset || _isShutdownInProgress) return;
            foreach (var result in results)
            {
                var track = result.Track;
                if (!Tracks.Contains(track) || !track.HasReusableConvertedFile || track.PreparedConvertedPreset != preset ||
                    !TrackPathService.PathEquals(track.PreparedConvertedPath, result.Path)) continue;
                track.PreparedAudioLength = result.Length;
                track.PreparedAudioModified = result.Modified;
                track.PreparedAudio = result.Audio;
            }
            if (cache.HasEntries) cache.Save(project, "prepared-audio-evidence.json");
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            _runtimeServices.DiagnosticLogService.WriteError("Prepared audio column evidence unavailable.", error);
        }
    }
}
