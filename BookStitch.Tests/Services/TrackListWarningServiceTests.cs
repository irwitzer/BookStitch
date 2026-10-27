using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackListWarningServiceTests
{
    private readonly TrackListWarningService _service = new();

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
