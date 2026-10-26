using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MetadataSummaryPresentationServiceTests
{
    [Theory]
    [InlineData("de", "Hörbuch-Metadaten")]
    [InlineData("en", "Audiobook metadata")]
    [InlineData("es", "Metadatos del audiolibro")]
    [InlineData("fr", "Métadonnées du livre audio")]
    [InlineData("cs", "Metadata audioknihy")]
    public void Create_UsesFallbackTitleAndEmptyDetailsWithoutMetadata(string language, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            var result = MetadataSummaryPresentationService.Create(
                null,
                null,
                null,
                null,
                null);

            Assert.Equal(expected, result.Title);
            Assert.Equal(string.Empty, result.Details);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Create_TrimsTitleAndJoinsDistinctDetails()
    {
        var result = MetadataSummaryPresentationService.Create(
            "  Das Buch  ",
            "  Autor  ",
            "Sprecher",
            "autor",
            "  Audiobook  ");

        Assert.Equal("Das Buch", result.Title);
        Assert.Equal("Autor · Sprecher · Audiobook", result.Details);
    }

    [Fact]
    public void Create_IgnoresBlankValues()
    {
        var result = MetadataSummaryPresentationService.Create(
            "Titel",
            " ",
            "Sprecher",
            string.Empty,
            null);

        Assert.Equal("Titel", result.Title);
        Assert.Equal("Sprecher", result.Details);
    }
}
