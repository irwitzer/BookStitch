using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscWorkflowStatusAdapterTests
{
    private readonly Mp3DiscWorkflowStatusAdapter _adapter = new();
    private readonly WorkflowStatusFormatter _formatter = new();

    [Fact]
    public void RunningSnapshot_FormatsCopyAndLiveConversion()
    {
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");

        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            ProjectPipelineState.AcquiringSources,
            currentDisc: 2,
            totalDiscs: 4,
            copiedCurrentDisc: 8,
            totalCurrentDisc: 16,
            copiedProject: 16,
            totalProject: 26,
            convertedProject: 7,
            activeTrackNumbers: [1, 3, 4, 5, 7],
            preset);

        var view = _formatter.Format(snapshot);

        Assert.Equal("MP3-CD 2 von 4 wird kopiert • 08 / 16 | Live-Konvertierung 7 / 26 AAC 128 kbps", view.TeletextText);
        Assert.Equal("50 % | 16 / 26 kopiert | Konvertierung: 01, 03, 04, 05, 07", view.ProgressText);
        Assert.Equal(50, view.ProgressPercent);
    }

    [Fact]
    public void WaitingSnapshot_FormatsNextDiscAndConversionProgress()
    {
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");

        var snapshot = _adapter.CreateRunningSnapshot(
            "project",
            ProjectPipelineState.AcquiringSources,
            currentDisc: 2,
            totalDiscs: 4,
            copiedCurrentDisc: 16,
            totalCurrentDisc: 16,
            copiedProject: 26,
            totalProject: 26,
            convertedProject: 21,
            activeTrackNumbers: [3, 5, 7, 8],
            preset,
            currentDiscFinished: true);

        var view = _formatter.Format(snapshot);

        Assert.Equal("MP3-CD 3 von 4 einlegen | Live-Konvertierung 21 / 26 AAC 128 kbps", view.TeletextText);
        Assert.Equal("80 % | 21 / 26 konvertiert | Aktive Jobs: 03, 05, 07, 08", view.ProgressText);
    }

    [Fact]
    public void ReadySnapshot_UsesAgreedReviewText()
    {
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");

        var view = _formatter.Format(_adapter.CreateReadySnapshot("project", 4, 26, preset));

        Assert.Equal("MP3-CD 4 von 4 fertig | 26 / 26 AAC 128 kbps | Bereit zum Zusammenfügen", view.TeletextText);
        Assert.Equal("100 %", view.ProgressText);
    }
}
