using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TagMetadataReaderTests
{
    public static IEnumerable<object[]> FormatFamilies =>
    [
        [MetadataTagFormat.Id3v2, "TXXX"],
        [MetadataTagFormat.Ape, "APEv2"],
        [MetadataTagFormat.Mp4, "----"],
        [MetadataTagFormat.Xiph, "Xiph"],
        [MetadataTagFormat.Asf, "ASF"],
        [MetadataTagFormat.Riff, "RIFF"]
    ];

    [Fact]
    public void Read_WhenTagsAreInvalid_ReturnsFailedResultWithoutThrowing()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "broken.mp3");
        File.WriteAllText(path, "not audio");

        var result = new TagMetadataReader().Read(path);

        Assert.True(result.Failed);
        Assert.False(result.HasEmbeddedCover);
        Assert.Empty(result.Original.RawTags);
        Assert.Contains(result.Diagnostics, item => item.Contains("Tagcontainer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_WhenMp4ContainsMalformedEmptyFreeform_PreservesOtherMetadata()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Mp4EmptyFreeformSeriesFixture.m4b");

        var result = new TagMetadataReader().Read(path);

        Assert.False(result.Failed);
        Assert.Equal([new SeriesEntry("Archiv der Sterne", "1.7")], result.Original.Series);
        Assert.Equal("1.7", result.Original.Volume);
        Assert.Contains(result.Diagnostics, item =>
            item.Contains("TRANSLATOR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadFirstEmbeddedCover_WhenTagsAreInvalid_ReturnsNull()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "broken.mp3");
        File.WriteAllText(path, "not audio");

        Assert.Null(new TagMetadataReader().ReadFirstEmbeddedCover(path));
    }

    [Fact]
    public void CreateFromNamedTagsForTests_PreservesNativeRawTagFormatAndDoesNotSplitSingleStrings()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTHOR"] = ["Müller; Muller / Mueller"],
                ["GENRE"] = ["Drama; Hörbuch"],
                ["BARCODE"] = ["9781234567890"]
            });

        Assert.Equal(["Müller; Muller / Mueller"], metadata.Authors);
        Assert.Equal(["Drama; Hörbuch"], metadata.Genres);
        Assert.Equal("9781234567890", metadata.Barcode);
        var raw = Assert.Single(metadata.RawTagEntries, entry => entry.OriginalKey == "AUTHOR");
        Assert.Equal(MetadataTagFormat.Xiph, raw.TagFormat);
        Assert.Equal("Xiph", raw.Namespace);
    }

    [Theory]
    [MemberData(nameof(FormatFamilies))]
    public void CreateFromNamedTagsForTests_WhenTagsAreEmpty_ReturnsEmptyEvidence(
        MetadataTagFormat format,
        string expectedNamespace)
    {
        _ = expectedNamespace;

        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            format,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(metadata.RawTagEntries);
        Assert.Empty(metadata.Authors);
        Assert.Equal(string.Empty, metadata.Title);
    }

    [Theory]
    [MemberData(nameof(FormatFamilies))]
    public void CreateFromNamedTagsForTests_PreservesMultipleValuesAndUnknownRawTagsPerFormatFamily(
        MetadataTagFormat format,
        string expectedNamespace)
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            format,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTHOR"] = ["Autor Eins", "Autor Zwei"],
                ["BOOKSTITCH_UNKNOWN_CUSTOM"] = ["Alpha", "Beta"]
            });

        Assert.Equal(["Autor Eins", "Autor Zwei"], metadata.Authors);
        var raw = Assert.Single(metadata.RawTagEntries, entry => entry.OriginalKey.Contains("BOOKSTITCH_UNKNOWN_CUSTOM", StringComparison.Ordinal));
        Assert.Equal(["Alpha", "Beta"], raw.Values);
        Assert.Equal(format, raw.TagFormat);
        Assert.Equal(expectedNamespace, raw.Namespace);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_KeepsDistinctUnicodeNames()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AUTHOR"] = ["Müller", "Muller"]
            });

        Assert.Equal(["Müller", "Muller"], metadata.Authors);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_PrefersExactCandidateOverCompatibleAlias()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ARTIST"] = ["Alias Artist"],
                ["AUTHOR"] = ["Exact Author"]
            });

        Assert.Equal(["Exact Author"], metadata.Authors);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_MapsNarratorBarcodeSeriesAndSortFields()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["NARRATOR"] = ["Sprecherin"],
                ["BARCODE"] = ["9781234567890"],
                ["SERIES"] = ["Reihe"],
                ["SERIES-PART"] = ["2"],
                ["NARRATORSORT"] = ["Sprecherin, Die"]
            });

        Assert.Equal(["Sprecherin"], metadata.Narrators);
        Assert.Equal("9781234567890", metadata.Barcode);
        Assert.Equal([new SeriesEntry("Reihe", "2")], metadata.Series);

        Assert.Equal(["Sprecherin, Die"], metadata.SortNarrators);
        Assert.Contains(metadata.RawTagEntries, entry =>
            entry.OriginalKey == "----:com.apple.iTunes:NARRATOR" &&
            entry.TagFormat == MetadataTagFormat.Mp4);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_MapsComposerExactlyAndKeepsNarratorCompatibleAlias()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Id3v2,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TCOM"] = ["Komponist A", "Komponist B"]
            });

        Assert.Equal(["Komponist A", "Komponist B"], metadata.Composers);
        Assert.Equal(["Komponist A", "Komponist B"], metadata.Narrators);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_ExplicitNarratorStillBeatsComposerCompatibleAlias()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["COMPOSER"] = ["Komponist"],
                ["NARRATOR"] = ["Sprecher"]
            });

        Assert.Equal(["Komponist"], metadata.Composers);
        Assert.Equal(["Sprecher"], metadata.Narrators);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_PreservesBarcodeAliasAndAllCatalogNumbers()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["EAN"] = ["9781234567890"],
                ["CATALOGNUMBER"] = ["CAT-1", "CAT-2"]
            });

        Assert.Equal("9781234567890", metadata.Barcode);
        Assert.Equal(["CAT-1", "CAT-2"], metadata.CatalogNumbers);
        Assert.Contains(metadata.RawTagEntries, entry => entry.OriginalKey == "EAN");
    }

    [Fact]
    public void CreateFromNamedTagsForTests_PreservesExplicitSourceYear()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["YEAR"] = ["1997"],
                ["RELEASEDATE"] = ["31.01.2024"]
            });

        Assert.Equal(1997u, metadata.Year);
        Assert.Equal("31.01.2024", metadata.ReleaseDate);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_MapsAbridgedStatusAndAudioWorkTypeEvidence()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ABRIDGED"] = ["1"],
                ["AUDIOWORKTYPE"] = ["Audio Drama"]
            });

        Assert.Equal("1", metadata.AbridgedStatus);
        Assert.Equal("Audio Drama", metadata.AudioWorkType);
        Assert.Contains(metadata.RawTagEntries, entry => entry.OriginalKey == "----:com.apple.iTunes:ABRIDGED");
        Assert.Contains(metadata.RawTagEntries, entry => entry.OriginalKey == "----:com.apple.iTunes:AUDIOWORKTYPE");
    }

    [Fact]
    public void CreateFromNamedTagsForTests_MapsPreserveRoleRightsAndLanguageFields()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["TRANSLATOR"] = ["Übersetzerin"],
                ["PERFORMERS"] = ["Darsteller"],
                ["CONTRIBUTOR"] = ["Beitrag"],
                ["HOST"] = ["Host"],
                ["GUEST"] = ["Gast"],
                ["SPEAKER"] = ["Stimme"],
                ["SCRIPT"] = ["Latn"],
                ["PHONOGRAMCOPYRIGHT"] = ["P 2024 Label"],
                ["LICENSE"] = ["CC-BY"],
                ["ORIGINALLANGUAGE"] = ["en"],
                ["TARGETLANGUAGE"] = ["de-CH"]
            });

        Assert.Equal(["Übersetzerin"], metadata.Translators);
        Assert.Equal(["Darsteller"], metadata.Performers);
        Assert.Equal(["Beitrag"], metadata.Contributors);
        Assert.Equal(["Host"], metadata.Hosts);
        Assert.Equal(["Gast"], metadata.Guests);
        Assert.Equal(["Stimme"], metadata.Speakers);
        Assert.Equal("Latn", metadata.Script);
        Assert.Equal("P 2024 Label", metadata.PhonogramCopyright);
        Assert.Equal("CC-BY", metadata.License);
        Assert.Equal(["en"], metadata.OriginalLanguages);
        Assert.Equal(["de-CH"], metadata.TargetLanguages);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_UsesBookStitchSeriesPreservationAsCompatibleRoundtrip()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["BOOKSTITCH_SERIES"] =
                [
                    """
                    {"version":1,"series":[{"name":"Hauptreihe","part":"4"},{"name":"Nebenreihe","part":"1"}]}
                    """
                ]
            });

        Assert.Equal(
            [new SeriesEntry("Hauptreihe", "4"), new SeriesEntry("Nebenreihe", "1")],
            metadata.Series);
        Assert.Contains(metadata.RawTagEntries, entry => entry.OriginalKey == "----:com.apple.iTunes:BOOKSTITCH_SERIES");
    }

    [Fact]
    public void CreateFromNamedTagsForTests_WhenBookStitchSeriesPreservationIsInvalid_KeepsRawTagWithoutInventingSeries()
    {
        var result = TagMetadataReader.CreateReadResultFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["BOOKSTITCH_SERIES"] = ["not json"]
            });

        var metadata = result.Original;
        Assert.Empty(metadata.Series);
        Assert.Contains(metadata.RawTagEntries, entry => entry.OriginalKey == "----:com.apple.iTunes:BOOKSTITCH_SERIES");
        Assert.Contains(result.Diagnostics, item => item.Contains("BOOKSTITCH_SERIES", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateFromNamedTagsForTests_WhenBookStitchSeriesPreservationHasFutureVersion_FallsBackToExternalSeries()
    {
        var result = TagMetadataReader.CreateReadResultFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["SERIES"] = ["Externe Reihe"],
                ["SERIES-PART"] = ["7"],
                ["BOOKSTITCH_SERIES"] =
                [
                    """
                    {"version":99,"series":[{"name":"Interne Reihe","part":"1"}]}
                    """
                ]
            });

        Assert.Equal([new SeriesEntry("Externe Reihe", "7")], result.Original.Series);
        Assert.Contains(result.Original.RawTagEntries, entry => entry.OriginalKey == "----:com.apple.iTunes:BOOKSTITCH_SERIES");
        Assert.Contains(result.Diagnostics, item => item.Contains("unbekannte Version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateReadResultFromNamedTagsForTests_WhenRawTagValueIsOverlong_TruncatesAndReportsDiagnostic()
    {
        var result = TagMetadataReader.CreateReadResultFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = [new string('A', 20_000)]
            });

        var raw = Assert.Single(result.Original.RawTagEntries);
        Assert.Equal(16 * 1024, raw.Values[0].Length);
        Assert.Contains(result.Diagnostics, item => item.Contains("Überlänge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateFromNamedTagsForTests_RemovesCompatibilityDescriptionOnlyOnExactSuffixEvidence()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = ["Klappentext\n\nBookStitch Compatibility Block"],
                ["BOOKSTITCH_COMPATIBILITY"] = ["BookStitch Compatibility Block"]
            });

        Assert.Equal("Klappentext", metadata.Description);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_RemovesExactBulletCompatibilitySuffixAndPreservesUserDescription()
    {
        const string block = "• Title: Book \n• Author: Eva Morgen \n• Series: Zwischenbände 0.1";
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = ["User-Beschreibung.\n\n" + block],
                ["BOOKSTITCH_COMPATIBILITY"] = [block]
            });

        Assert.Equal("User-Beschreibung.", metadata.Description);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_DoesNotTrimSimilarButNonExactBulletSuffix()
    {
        const string block = "• Title: Book \n• Author: Eva Morgen";
        const string description = "User-Beschreibung.\n\n• Title: Book \n• Author: Andere Person";
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = [description],
                ["BOOKSTITCH_COMPATIBILITY"] = [block]
            });

        Assert.Equal(description, metadata.Description);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_DoesNotRemoveDescriptionWithoutExactCompatibilitySuffix()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = ["Klappentext\n\nAnderer Block"],
                ["BOOKSTITCH_COMPATIBILITY"] = ["BookStitch Compatibility Block"]
            });

        Assert.Equal("Klappentext\n\nAnderer Block", metadata.Description);
    }

    [Fact]
    public void CreateFromNamedTagsForTests_RemovesBookStitchCompatibilityDescriptionWhenMarkerIsExact()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DESCRIPTION"] = ["Klappentext\n\nBookStitch Compatibility Data\nTitle: Titel\nLength: 25:02:03"],
                ["BOOKSTITCH_COMPATIBILITY"] = ["BookStitch Compatibility Data"]
            });

        Assert.Equal("Klappentext", metadata.Description);
    }
}
