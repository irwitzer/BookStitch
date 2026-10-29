using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackListWarningServiceTests
{
    private readonly TrackListWarningService _service = new();

    [Theory]
    [InlineData("019_book.mp3", 99, true)]
    [InlineData("Track 019.mp3", 99, true)]
    [InlineData("019_book.mp3", 19, false)]
    [InlineData("019_book.mp3", null, false)]
    [InlineData("019_book.mp3", 0, false)]
    [InlineData("019_book.mp3", -1, false)]
    [InlineData("book_019.mp3", 99, false)]
    [InlineData("book.mp3", 99, false)]
    [InlineData("000_book.mp3", 99, false)]
    [InlineData("123456789012345_book.mp3", 99, false)]
    public void Apply_compares_original_track_tag_with_filename_only(string fileName, int? sourceNumber, bool expected)
    {
        var row = new TrackInfo { FileName = fileName, SourceTrackNumber = sourceNumber,
            TrackNumber = 7, Index = 42, TagTitle = "Track 99", IsExcluded = true };
        _service.Apply([row]);
        Assert.Equal(expected, row.HasChapterWarning);
        if (expected)
            Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19), row.ChapterWarningText);
        Assert.False(row.HasFileWarning);
        Assert.Equal(sourceNumber, row.SourceTrackNumber);
        Assert.Equal(7, row.TrackNumber);
        Assert.Equal(42, row.Index);
        Assert.Equal(fileName, row.FileName);
    }

    [Fact]
    public void Apply_uses_preserved_metadata_without_inventing_a_source_tag_from_working_number()
    {
        var row = new TrackInfo { FileName = "019_book.mp3", TrackNumber = 19,
            SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 99 } };
        _service.Apply([row]);
        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19), row.ChapterWarningText);
        row.SourceMetadataFields = null;
        _service.Apply([row]);
        Assert.False(row.HasChapterWarning);
    }

    [Fact]
    public void Apply_combines_metadata_and_order_reasons_without_duplicates_and_clears_resolved_mismatch()
    {
        var first = new TrackInfo { TrackNumber = 20, SourceTrackNumber = 20, FileName = "020_book.mp3" };
        var row = new TrackInfo { TrackNumber = 19, SourceTrackNumber = 99, FileName = "019_book.mp3" };
        var rows = new List<TrackInfo> { first, row };
        _service.Apply(rows);
        Assert.Equal(new[] { LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19),
            TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder) }, TrackInfo.SplitWarningMessages(row.ChapterWarningText));
        var warning = row.ChapterWarningText;
        _service.Apply(rows);
        Assert.Equal(warning, row.ChapterWarningText);
        row.SourceTrackNumber = 19;
        _service.Apply(rows);
        Assert.Equal(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), row.ChapterWarningText);
        Assert.Same(first, rows[0]);
        Assert.Same(row, rows[1]);
    }

    [Fact]
    public void Complete_filename_inventory_with_mistyped_source_tag_shows_only_metadata_conflict()
    {
        var rows = Enumerable.Range(170, 169).Select(n => new TrackInfo
        { DiscNumber = 2, TrackNumber = n == 213 ? 123 : n, SourceTrackNumber = n == 213 ? 123 : n,
            FileName = $"{n}_book_CD02.mp3" }).ToList();
        _service.Apply(rows);
        Assert.Single(rows, row => row.HasChapterWarning);
        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 123, 213), rows[43].ChapterWarningText);
        Assert.Equal(123, rows[43].TrackNumber);
        Assert.Equal(123, rows[43].SourceTrackNumber);
    }

    [Fact]
    public void Complete_filename_inventory_does_not_invent_gaps_from_a_mistyped_tag()
    {
        var rows = Enumerable.Range(170, 169).Select(n => new TrackInfo
        { DiscNumber = 2, TrackNumber = n == 213 ? 123 : n, FileName = $"{n}_book_CD02.mp3" }).ToList();
        _service.Apply(rows);
        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
        Assert.Equal(123, rows[43].TrackNumber);
        (rows[0], rows[1]) = (rows[1], rows[0]);
        _service.Apply(rows);
        Assert.True(rows[1].HasChapterWarning);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    public void Refresh_retires_missing_title_warnings_and_counts_without_hiding_real_warnings(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var missing = new TrackInfo { TrackNumber = 1, ChapterWarningText = "obsolete rendered title warning" };
            missing.SetWarning(TrackWarningPresentation.Format(TrackWarningCodes.MissingTagTitle), TrackWarningCodes.MissingTagTitle);
            var real = new TrackInfo { TrackNumber = 2, WarningCodes = TrackWarningCodes.MissingTagTitle |
                TrackWarningCodes.UnreadableTags | TrackWarningCodes.CheckOrder | TrackWarningCodes.InvalidAudio };
            var rows = new List<TrackInfo> { missing, real };

            Assert.True(_service.Apply(rows));
            Assert.Equal(TrackWarningCodes.None, missing.WarningCodes);
            Assert.False(missing.HasChapterWarning);
            Assert.False(missing.HasFileWarning);
            Assert.Equal(TrackWarningCodes.UnreadableTags | TrackWarningCodes.CheckOrder | TrackWarningCodes.InvalidAudio, real.WarningCodes);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), real.DisplayChapterWarning);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags), real.DisplayChapterWarning);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.InvalidAudio), real.DisplayFileWarning);
            var metrics = TrackWarningMetricsService.Create(rows.Select(t => new TrackWarningMetric(t.DisplayFileWarning, t.DisplayChapterWarning)));
            Assert.Equal(1, metrics.ChapterWarningCount);
            Assert.Equal(1, metrics.FileWarningCount);
            Assert.Same(missing, rows[0]);
            Assert.Same(real, rows[1]);
            Assert.False(_service.Apply(rows));
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Apply_ExtremeSourceNumberFollowedByLowerNumberPreservesOrderWarning()
    {
        var tracks = new List<TrackInfo>
        {
            new() { Index = 1, TrackNumber = int.MaxValue - 1 },
            new() { Index = 2, TrackNumber = int.MaxValue },
            new() { Index = 3, TrackNumber = 1 }
        };
        var originalOrder = tracks.ToArray();

        _service.Apply(tracks);

        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), tracks[2].ChapterWarningText);
        Assert.DoesNotContain(TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter), tracks[2].ChapterWarningText);
        Assert.Equal(originalOrder, tracks);
        Assert.Equal([1, 2, 3], tracks.Select(track => track.Index));
    }

    [Fact]
    public void Apply_MapsInvalidAudioToShortFileWarning()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Codec = "Ungültig", Warning = "Keine gültige Audiodatei erkannt." }
        };

        var changed = _service.Apply(tracks);

        Assert.True(changed);
        Assert.Equal("Keine gültige Audiodatei", tracks[0].FileWarningText);
        Assert.Equal("Keine gültige Audiodatei", tracks[0].DisplayFileWarning);
    }

    [Fact]
    public void Apply_AllowsFileAndChapterWarningOnSameTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Warning = "Quelldatei ist leer" },
            new() { TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.Equal("Quelldatei leer", tracks[0].FileWarningText);
        Assert.Equal("Kapitel doppelt", tracks[0].ChapterWarningText);
        Assert.Equal("Kapitel doppelt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_ReassignsTagMetadataMessagesToChapterWarnings()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Warning = "Kein Tag-Titel; Tags konnten nicht gelesen werden" }
        };

        _service.Apply(tracks);

        Assert.Equal(string.Empty, tracks[0].FileWarningText);
        Assert.Equal("Tags konnten nicht gelesen werden", tracks[0].ChapterWarningText);
        Assert.Equal(string.Empty, tracks[0].DisplayHintWarning);
    }

    [Fact]
    public void Apply_PreservesMultipleChapterWarningsOnOneTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1 },
            new() { TrackNumber = 3, Warning = "Doppelter Kapitelname" }
        };

        _service.Apply(tracks);

        Assert.Equal("Doppelter Kapitelname · Kapitel fehlt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksFirstTrackAfterMissingTrackNumber()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Mitte" },
            new() { TrackNumber = 4, ChapterTitle = "003 Ende" },
            new() { TrackNumber = 5, ChapterTitle = "004 Schluss" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "Kapitel fehlt", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksBackwardTrackOrderForReview()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 3, ChapterTitle = "001 Ende" },
            new() { TrackNumber = 2, ChapterTitle = "002 Mitte" }
        };

        _service.Apply(tracks);

        Assert.Equal("Sortierung prüfen", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_IgnoresExcludedTracksForChapterSequence()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Ausgeschlossen", IsExcluded = true },
            new() { TrackNumber = 3, ChapterTitle = "003 Ende" }
        };

        _service.Apply(tracks);

        Assert.Equal("", tracks[0].ChapterWarningText);
        Assert.Equal("", tracks[1].ChapterWarningText);
        Assert.Equal("", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksMissingChapterWhenGapIsNotOnlyExcludedTracks()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Ausgeschlossen", IsExcluded = true },
            new() { TrackNumber = 4, ChapterTitle = "003 Ende" }
        };

        _service.Apply(tracks);

        Assert.Equal("Kapitel fehlt", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksPossibleMissingDiscForInternalDiscGap()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 4, TrackNumber = 1, ChapterTitle = "CD 4 Track 1", AutomaticOrderPosition = 3 }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "Disc 3 fehlt möglicherweise" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkDiscInventoryGapWhenDiscSequenceIsComplete()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1 },
            new() { DiscNumber = 2, TrackNumber = 1 },
            new() { DiscNumber = 3, TrackNumber = 1 },
            new() { DiscNumber = 4, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkMissingEarlierDiscsWhenPartialDiscSetStartsAtThree()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 3, TrackNumber = 1 },
            new() { DiscNumber = 4, TrackNumber = 1 },
            new() { DiscNumber = 5, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksPossibleMissingDiscRangeForMultipleInternalDiscGaps()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1 },
            new() { DiscNumber = 2, TrackNumber = 1 },
            new() { DiscNumber = 5, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.Equal("Disc 3-4 fehlen möglicherweise", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_DoesNotTreatNonConsecutiveSourceNumbersAsMissingChapters()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 10 },
            new() { TrackNumber = 20 },
            new() { TrackNumber = 30 },
            new() { TrackNumber = 40 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkInventoryGapForExcludedExistingTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1 },
            new() { TrackNumber = 2 },
            new() { TrackNumber = 3, IsExcluded = true },
            new() { TrackNumber = 4 },
            new() { TrackNumber = 5 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_CurrentOrderWarningsRemainWhenAllSourceTracksExistButOrderIsWrong()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, AutomaticOrderPosition = 1 },
            new() { TrackNumber = 5, AutomaticOrderPosition = 5 },
            new() { TrackNumber = 2, AutomaticOrderPosition = 2 },
            new() { TrackNumber = 4, AutomaticOrderPosition = 4 },
            new() { TrackNumber = 3, AutomaticOrderPosition = 3 }
        };

        _service.Apply(tracks);

        Assert.Contains(tracks, track => track.ChapterWarningText == "Sortierung prüfen");
        Assert.DoesNotContain(tracks, track => track.ChapterWarningText.Contains("möglicherweise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_UsesTagTitleNumberWhenTrackNumberIsMissing()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TagTitle = "001 Anfang", ChapterTitle = "001 Vorschlag" },
            new() { TagTitle = "002 Mitte", ChapterTitle = "002 Vorschlag" },
            new() { TagTitle = "003 Ende", ChapterTitle = "003 Vorschlag" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_UsesFileNameNumberWhenTrackNumberAndTagTitleAreMissing()
    {
        var tracks = new List<TrackInfo>
        {
            new() { FileName = "001_Anfang.mp3", ChapterTitle = "001 Vorschlag" },
            new() { FileName = "003_Ende.mp3", ChapterTitle = "002 Vorschlag" }
        };

        _service.Apply(tracks);

        Assert.Equal("Kapitel fehlt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_DoesNotWarnWhenNoSequenceNumberCanBeRead()
    {
        var tracks = new List<TrackInfo>
        {
            new() { ChapterTitle = "Prolog" }
        };

        _service.Apply(tracks);

        Assert.Equal("", tracks[0].ChapterWarningText);
        Assert.Equal("", tracks[0].DisplayChapterWarning);
    }

    [Fact]
    public void Apply_ReturnsFalseWhenWarningsAreAlreadyCurrent()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                TrackNumber = 1,
                Warning = "Quelldatei ist leer",
                FileWarningText = "Quelldatei leer"
            }
        };

        var changed = _service.Apply(tracks);

        Assert.False(changed);
    }


    [Fact]
    public void Apply_DoesNotMarkSameTrackNumbersAcrossDifferentDiscsAsDuplicates()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 2, ChapterTitle = "CD 1 Track 2" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksDuplicateTrackNumbersWithinSameDisc()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1 erneut" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2 erneut" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "Kapitel doppelt", "Kapitel doppelt", "Kapitel doppelt", "Kapitel doppelt" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_RestartsSequenceCheckForEachDisc()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 3, ChapterTitle = "CD 1 Track 3" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "Kapitel fehlt", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_UsesExcludedTracksOnlyWithinSameDiscForMissingChapterCheck()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 2, ChapterTitle = "CD 1 Track 2 ausgeschlossen", IsExcluded = true },
            new() { DiscNumber = 1, TrackNumber = 3, ChapterTitle = "CD 1 Track 3" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 3, ChapterTitle = "CD 2 Track 3" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "", "", "Kapitel fehlt" }, tracks.Select(track => track.ChapterWarningText));
    }

}
