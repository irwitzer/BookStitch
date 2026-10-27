using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfmpegSettingsApplicationServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ready for project")]
    public void Missing_tool_replaces_only_idle_status_with_installation_instruction(string status)
    {
        Assert.Equal(LocalizationText.Get("MainWindow.Ffmpeg.InstallRequired"),
            FfmpegSettingsApplicationService.ResolveStatusText(status, false, "Ready for project"));
        Assert.Equal(status,
            FfmpegSettingsApplicationService.ResolveStatusText(status, true, "Ready for project"));
    }

    [Theory]
    [InlineData("Installation in progress")]
    [InlineData("Project conversion failed")]
    public void Tool_hint_preserves_progress_and_project_status(string status)
    {
        Assert.Equal(status,
            FfmpegSettingsApplicationService.ResolveStatusText(status, false, "Ready for project"));
    }

    [Fact]
    public void Localized_installation_progress_is_cleared_after_detection()
    {
        var result = FfmpegSettingsApplicationService.Create("", "ffmpeg", true,
            "", "ffprobe", true, LocalizationText.Get("MainWindow.Ffmpeg.InstallStarted"));
        Assert.True(result.ShouldClearToolStatus);
    }

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
