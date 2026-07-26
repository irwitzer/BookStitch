using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ApplicationShutdownServiceTests
{
    private readonly ApplicationShutdownService _service = new();

    [Theory]
    [InlineData(true, false, true, true, ApplicationActivity.Mp3DiscImport)]
    [InlineData(true, true, true, true, ApplicationActivity.AudioDiscProcessing)]
    [InlineData(false, true, true, true, ApplicationActivity.Export)]
    [InlineData(false, false, true, true, ApplicationActivity.Export)]
    [InlineData(false, false, false, true, ApplicationActivity.BackgroundWork)]
    [InlineData(false, false, false, false, ApplicationActivity.None)]
    public void GetActiveActivity_UsesStablePriority(
        bool isDiscImporting,
        bool isAudioDiscProcessing,
        bool isExporting,
        bool isBusy,
        ApplicationActivity expected)
    {
        var result = _service.GetActiveActivity(
            isDiscImporting,
            isAudioDiscProcessing,
            isExporting,
            isBusy);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false, ApplicationActivity.AudioDiscProcessing)]
    [InlineData(false, true, ApplicationActivity.Mp3DiscImport)]
    [InlineData(false, false, ApplicationActivity.Export)]
    public void GetActiveActivity_TreatsPausedPipelineAsActive(
        bool hasPausedAudioDiscProject,
        bool hasPausedMp3DiscProject,
        ApplicationActivity expected)
    {
        var result = _service.GetActiveActivity(
            isDiscImporting: false,
            isAudioDiscProcessing: false,
            isExporting: false,
            isBusy: false,
            isPipelinePaused: true,
            hasPausedAudioDiscProject: hasPausedAudioDiscProject,
            hasPausedMp3DiscProject: hasPausedMp3DiscProject);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CreatePrompt_ForDiscImport_ExplainsPreservedFiles()
    {
        var prompt = _service.CreatePrompt(ApplicationActivity.Mp3DiscImport);

        Assert.Contains("MP3-CD-Import", prompt.Heading);
        Assert.Contains("vollständig kopierte", prompt.Message);
        Assert.Contains("beendet", prompt.ProgressText);
    }

    [Fact]
    public void CreatePrompt_ForAudioDiscProcessing_UsesAudioDiscWording()
    {
        var prompt = _service.CreatePrompt(ApplicationActivity.AudioDiscProcessing);

        Assert.Contains("Audio-CD-Verarbeitung", prompt.Heading);
        Assert.Contains("Rippen", prompt.Message);
        Assert.Contains("vollständig gerippte und konvertierte", prompt.Message);
        Assert.Contains("Audio-CD-Verarbeitung", prompt.ProgressText);
    }

    [Fact]
    public async Task WaitForIdleAsync_ReturnsWhenWorkFinishes()
    {
        var active = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            active = false;
        });

        var result = await _service.WaitForIdleAsync(() => active, TimeSpan.FromSeconds(2));

        Assert.True(result);
    }

    [Fact]
    public async Task WaitForIdleAsync_ReturnsFalseAfterTimeout()
    {
        var result = await _service.WaitForIdleAsync(
            () => true,
            TimeSpan.FromMilliseconds(30));

        Assert.False(result);
    }
}
