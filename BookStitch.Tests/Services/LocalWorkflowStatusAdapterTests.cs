using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalWorkflowStatusAdapterTests
{
    private readonly LocalWorkflowStatusAdapter _adapter = new();
    private readonly WorkflowStatusFormatter _formatter = new();

    [Fact]
    public void RunningCopyAndLiveConversion_UsesAgreedLocalStatusText()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            ProjectPipelineState.AcquiringSources,
            new LocalProjectLivePreparationProgress(
                18,
                42,
                11,
                "18.mp3",
                ["01.mp3", "03.mp3", "04.mp3"],
                [1, 3, 4, 5, 7]),
            ExportPreset.Parse("AAC Stereo 128 kbps"));

        var result = _formatter.Format(snapshot);

        Assert.Equal("Kopieren • 18 / 42 | Live-Konvertierung 11 / 42 AAC 128 kbps", result.TeletextText);
        Assert.Equal("42 % | 18 / 42 kopiert | Konvertierung: 01, 03, 04, 05, 07", result.ProgressText);
        Assert.Equal(42, result.ProgressPercent);
    }

    [Fact]
    public void FinishedCopyWhileConversionContinues_PreservesCopyResult()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            ProjectPipelineState.Converting,
            new LocalProjectLivePreparationProgress(
                42,
                42,
                31,
                "31.mp3",
                ["31.mp3"],
                [31]),
            ExportPreset.Parse("AAC Stereo 128 kbps"));

        var result = _formatter.Format(snapshot);

        Assert.Equal("Kopieren abgeschlossen 42 / 42 | Live-Konvertierung 31 / 42 AAC 128 kbps", result.TeletextText);
        Assert.Equal("74 % | 31 / 42 konvertiert | Aktive Jobs: 31", result.ProgressText);
    }

    [Fact]
    public void PausedRun_PreservesCountsAndAddsPausePrefix()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            ProjectPipelineState.AcquiringSources,
            new LocalProjectLivePreparationProgress(16, 26, 7, "", [], []),
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            isPaused: true);

        var result = _formatter.Format(snapshot);

        Assert.Equal("Pause • Kopieren • 16 / 26 | Live-Konvertierung 7 / 26 AAC 128 kbps", result.TeletextText);
        Assert.Equal("61 % | pausiert | 16 / 26 kopiert", result.ProgressText);
    }

    [Fact]
    public void ReadySnapshot_UsesAgreedReviewText()
    {
        var snapshot = _adapter.CreateReadySnapshot(
            "project",
            42,
            ExportPreset.Parse("AAC Stereo 128 kbps"));

        var result = _formatter.Format(snapshot);

        Assert.Equal("Kopieren abgeschlossen 42 / 42 | 42 / 42 AAC 128 kbps | Bereit zum Zusammenfügen", result.TeletextText);
        Assert.Equal("100 %", result.ProgressText);
    }
}
