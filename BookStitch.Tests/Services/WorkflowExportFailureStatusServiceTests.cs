using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class WorkflowExportFailureStatusServiceTests
{
    private readonly WorkflowExportFailureStatusService _service = new();

    [Fact]
    public void Create_ForExportTrackException_UsesTrackErrorSummaryAndTrackNumber()
    {
        var error = _service.Create(new ExportTrackException(
            trackIndex: 17,
            fileName: "Track 17.aac",
            sourcePath: @"C:\\BookStitch\\Track 17.aac",
            innerException: new InvalidOperationException("FFmpeg konnte Track 17 nicht lesen.\nDetails folgen.")));

        Assert.Equal("FFmpeg konnte Track 17 nicht lesen.", error.Message);
        Assert.Equal(17, error.FailedTrackOrFileNumber);
    }

    [Fact]
    public void Create_ForGeneralException_UsesFirstMessageLineWithoutFailedTrackNumber()
    {
        var error = _service.Create(new InvalidOperationException(
            "Ausgabeordner ist nicht beschreibbar.\nZugriff verweigert."));

        Assert.Equal("Ausgabeordner ist nicht beschreibbar.", error.Message);
        Assert.Null(error.FailedTrackOrFileNumber);
    }

    [Fact]
    public void Create_ForMissingException_UsesStableFallbackText()
    {
        var error = _service.Create(null);

        Assert.Equal("Export fehlgeschlagen.", error.Message);
        Assert.Null(error.FailedTrackOrFileNumber);
    }
}
