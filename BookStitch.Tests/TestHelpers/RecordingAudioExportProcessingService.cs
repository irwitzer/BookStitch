using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Tests.TestHelpers;

internal sealed class RecordingAudioExportProcessingService : IAudioExportProcessingService
{
    public Func<string, CancellationToken, Task<ConvertedAacTiming>>? AnalyzeTiming { get; set; }

    public Task<ConvertedAacTiming> AnalyzeConvertedTimingAsync(string path, string ffprobePath,
        ConvertedAacTiming? cached, CancellationToken token) => AnalyzeTiming?.Invoke(path, token) ?? throw new NotSupportedException();
    public PerformanceExternalProcessContext? LastPreparationContext { get; private set; }
    public Func<CancellationToken, Task>? Prepare { get; set; }

    public Task PrepareTrackForExportAsync(
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null,
        string? ffprobePath = null)
    {
        LastPreparationContext = performanceContext;
        if (Prepare is not null) return Prepare(token);
        Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
        File.WriteAllText(convertedPath, "prepared");
        return Task.CompletedTask;
    }

    public Task MergeConvertedTracksAsync(
        string concatListPath,
        string chapterMetadataPath,
        string finalPartPath,
        long totalTicks,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null) =>
        throw new NotSupportedException("This test double records track preparation only.");
}
