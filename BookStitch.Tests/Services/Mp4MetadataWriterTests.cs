using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp4MetadataWriterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Original_title_uses_independent_freeform_and_existing_output_policy(bool enabled)
    {
        var sink = new FakeMp4MetadataSink();
        var data = new FinalAudioTagData
        {
            Title = "Translated title", OriginalTitle = " Original work title ",
            DisabledFields = enabled ? new HashSet<ProjectMetadataField>() : [ProjectMetadataField.OriginalTitle]
        };
        new Mp4MetadataWriter().Write(sink, data);
        Assert.Equal("Translated title", sink.Title);
        if (enabled) Assert.Equal(new[] { "Original work title" }, sink.Freeforms["ORIGINALTITLE"]);
        else AssertNoFreeformValue(sink, "ORIGINALTITLE");
    }

    [Fact]
    public void Write_WritesExplicitMp4AtomsFreeformsAndCompatibilityBlock()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(
            sink,
            new FinalAudioTagData
            {
                Title = "Book",
                Album = "Album",
                Authors = ["Author A", "Author B"],
                Narrators = ["Narrator A", "Narrator B"],
                Composers = ["Composer A", "Composer B"],
                Genres = ["Audiobook", "Mystery"],
                Description = "Blurb",
                Series = [new SeriesEntry("Series", "3"), new SeriesEntry("Side", "1")],
                ISBN = "978123",
                CatalogNumbers = ["CAT-1", "CAT-2"],
                AudioWorkType = AudioWorkType.Audiobook,
                CompatibilityDescriptionEnabled = true,
                OutputPresentationCulture = "en-US"
            });

        Assert.Equal("Book", sink.Title);
        Assert.Equal(["Author A", "Author B"], sink.AlbumArtists);
        Assert.Equal(["Author A", "Author B"], sink.Performers);
        Assert.Equal(["Composer A", "Composer B"], sink.Composers);
        Assert.Equal(["Audiobook", "Mystery"], sink.Genres);
        Assert.Equal(["Narrator A", "Narrator B"], sink.TextAtoms["©nrt"]);
        Assert.Equal(["Series"], sink.TextAtoms["©mvn"]);
        Assert.Equal(["3"], sink.TextAtoms["©mvi"]);
        Assert.Equal(["Series"], sink.Freeforms["SERIES"]);
        Assert.Equal(["3"], sink.Freeforms["SERIES-PART"]);
        Assert.Equal(["978123"], sink.Freeforms["ISBN"]);
        Assert.Equal(["CAT-1", "CAT-2"], sink.Freeforms["CATALOGNUMBER"]);
        Assert.Contains("\"version\":1", sink.Freeforms["BOOKSTITCH_SERIES"][0]);
        Assert.Contains("\"name\":\"Side\"", sink.Freeforms["BOOKSTITCH_SERIES"][0]);
        Assert.Contains("• Title: Book", sink.Freeforms["BOOKSTITCH_COMPATIBILITY"][0]);
        Assert.Equal("Blurb\n\n" + sink.Freeforms["BOOKSTITCH_COMPATIBILITY"][0], sink.Description);
        Assert.True(sink.StikAudiobookWritten);
    }

    [Fact]
    public void Write_WhenComposerIsDisabled_ClearsNativeComposerWithoutTouchingNarrator()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(
            sink,
            new FinalAudioTagData
            {
                Narrators = ["Narrator"],
                Composers = ["Composer"],
                DisabledFields = new HashSet<ProjectMetadataField> { ProjectMetadataField.Composers }
            });

        Assert.Empty(sink.Composers);
        Assert.Equal(["Narrator"], sink.TextAtoms["©nrt"]);
    }

    [Fact]
    public void Write_WhenSeriesIsDisabled_SuppressesAllSeriesAtomsFreeformsAndCompatibilityLines()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(
            sink,
            new FinalAudioTagData
            {
                Title = "Book",
                Series = [new SeriesEntry("Series", "3")],
                DisabledFields = new HashSet<ProjectMetadataField> { ProjectMetadataField.Series }
            });

        Assert.Empty(sink.TextAtoms["©mvn"]);
        Assert.Empty(sink.TextAtoms["©mvi"]);
        AssertNoFreeformValue(sink, "SERIES");
        AssertNoFreeformValue(sink, "SERIES-PART");
        AssertNoFreeformValue(sink, "BOOKSTITCH_SERIES");
        Assert.DoesNotContain("Series:", sink.Description);
    }

    [Fact]
    public void Write_WhenDescriptionIsDisabled_DoesNotUseDescriptionAsCompatibilityBackdoor()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(
            sink,
            new FinalAudioTagData
            {
                Title = "Book",
                Description = "Blurb",
                Authors = ["Author"],
                DisabledFields = new HashSet<ProjectMetadataField> { ProjectMetadataField.Description }
            });

        Assert.Equal(string.Empty, sink.Description);
        Assert.Equal(["• Title: Book \n• Author: Author"], sink.Freeforms["BOOKSTITCH_COMPATIBILITY"]);
    }

    [Fact]
    public void Write_WhenCompatibilityDescriptionEmbeddingIsDisabled_StillWritesFreeform()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(sink, new FinalAudioTagData
        {
            Title = "Book",
            Description = "Blurb",
            CompatibilityDescriptionEnabled = false,
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("Blurb", sink.Description);
        Assert.Equal(["• Title: Book"], sink.Freeforms["BOOKSTITCH_COMPATIBILITY"]);
    }

    [Theory]
    [InlineData(AbridgedStatus.Abridged, AudioWorkType.Audiobook, "1", "Audiobook")]
    [InlineData(AbridgedStatus.Unabridged, AudioWorkType.AudioDrama, "0", "Audio Drama")]
    public void Write_UsesNormativeAbridgedAndAudioWorkTypeValues(
        AbridgedStatus abridged,
        AudioWorkType workType,
        string expectedAbridged,
        string expectedWorkType)
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(sink, new FinalAudioTagData
        {
            AbridgedStatus = abridged,
            AudioWorkType = workType
        });

        Assert.Equal([expectedAbridged], sink.Freeforms["ABRIDGED"]);
        Assert.Equal([expectedWorkType], sink.Freeforms["AUDIOWORKTYPE"]);
        Assert.True(sink.StikAudiobookWritten);
    }

    [Fact]
    public void Write_DoesNotInventDiscOrSourceTechnicalTags()
    {
        var sink = new FakeMp4MetadataSink();

        new Mp4MetadataWriter().Write(
            sink,
            new FinalAudioTagData
            {
                Title = "Book",
                MusicBrainzReleaseId = "release-id"
            });

        Assert.True(sink.StikAudiobookWritten);
        Assert.Equal(["release-id"], sink.Freeforms["MUSICBRAINZRELEASEID"]);
        Assert.False(sink.Freeforms.ContainsKey("ISRC"));
        Assert.False(sink.Freeforms.ContainsKey("MUSICBRAINZRECORDINGID"));
        Assert.False(sink.Freeforms.ContainsKey("REPLAYGAIN_TRACK_GAIN"));
        Assert.False(sink.Freeforms.ContainsKey("REPLAYGAIN_TRACK_PEAK"));
        Assert.False(sink.Freeforms.ContainsKey("REPLAYGAIN_ALBUM_GAIN"));
        Assert.False(sink.Freeforms.ContainsKey("REPLAYGAIN_ALBUM_PEAK"));
        Assert.False(sink.Freeforms.ContainsKey("ENCODER"));
        Assert.True(sink.SourceOnlyFieldsCleared);
    }

    private sealed class FakeMp4MetadataSink : IMp4MetadataSink
    {
        public Dictionary<string, IReadOnlyList<string>> TextAtoms { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<string>> Freeforms { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool StikAudiobookWritten { get; private set; }
        public bool SourceOnlyFieldsCleared { get; private set; }
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Album { get; set; } = "";
        public string Description { get; set; } = "";
        public string Comment { get; set; } = "";
        public string[] AlbumArtists { get; set; } = [];
        public string[] Performers { get; set; } = [];
        public string[] Composers { get; set; } = [];
        public string[] Genres { get; set; } = [];
        public string Publisher { get; set; } = "";
        public string Copyright { get; set; } = "";
        public string TitleSort { get; set; } = "";
        public string AlbumSort { get; set; } = "";
        public string[] AlbumArtistsSort { get; set; } = [];
        public string[] PerformersSort { get; set; } = [];
        public uint Year { get; set; }
        public uint Track { get; set; }
        public uint TrackCount { get; set; }

        public void SetTextAtom(string atom, IReadOnlyList<string> values)
        {
            TextAtoms[atom] = values.ToArray();
        }

        public void SetFreeform(string name, IReadOnlyList<string> values)
        {
            Freeforms[name] = values.ToArray();
        }

        public void ClearFreeforms()
        {
            Freeforms.Clear();
        }

        public void ClearSourceOnlyFields()
        {
            SourceOnlyFieldsCleared = true;
        }

        public void SetStikAudiobook()
        {
            StikAudiobookWritten = true;
        }

        public void SetCover(string coverPath)
        {
        }
    }

    private static void AssertNoFreeformValue(FakeMp4MetadataSink sink, string name)
    {
        Assert.False(
            sink.Freeforms.TryGetValue(name, out var values) && values.Count > 0,
            $"{name} should not contain MP4 freeform values.");
    }
}
