using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterMarkerCatalogTests
{
    [Theory]
    [InlineData("Kapittel")]
    [InlineData("Luku")]
    [InlineData("Bölüm")]
    [InlineData("Chương")]
    [InlineData("অধ্যায়")]
    public void NewNumericOnlyMarkersDoNotMakeBareWordsGenericOrTechnical(string marker)
    {
        Assert.False(ChapterMarkerCatalog.IsGenericMarker(marker));
        Assert.False(ChapterMarkerCatalog.IsTechnicalNumberingMarker(marker));
        Assert.False(ChapterMarkerCatalog.IsTechnicalStructureName(marker));
        Assert.False(ChapterMarkerCatalog.TryMatchStructuredChapterPrefix(marker, out _));
        Assert.True(ChapterMarkerCatalog.TryMatchStructuredChapterPrefix(marker + " 12 – Title", out var length));
        Assert.Equal(marker + " 12", (marker + " 12 – Title")[..length]);
    }

    [Theory]
    [InlineData("Kapitel")]
    [InlineData("Chapter")]
    [InlineData("Capítulo")]
    [InlineData("Chapitre")]
    [InlineData("Capitolo")]
    [InlineData("Hoofdstuk")]
    public void TechnicalMarkers_CoverEveryStartLanguage(string marker)
    {
        Assert.True(ChapterMarkerCatalog.IsTechnicalNumberingMarker(marker));
    }

    [Theory]
    [InlineData("Teil")]
    [InlineData("Band")]
    [InlineData("Part")]
    [InlineData("Parte")]
    [InlineData("Partie")]
    [InlineData("Deel")]
    public void AmbiguousStructuralWords_AreGenericButNotTechnical(string marker)
    {
        Assert.True(ChapterMarkerCatalog.IsGenericMarker(marker));
        Assert.False(ChapterMarkerCatalog.IsTechnicalNumberingMarker(marker));
    }

    [Theory]
    [InlineData("Kapitola")]
    [InlineData("Rozdział")]
    [InlineData("Глава")]
    [InlineData("Κεφάλαιο")]
    public void TechnicalMarkers_CoverAdditionalWritingSystems(string marker)
    {
        Assert.True(ChapterMarkerCatalog.IsTechnicalNumberingMarker(marker));
    }

    [Theory]
    [InlineData("Část")]
    [InlineData("Časť")]
    [InlineData("Część")]
    [InlineData("Часть")]
    [InlineData("Μέρος")]
    public void StructuralMarkers_AreGenericWithoutBecomingAggressivePrefixes(string marker)
    {
        Assert.True(ChapterMarkerCatalog.IsGenericMarker(marker));
        Assert.False(ChapterMarkerCatalog.IsTechnicalNumberingMarker(marker));
    }
}
