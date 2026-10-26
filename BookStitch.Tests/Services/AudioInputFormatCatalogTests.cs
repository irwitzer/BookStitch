using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioInputFormatCatalogTests
{
    [Fact]
    public void All_ContainsCurrentInputFormatsWithWmaAsLegacy()
    {
        Assert.Equal(
            [".mp3", ".m4a", ".m4b", ".aac", ".wav", ".flac", ".wma", ".ogg", ".oga", ".opus"],
            AudioInputFormatCatalog.All.Select(format => format.Extension).ToArray());
        Assert.Equal(
            AudioInputFormatSupportLevel.Legacy,
            AudioInputFormatCatalog.All.Single(format => format.Extension == ".wma").SupportLevel);
        Assert.All(
            AudioInputFormatCatalog.All.Where(format => format.Extension is ".ogg" or ".oga" or ".opus"),
            format => Assert.Equal(AudioInputFormatSupportLevel.Standard, format.SupportLevel));
    }

    [Fact]
    public void All_DoesNotContainDuplicateExtensions()
    {
        var duplicateExtensions = AudioInputFormatCatalog.All
            .GroupBy(format => format.Extension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicateExtensions);
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".m4b")]
    [InlineData(".aac")]
    [InlineData(".wav")]
    [InlineData(".flac")]
    [InlineData(".wma")]
    [InlineData(".ogg")]
    [InlineData(".oga")]
    [InlineData(".opus")]
    [InlineData("WMA")]
    [InlineData("OPUS")]
    public void IsLocalImportExtension_AcceptsCurrentFormatsCaseInsensitively(string extension)
    {
        Assert.True(AudioInputFormatCatalog.IsLocalImportExtension(extension));
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".m4b")]
    [InlineData(".aac")]
    [InlineData(".wav")]
    [InlineData(".flac")]
    [InlineData(".wma")]
    [InlineData(".ogg")]
    [InlineData(".oga")]
    [InlineData(".opus")]
    [InlineData("WMA")]
    [InlineData("OPUS")]
    public void IsDataDiscImportExtension_AcceptsCurrentFormatsCaseInsensitively(string extension)
    {
        Assert.True(AudioInputFormatCatalog.IsDataDiscImportExtension(extension));
    }

    [Fact]
    public void OpenFileDialogFilter_IsDerivedFromLocalImportExtensions()
    {
        var expectedExtensions = string.Join(
            ";",
            AudioInputFormatCatalog.LocalImportExtensions.Select(extension => "*" + extension));

        Assert.Equal(
            $"Audiodateien|{expectedExtensions}|Alle Dateien|*.*",
            AudioInputFormatCatalog.OpenFileDialogFilter);
    }
}
