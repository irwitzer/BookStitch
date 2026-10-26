using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalizationBoundaryTests
{
    [Fact]
    public void ProcessingActionsUseStableIdsAndUnderstandLegacyGerman()
    {
        Assert.Equal(ProcessingActionIds.Reuse, ProcessingActionIds.Normalize("Übernehmen"));
        Assert.Equal(ProcessingActionIds.Convert, ProcessingActionIds.Normalize("Konvertieren"));
        Assert.Equal(ProcessingActionIds.RipFlac, ProcessingActionIds.Normalize("FLAC rippen"));
        Assert.Equal("Übernehmen", ProcessingActionPresentation.Format(ProcessingActionIds.Reuse));
    }

    [Fact]
    public void BlockingWarningUsesCodeIndependentOfDisplayText()
    {
        var track = new TrackInfo
        {
            Warning = "An arbitrary imported note",
            FileWarningText = "Translated text that contains no German marker",
            WarningCodes = TrackWarningCodes.MissingSource
        };

        Assert.True(track.HasBlockingFileWarning);
        new TrackListWarningService().Apply([track]);
        Assert.Equal("Quelldatei fehlt", track.FileWarningText);
    }
}
