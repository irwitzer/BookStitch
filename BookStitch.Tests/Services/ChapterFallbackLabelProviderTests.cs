using System.Globalization;
using System.Xml.Linq;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterFallbackLabelProviderTests
{
    [Fact]
    public void GetLabel_AllExplicitUiLanguagesUseTheirShippedResource()
    {
        foreach (var choice in UiLanguageService.Choices.Where(choice => choice.Id != UiLanguageIds.System))
        {
            var culture = CultureInfo.GetCultureInfo(choice.Id);
            Assert.Equal(ReadGenericChapterLabel(culture), ChapterFallbackLabelProvider.GetLabel(culture));
        }
    }

    [Theory]
    [InlineData("de-DE", "Kapitel")]
    [InlineData("en-US", "Chapter")]
    [InlineData("fr-FR", "Chapitre")]
    [InlineData("es-ES", "Capítulo")]
    [InlineData("cs-CZ", "Kapitola")]
    public void GetLabel_CurrentShippedUiLanguagesUseExpectedFallback(string cultureName, string expected)
    {
        Assert.Equal(expected, ChapterFallbackLabelProvider.GetLabel(CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void GetLabel_UnsupportedCultureUsesNeutralResourceFallback()
    {
        Assert.Equal("Chapter", ChapterFallbackLabelProvider.GetLabel(CultureInfo.GetCultureInfo("is-IS")));
    }

    private static string ReadGenericChapterLabel(CultureInfo culture)
    {
        var neutralPath = RepositorySource.Find(["BookStitch", "Resources", "LocalizationResources.en.resx"]);
        var resourceDirectory = Path.GetDirectoryName(neutralPath)!;

        for (var current = culture; current.Name.Length > 0; current = current.Parent)
        {
            var satellitePath = Path.Combine(resourceDirectory, $"LocalizationResources.{current.Name}.resx");
            if (File.Exists(satellitePath))
                return ReadValue(satellitePath, "GenericChapterLabel");
        }

        return ReadValue(neutralPath, "GenericChapterLabel");
    }

    private static string ReadValue(string path, string key) =>
        XDocument.Load(path).Root!.Elements("data")
            .Single(element => string.Equals(element.Attribute("name")?.Value, key, StringComparison.Ordinal))
            .Element("value")!.Value;
}
