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
