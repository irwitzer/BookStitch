namespace BookStitch.Services;

public static class AudioDiscExportAvailabilityService
{
    public static bool CanStart(
        bool isAwaitingRip,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting,
        bool hasActiveManifest,
        bool ffmpegAvailable) =>
        isAwaitingRip &&
        !isBusy &&
        !isExporting &&
        !isDiscImporting &&
        hasActiveManifest &&
        ffmpegAvailable;
}