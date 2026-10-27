namespace BookStitch.Services;

public sealed record FfmpegSettingsApplicationDecision(
    string FfmpegPath,
    string FfprobePath,
    bool ShouldClearToolStatus);

public static class FfmpegSettingsApplicationService
{
    public static string ResolveStatusText(string statusText, bool ffmpegAvailable, string readyStatus)
    {
        return !ffmpegAvailable && (string.IsNullOrWhiteSpace(statusText) || statusText == readyStatus)
            ? LocalizationText.Get("MainWindow.Ffmpeg.InstallRequired")
            : statusText;
    }

    public static FfmpegSettingsApplicationDecision Create(
        string? currentFfmpegPath,
        string? detectedFfmpegPath,
        bool ffmpegAvailable,
        string? currentFfprobePath,
        string? detectedFfprobePath,
        bool ffprobeAvailable,
        string? statusText)
    {
        var ffmpegPath = ffmpegAvailable
            ? detectedFfmpegPath ?? string.Empty
            : currentFfmpegPath ?? string.Empty;

        var ffprobePath = ffprobeAvailable
            ? detectedFfprobePath ?? string.Empty
            : currentFfprobePath ?? string.Empty;

        return new FfmpegSettingsApplicationDecision(
            ffmpegPath,
            ffprobePath,
            statusText == LocalizationText.Get("MainWindow.Ffmpeg.InstallStarted") ||
            statusText?.StartsWith(
                "FFmpeg ",
                StringComparison.OrdinalIgnoreCase) == true);
    }
}
