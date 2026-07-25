using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackListWarningServiceTests
{
    private readonly TrackListWarningService _service = new();

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
        Assert.Equal("⛔ Keine gültige Audiodatei", tracks[0].DisplayFileWarning);
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

    [Fact]
    public void TrackInfo_DisplayOutputSizeMb_UsesOneDecimalWithUnitOrDash()
    {
        var track = new TrackInfo { ConvertedSizeAvailable = true, ConvertedSizeMb = 12.44 };
        var missing = new TrackInfo();

        Assert.Equal("12.4 MB", track.DisplayOutputSizeMb);
        Assert.Equal("–", missing.DisplayOutputSizeMb);
    }
}
