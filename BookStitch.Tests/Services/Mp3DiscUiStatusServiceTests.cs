using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class Mp3DiscUiStatusServiceTests
{
    private readonly Mp3DiscUiStatusService _service = new();

    [Fact]
    public void ErrorAndDuplicateStates_RemainStable()
    {
        var analysis = _service.CreateAnalysisFailed(2);
        var duplicate = _service.CreateAlreadyImported(3, 5);
        var failed = _service.CreateImportFailed();

        Assert.Equal("MP3-CD-Analyse für CD 2 fehlgeschlagen.", analysis.StatusText);
        Assert.Equal("Bitte CD 3 von 5 einlegen.", duplicate.ExportProgressText);
        Assert.Equal("MP3-CD-Import fehlgeschlagen.", failed.StatusText);
        Assert.Equal("MP3-CD-Import fehlgeschlagen.", failed.ExportProgressText);
    }
}
