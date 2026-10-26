namespace BookStitch.Services;

public sealed record FfmpegSettingsApplicationDecision(
    string FfmpegPath,
    string FfprobePath,
    bool ShouldClearToolStatus);

public static class FfmpegSettingsApplicationService
{
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
            statusText?.StartsWith(
                "FFmpeg ",
                StringComparison.OrdinalIgnoreCase) == true);
    }
}