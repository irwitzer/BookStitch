using BookStitch.Services;
using System.Threading;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ApplicationShutdownServiceTests
{
    [Theory]
    [InlineData(false, false, false, ProjectCloseIntent.Save)]
    [InlineData(true, false, false, ProjectCloseIntent.DeleteNewProject)]
    [InlineData(false, true, false, ProjectCloseIntent.RollbackRun)]
    [InlineData(false, false, true, ProjectCloseIntent.RollbackRun)]
    [InlineData(true, true, false, ProjectCloseIntent.RollbackRun)]
    [InlineData(true, false, true, ProjectCloseIntent.RollbackRun)]
    public void Workspace_close_preserves_loaded_projects_and_distinguishes_new_preparation_from_rollback(
        bool newProject, bool extension, bool reconversion, ProjectCloseIntent expected)
    {
        Assert.Equal(expected, ApplicationShutdownService.ResolveProjectCloseIntent(newProject, extension, reconversion));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Merge_close_discards_only_the_merge_even_if_a_previous_context_is_present(bool extension, bool reconversion)
        => Assert.Equal(ProjectCloseIntent.DiscardMerge,
            ApplicationShutdownService.ResolveProjectCloseIntent(false, extension, reconversion, isMerge: true));

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
        using var idleSignal = new ManualResetEventSlim(initialState: false);
        var worker = new Thread(() =>
        {
            Thread.Sleep(40);
            idleSignal.Set();
        })
        {
            IsBackground = true
        };
        worker.Start();

        var result = await _service.WaitForIdleAsync(
            () => !idleSignal.IsSet,
            TimeSpan.FromSeconds(2));

        Assert.True(result);
        Assert.True(worker.Join(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WaitForIdleAsync_ReturnsImmediatelyWhenWorkIsAlreadyIdle()
    {
        var checks = 0;

        var result = await _service.WaitForIdleAsync(
            () =>
            {
                checks++;
                return false;
            },
            TimeSpan.FromSeconds(2));

        Assert.True(result);
        Assert.Equal(1, checks);
    }

    [Fact]
    public async Task PausedPipeline_CanBeActiveEvenWhenWorkPredicateIsImmediatelyIdle()
    {
        var activity = _service.GetActiveActivity(
            isDiscImporting: false,
            isAudioDiscProcessing: false,
            isExporting: false,
            isBusy: false,
            isPipelinePaused: true,
            hasPausedAudioDiscProject: false,
            hasPausedMp3DiscProject: false);

        var stoppedCleanly = await _service.WaitForIdleAsync(
            () => false,
            TimeSpan.FromSeconds(2));

        Assert.Equal(ApplicationActivity.Export, activity);
        Assert.True(stoppedCleanly);
    }

    [Fact]
    public async Task WaitForIdleAsync_WaitsAsynchronouslyUntilWorkBecomesIdle()
    {
        var checks = 0;

        var result = await _service.WaitForIdleAsync(
            () => Interlocked.Increment(ref checks) < 3,
            TimeSpan.FromSeconds(2));

        Assert.True(result);
        Assert.True(checks >= 3);
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
