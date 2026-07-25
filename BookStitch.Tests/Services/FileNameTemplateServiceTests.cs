using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class FileNameTemplateServiceTests
{
    [Fact]
    public void BuildOutputFileName_SupportsAlbumAndSeriesPlaceholders()
    {
        var result = FileNameTemplateService.BuildOutputFileName(
            "Titel",
            "Autor",
            "Sprecher",
            "{Reihe} - {Autor} - {Album} - {Titel}",
            ".m4b",
            "Album",
            "Reihe");

        Assert.Equal("Reihe - Autor - Album - Titel.m4b", result);
    }

    [Fact]
    public void BuildOutputFileName_SkipsMissingOptionalMetadataWithoutDoubleSeparators()
    {
        var result = FileNameTemplateService.BuildOutputFileName(
            "Titel",
            "Autor",
            null,
            "{Reihe} - {Autor} - {Sprecher} - {Titel}",
            ".m4a");

        Assert.Equal("Autor - Titel.m4a", result);
    }
}
