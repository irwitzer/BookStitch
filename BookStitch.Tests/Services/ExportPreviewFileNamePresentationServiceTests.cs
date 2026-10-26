using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportPreviewFileNamePresentationServiceTests
{
    [Fact]
    public void Create_InEnglishLocalizesTheLabelAndPreservesTheUnicodePath()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Output: no output planned yet", ExportPreviewFileNamePresentationService.Create(0, "ignored"));
            Assert.Equal("Output: 日本語/Книга.m4b", ExportPreviewFileNamePresentationService.Create(1, "日本語/Книга.m4b"));
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Create_WithoutTracks_ReturnsEmptyPlanMessage()
    {
        var result = ExportPreviewFileNamePresentationService.Create(
            trackCount: 0,
            outputRelativePathPreview: "ignored.m4b");

        Assert.Equal("Ausgabe: noch keine Ausgabe geplant", result);
    }

    [Fact]
    public void Create_WithTracks_ReturnsOutputPathPreview()
    {
        var result = ExportPreviewFileNamePresentationService.Create(
            trackCount: 3,
            outputRelativePathPreview: "Autor/Album.m4b");

        Assert.Equal("Ausgabe: Autor/Album.m4b", result);
    }
}
