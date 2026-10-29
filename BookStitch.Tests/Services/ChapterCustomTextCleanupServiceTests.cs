using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCustomTextCleanupServiceTests
{
    [Theory]
    [InlineData(99, "099")]
    [InlineData(100, "100")]
    [InlineData(999, "0999")]
    [InlineData(1000, "1000")]
    [InlineData(10000, "10000")]
    public void Clean_RemovesProvenSourceOrdinalAcrossDigitBoundaries(int ordinal, string number)
    {
        Assert.Equal("Armageddon", _service.CleanTagTitle(number + " - Armageddon", ordinal));
        Assert.Equal("Armageddon", _service.CleanFileName(number + "_Armageddon.mp3", ordinal));
    }

    [Theory]
    [InlineData("1984 Orwell", 1)]
    [InlineData("100 Jahre Einsamkeit", 7)]
    [InlineData("100Jahre", 100)]
    [InlineData("100 - Armageddon", null)]
    public void Clean_DoesNotRemoveUnprovenOrAttachedSemanticNumbers(string title, int? ordinal)
    {
        Assert.Equal(title, _service.CleanTagTitle(title, ordinal));
        Assert.Equal(title, _service.CleanFileName(title + ".mp3", ordinal));
    }

    [Theory]
    [InlineData("Kapitel 27", "Kapitel")]
    [InlineData("Chapter 27", "Chapter")]
    [InlineData("Chapitre 27", "Chapitre")]
    [InlineData("Capítulo 27", "Capítulo")]
    [InlineData("Глава 27", "Глава")]
    [InlineData("Kapitel XXVII", "Kapitel")]
    [InlineData("Chapter twenty seven", "Chapter")]
    public void CleanTagTitle_ProvenOrdinalKeepsMarkerFallbackAcrossPaddingBoundary(string title, string expected)
    {
        Assert.Equal(expected, _service.CleanTagTitle("099 " + title, 99));
        Assert.Equal(expected, _service.CleanTagTitle("100 " + title, 100));
        Assert.Equal(expected, _service.CleanTagTitle("125 " + title, 125));
        Assert.Equal(expected, _service.CleanTagTitle("0999 " + title, 999));
        Assert.Equal(expected, _service.CleanTagTitle("1000 " + title, 1000));
    }

    [Theory]
    [InlineData("125 Zweiter Teil Kapitel 34", 125, "Zweiter Teil")]
    [InlineData("100 Kapitel 27", null, "100")]
    [InlineData("125 Kapitel 34", 1, "125")]
    [InlineData("100", 100, "")]
    [InlineData("100 - Ankunft", 100, "Ankunft")]
    [InlineData("100_Kapitel_27", 100, "Kapitel")]
    public void CleanTagTitle_PreCleanupOrdinalRequiresProofAndPreservesExistingSemantics(
        string title, int? sourceOrdinal, string expected)
    {
        Assert.Equal(expected, _service.CleanTagTitle(title, sourceOrdinal));
    }

    private readonly ChapterCustomTextCleanupService _service = new();

    [Theory]
    [InlineData("Zaubermus und Marzipan.mp3.mp3", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.mp3.aac.aac", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.MP3.AaC.OGG", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.MP3.Ogg.M4A.AAC.mp3", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.mp3.wav.wav", "Zaubermus und Marzipan")]
    public void CleanFileName_RemovesRepeatedAndMixedKnownAudioExtensions(
        string fileName,
        string expected)
    {
        Assert.Equal(expected, _service.CleanFileName(fileName));
    }

    [Fact]
    public void CleanFileName_DoesNotStripEmbeddedExtensionLikeText()
    {
        Assert.Equal("Kapitel.mp3x", _service.CleanFileName("Kapitel.mp3x"));
    }

    [Theory]
    [InlineData("Zaubermus und Marzipan.mp3.mp3", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.mp3.aac.aac", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.MP3.Ogg.M4A.AAC.mp3", "Zaubermus und Marzipan")]
    [InlineData("Zaubermus und Marzipan.mp3.wav.wav", "Zaubermus und Marzipan")]
    public void CleanTagTitle_RemovesRepeatedAndMixedKnownAudioExtensions(
        string tagTitle,
        string expected)
    {
        Assert.Equal(expected, _service.CleanTagTitle(tagTitle));
    }

    [Fact]
    public void CleanTagTitle_DoesNotStripEmbeddedExtensionLikeText()
    {
        Assert.Equal("Kapitel.mp3x", _service.CleanTagTitle("Kapitel.mp3x"));
    }

    [Theory]
    [InlineData("001_Prolog.mp3", "Prolog")]
    [InlineData("002_Die Reise.mp3", "Die Reise")]
    [InlineData("003_Das Haus.mp3", "Das Haus")]
    [InlineData("001_01_-_Die_Flüsse_von_London.flac", "Die Flüsse von London")]
    public void CleanFileName_RemovesLegacyTechnicalPrefixes(string fileName, string expected)
    {
        Assert.Equal(expected, _service.CleanFileName(fileName));
    }

    [Theory]
    [InlineData("Chapitre 3 - Arrivée.mp3", "Arrivée")]
    [InlineData("Kapitola 3 - Příchod.flac", "Příchod")]
    [InlineData("第1章标题.m4b", "标题")]
    [InlineData("第一章タイトル.aac", "タイトル")]
    public void CleanFileName_UsesTheSharedInternationalTechnicalLexicon(string fileName, string expected)
    {
        Assert.Equal(expected, _service.CleanFileName(fileName));
    }

    [Theory]
    [InlineData("Chapitre 3 - Arrivée", "Arrivée")]
    [InlineData("Kapitola 3 - Příchod", "Příchod")]
    [InlineData("第1章标题", "标题")]
    public void CleanTagTitle_MatchesAutoNormalizationForTechnicalPrefixes(string value, string expected)
    {
        Assert.Equal(expected, _service.CleanTagTitle(value));
        Assert.Equal(expected, new ChapterCandidateNormalizer()
            .Normalize(ChapterCandidateSource.TrackTitle, value).NormalizedText);
    }
}
