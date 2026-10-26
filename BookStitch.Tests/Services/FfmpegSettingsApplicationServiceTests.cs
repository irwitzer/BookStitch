using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfmpegSettingsApplicationServiceTests
{
    [Theory]
    [InlineData(
        "old-ffmpeg",
        "new-ffmpeg",
        true,
        "old-ffprobe",
        "new-ffprobe",
        true,
        "Bereit",
        "new-ffmpeg",
        "new-ffprobe",
        false)]
    [InlineData(
        "old-ffmpeg",
        "new-ffmpeg",
        false,
        "old-ffprobe",
        "new-ffprobe",
        false,
        "FFmpeg nicht gefunden",
        "old-ffmpeg",
        "old-ffprobe",
        true)]
    [InlineData(
        null,
        null,
        true,
        null,
        null,
        true,
        "ffmpeg wird eingerichtet",
        "",
        "",
        true)]
    public void Create_AppliesAvailableToolPathsAndStatusDecision(
        string? currentFfmpegPath,
        string? detectedFfmpegPath,
        bool ffmpegAvailable,
        string? currentFfprobePath,
        string? detectedFfprobePath,
        bool ffprobeAvailable,
        string? statusText,
        string expectedFfmpegPath,
        string expectedFfprobePath,
        bool expectedShouldClearToolStatus)
    {
        var result = FfmpegSettingsApplicationService.Create(
            currentFfmpegPath,
            detectedFfmpegPath,
            ffmpegAvailable,
            currentFfprobePath,
            detectedFfprobePath,
            ffprobeAvailable,
            statusText);

        Assert.Equal(expectedFfmpegPath, result.FfmpegPath);
        Assert.Equal(expectedFfprobePath, result.FfprobePath);
        Assert.Equal(
            expectedShouldClearToolStatus,
            result.ShouldClearToolStatus);
    }
}