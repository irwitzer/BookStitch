using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscWorkflowStatusAdapterTests
{
    private readonly AudioDiscWorkflowStatusAdapter _adapter = new();
    private readonly WorkflowStatusFormatter _formatter = new();

    [Fact]
    public void RunningSnapshot_CombinesRippingAndLiveConversion()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            currentDisc: 2,
            totalDiscs: 4,
            rippedCurrentDisc: 8,
            totalCurrentDisc: 16,
            rippedProject: 16,
            totalProject: 26,
            convertedProject: 7,
            activeTrackNumbers: new[] { 3, 5, 7 },
            preset: ExportPreset.Parse("AAC Stereo 128 kbps"),
            workingFormat: "FLAC");

        var view = _formatter.Format(snapshot);

        Assert.Equal("Audio-CD 2 von 4 wird gerippt • 08 / 16 FLAC | Live-Konvertierung 7 / 26 AAC 128 kbps", view.TeletextText);
        Assert.Equal("50 % | 16 / 26 gerippt | Konvertierung: 03, 05, 07", view.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Source, view.ProgressVisualKind);
    }

    [Fact]
    public void RunningSecondDisc_UsesCumulativeRipCountsInProgressText()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            currentDisc: 2,
            totalDiscs: 2,
            rippedCurrentDisc: 3,
            totalCurrentDisc: 19,
            rippedProject: 21,
            totalProject: 37,
            convertedProject: 21,
            activeTrackNumbers: Array.Empty<int>(),
            preset: ExportPreset.Parse("AAC Stereo 192 kbps"),
            workingFormat: "FLAC");

        var view = _formatter.Format(snapshot);

        Assert.Equal("Audio-CD 2 von 2 wird gerippt • 03 / 19 FLAC | Live-Konvertierung 21 / 37 AAC 192 kbps", view.TeletextText);
        Assert.Equal("15 % | 21 / 37 gerippt", view.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Source, view.ProgressVisualKind);
    }

    [Fact]
    public void PausedSnapshot_KeepsBothProgressContexts()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project", 1, 2, 5, 12, 5, 12, 3, new[] { 4, 5 },
            ExportPreset.Parse("AAC Mono 64 kbps"), "FLAC", isPaused: true);

        var view = _formatter.Format(snapshot);

        Assert.StartsWith("Pause • Audio-CD 1 von 2 • 05 / 12 FLAC | Live-Konvertierung 3 / 12 AAC 64 kbps Mono", view.TeletextText);
        Assert.Contains("pausiert", view.ProgressText);
    }

    [Fact]
    public void WaitingForNextDisc_KeepsStructuredDiscChangeStatus()
    {
        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            currentDisc: 1,
            totalDiscs: 2,
            rippedCurrentDisc: 18,
            totalCurrentDisc: 18,
            rippedProject: 18,
            totalProject: 18,
            convertedProject: 16,
            activeTrackNumbers: new[] { 17, 18 },
            preset: ExportPreset.Parse("AAC Stereo 192 kbps"),
            workingFormat: "FLAC",
            currentDiscFinished: true);

        var view = _formatter.Format(snapshot);

        Assert.Equal("Audio-CD 2 von 2 einlegen | Live-Konvertierung 16 / 18 AAC 192 kbps", view.TeletextText);
        Assert.Equal("88 % | 16 / 18 konvertiert | Aktive Jobs: 17, 18", view.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Conversion, view.ProgressVisualKind);
    }

    [Fact]
    public void ReadySnapshot_UsesAudioDiscReviewText()
    {
        var snapshot = _adapter.CreateReadySnapshot(
            "project", 2, 26, ExportPreset.Parse("AAC Stereo 128 kbps"), "FLAC");

        var view = _formatter.Format(snapshot);

        Assert.Equal("Audio-CD 2 von 2 fertig | 26 / 26 AAC 128 kbps | Bereit zum Zusammenfügen", view.TeletextText);
        Assert.Equal("100 %", view.ProgressText);
    }
}
