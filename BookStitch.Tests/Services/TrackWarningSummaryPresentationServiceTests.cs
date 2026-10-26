using System.Globalization;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackWarningSummaryPresentationServiceTests
{
    [Fact]
    public void Create_ReturnsInvisiblePresentationWithoutWarnings()
    {
        var result = TrackWarningSummaryPresentationService.Create(0, 0);

        Assert.Equal(string.Empty, result.FileWarningSummary);
        Assert.Equal(string.Empty, result.ChapterWarningSummary);
        Assert.False(result.HasFileWarnings);
        Assert.False(result.HasChapterWarnings);
        Assert.False(result.ShowSeparator);
        Assert.False(result.IsVisible);
    }

    [Fact]
    public void Create_UsesSingularLabelsForOneWarning()
    {
        var result = TrackWarningSummaryPresentationService.Create(1, 1);

        Assert.Equal("1 Dateiwarnung", result.FileWarningSummary);
        Assert.Equal("1 Kapitelwarnung", result.ChapterWarningSummary);
        Assert.True(result.ShowSeparator);
        Assert.True(result.IsVisible);
    }

    [Fact]
    public void Create_UsesPluralLabelsAndIndependentVisibility()
    {
        var result = TrackWarningSummaryPresentationService.Create(3, 0);

        Assert.Equal("3 Dateiwarnungen", result.FileWarningSummary);
        Assert.Equal(string.Empty, result.ChapterWarningSummary);
        Assert.True(result.HasFileWarnings);
        Assert.False(result.HasChapterWarnings);
        Assert.False(result.ShowSeparator);
        Assert.True(result.IsVisible);
    }

    [Fact]
    public void Create_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TrackWarningSummaryPresentationService.Create(-1, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TrackWarningSummaryPresentationService.Create(0, -1));
    }

    [Fact]
    public void Create_UsesTheCurrentUiCultureForWarningSummaries()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var result = TrackWarningSummaryPresentationService.Create(1, 3);
            Assert.Equal("1 advertencia de archivo", result.FileWarningSummary);
            Assert.Equal("3 advertencias de capítulo", result.ChapterWarningSummary);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
