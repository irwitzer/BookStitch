using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCustomTitleServiceTests
{
    [Theory]
    [InlineData(100, "CleanTagTitle", "2 Armageddon")]
    [InlineData(100, "CleanFileName", "2 Armageddon")]
    [InlineData(1000, "CleanTagTitle", "2 Armageddon")]
    [InlineData(1000, "CleanFileName", "2 Armageddon")]
    [InlineData(100, "OriginalTagTitle", "2 100 - Armageddon")]
    [InlineData(100, "OriginalFileName", "2 100_Armageddon")]
    public void Render_CleanSlotsRemoveSourceOrdinalEvenAfterReordering_OriginalSlotsPreserveIt(
        int sourceNumber, string slot, string expected)
    {
        var track = new TrackInfo
        {
            TagTitle = sourceNumber + " - Armageddon",
            FileName = sourceNumber + "_Armageddon.mp3",
            SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = sourceNumber }
        };
        var settings = new ChapterSettings
        {
            Mode = "Custom", NumberingStyle = "Plain", Slot1 = "Number", Slot2 = slot,
            Slot3 = "Empty", Separator1 = "Space", Separator2 = "Space"
        };
        Assert.Equal(expected, new ChapterCustomTitleService().Render(track, settings, 2, 1300, null, null));
    }

    private readonly ChapterCustomTitleService _service = new();

    [Fact]
    public void Render_UsesAllThreeSlots()
    {
        var settings = new ChapterSettings
        {
            NumberingStyle = "ZeroPadded",
            Slot1 = "BookTitle",
            Separator1 = "Dash",
            Slot2 = "Number",
            Separator2 = "Dash",
            Slot3 = "OriginalTagTitle"
        };

        var result = _service.Render(
            new TrackInfo { TagTitle = "Intro", FileName = "001_Intro.mp3" },
            settings,
            chapterPosition: 4,
            activeChapterCount: 120,
            bookTitle: "Billy Summers",
            album: "Album");

        Assert.Equal("Billy Summers - 004 - Intro", result);
    }

    [Fact]
    public void Render_OriginalTitleTagStaysUnchanged()
    {
        var settings = new ChapterSettings
        {
            NumberingStyle = "Plain",
            Slot1 = "OriginalTagTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { TagTitle = "001_Intro" },
            settings,
            1,
            10,
            null,
            null);

        Assert.Equal("001_Intro", result);
    }

    [Theory]
    [InlineData("Space", "Billy Intro")]
    [InlineData("Dash", "Billy - Intro")]
    [InlineData("EnDash", "Billy – Intro")]
    [InlineData("Dot", "Billy · Intro")]
    [InlineData("Colon", "Billy: Intro")]
    [InlineData("Pipe", "Billy | Intro")]
    [InlineData("None", "BillyIntro")]
    public void Render_SupportsSeparators(string separator, string expected)
    {
        var settings = new ChapterSettings
        {
            Slot1 = "BookTitle",
            Separator1 = separator,
            Slot2 = "CleanFileName",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { FileName = "001_Intro.mp3" },
            settings,
            1,
            10,
            "Billy",
            null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("05 Zaubermus und Marzipan.mp3.mp3", "Zaubermus und Marzipan")]
    [InlineData("06 Zaubermus und Marzipan.mp3.aac.aac", "Zaubermus und Marzipan")]
    [InlineData("07 Zaubermus und Marzipan.mp3.ogg.ogg", "Zaubermus und Marzipan")]
    public void Render_CleanFileName_RemovesEveryTrailingKnownAudioExtension(
        string fileName,
        string expected)
    {
        var settings = new ChapterSettings
        {
            Slot1 = "CleanFileName",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { FileName = fileName },
            settings,
            1,
            10,
            null,
            null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("05 Zaubermus und Marzipan.mp3.mp3", "Zaubermus und Marzipan")]
    [InlineData("06 Zaubermus und Marzipan.mp3.aac.aac", "Zaubermus und Marzipan")]
    [InlineData("07 Zaubermus und Marzipan.MP3.Ogg.M4A.AAC.mp3", "Zaubermus und Marzipan")]
    public void Render_CleanTagTitle_RemovesEveryTrailingKnownAudioExtension(
        string tagTitle,
        string expected)
    {
        var settings = new ChapterSettings
        {
            Slot1 = "CleanTagTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { TagTitle = tagTitle },
            settings,
            1,
            10,
            null,
            null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_OriginalTagTitle_PreservesTrailingAudioExtensionText()
    {
        const string original = "05 Zaubermus und Marzipan.mp3.aac.aac";
        var settings = new ChapterSettings
        {
            Slot1 = "OriginalTagTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { TagTitle = original },
            settings,
            1,
            10,
            null,
            null);

        Assert.Equal(original, result);
    }

    [Fact]
    public void Render_SkipsEmptySlotsWithoutOrphanSeparators()
    {
        var settings = new ChapterSettings
        {
            Slot1 = "BookTitle",
            Separator1 = "Dash",
            Slot2 = "Empty",
            Separator2 = "Dash",
            Slot3 = "CleanFileName"
        };

        var result = _service.Render(
            new TrackInfo { FileName = "001_Intro.mp3" },
            settings,
            1,
            10,
            "Billy",
            null);

        Assert.Equal("Billy - Intro", result);
    }

    [Theory]
    [InlineData(1, 9, "1 - Titel")]
    [InlineData(1, 10, "1\u2007 - Titel")]
    [InlineData(9, 10, "9\u2007 - Titel")]
    [InlineData(10, 10, "10 - Titel")]
    [InlineData(1, 100, "1\u2007\u2007 - Titel")]
    [InlineData(58, 100, "58\u2007 - Titel")]
    [InlineData(99, 100, "99\u2007 - Titel")]
    [InlineData(100, 100, "100 - Titel")]
    [InlineData(1, 1000, "1\u2007\u2007\u2007 - Titel")]
    [InlineData(984, 1000, "984\u2007 - Titel")]
    [InlineData(999, 1000, "999\u2007 - Titel")]
    [InlineData(1000, 1000, "1000 - Titel")]
    public void Render_NumberSlotPreservesSpacePaddedFigureSpacesBeforeSeparator(
        int position,
        int activeChapterCount,
        string expected)
    {
        var settings = new ChapterSettings
        {
            NumberingStyle = "SpacePadded",
            Slot1 = "Number",
            Separator1 = "Dash",
            Slot2 = "OriginalTagTitle",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo { TagTitle = "Titel" },
            settings,
            position,
            activeChapterCount,
            null,
            null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_NumberOnlyKeepsSpacePaddedFigureSpaces()
    {
        var settings = new ChapterSettings
        {
            NumberingStyle = "SpacePadded",
            Slot1 = "Number",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.Render(
            new TrackInfo(),
            settings,
            chapterPosition: 984,
            activeChapterCount: 1000,
            null,
            null);

        Assert.Equal("984\u2007", result);
    }
}
