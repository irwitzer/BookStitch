using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCustomEngineServiceTests
{
    private readonly ChapterCustomEngineService _service = new();

    [Theory]
    [InlineData(true, "Buchtitel")]
    [InlineData(false, "Eigenes Album")]
    public void Apply_AlbumSlotRespectsTitleBindingWithoutChangingRawAutomaticCandidates(bool linked, string expected)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Buchtitel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Eigenes Album", MetadataOrigin.User);
        var captured = ChapterCanonicalMetadataSnapshot.Capture(metadata,
            new Dictionary<string, bool> { [ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = linked });
        var request = Request([new TrackInfo { DurationTicks = TimeSpan.TicksPerSecond }], settings =>
        {
            settings.Slot1 = "Album"; settings.Slot2 = "Empty"; settings.Slot3 = "Empty";
        }) with { CanonicalMetadata = captured };
        Assert.Equal(expected, Assert.Single(_service.Apply(request).RenderedTimeline).Title);
        Assert.Equal("Eigenes Album", captured.AlbumValue);
        Assert.Equal("Eigenes Album", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Apply_RendersSyntheticCustomTimelineAndUpdatesActiveTrackTitles()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                FileName = "001_Intro.mp3",
                TagTitle = "Intro",
                DurationTicks = TimeSpan.FromSeconds(2).Ticks
            }
        };

        var result = _service.Apply(Request(
            tracks,
            settings =>
            {
                settings.NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString();
                settings.Slot1 = "BookTitle";
                settings.Separator1 = "Dash";
                settings.Slot2 = "Number";
                settings.Separator2 = "Dash";
                settings.Slot3 = "OriginalTagTitle";
            },
            bookTitle: "Billy Summers"));

        var chapter = Assert.Single(result.RenderedTimeline);
        Assert.Equal(ChapterSettingsService.ModeCustom, result.Mode);
        Assert.Null(result.EngineResult);
        Assert.Equal("Billy Summers - 1 - Intro", chapter.Title);
        Assert.Equal("Billy Summers - 1 - Intro", tracks[0].ChapterTitle);
    }

    [Fact]
    public void Apply_RendersEmbeddedCustomTimelineWithExportContextSemantics()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                DurationTicks = TimeSpan.FromSeconds(2).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Embedded One",
                        StartTicks = 0,
                        EndTicks = TimeSpan.FromSeconds(1).Ticks
                    },
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Embedded Two",
                        StartTicks = TimeSpan.FromSeconds(1).Ticks,
                        EndTicks = TimeSpan.FromSeconds(2).Ticks
                    }
                ]
            }
        };

        var result = _service.Apply(Request(
            tracks,
            settings =>
            {
                settings.Slot1 = "Number";
                settings.Separator1 = "Dash";
                settings.Slot2 = "OriginalTagTitle";
                settings.Slot3 = "Empty";
            }));

        Assert.Equal(["1 - Embedded One", "2 - Embedded Two"], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal("1 - Embedded One", tracks[0].ChapterTitle);
    }

    [Fact]
    public void Apply_CustomOriginalTagTitlePreservesRightSequentialSuffix()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                TagTitle = "Wenn du erzählst, erblüht die Wüste - 001",
                FileName = "001.m4a",
                DurationTicks = TimeSpan.FromSeconds(1).Ticks
            }
        };

        var result = _service.Apply(Request(
            tracks,
            settings =>
            {
                settings.Slot1 = "Number";
                settings.Separator1 = "Space";
                settings.Slot2 = "OriginalTagTitle";
                settings.Slot3 = "Empty";
            }));

        Assert.Equal(
            "1 Wenn du erzählst, erblüht die Wüste - 001",
            Assert.Single(result.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_UsesGlobalOutputPositionForSyntheticTrackAfterEmbeddedChapters()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                DurationTicks = TimeSpan.FromSeconds(2).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo { Title = "Quelle 1", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks },
                    new TrackEmbeddedChapterInfo { Title = "Quelle 2", StartTicks = TimeSpan.FromSeconds(1).Ticks, EndTicks = TimeSpan.FromSeconds(2).Ticks }
                ]
            },
            new()
            {
                TagTitle = "Anschluss",
                DurationTicks = TimeSpan.FromSeconds(1).Ticks
            }
        };

        var result = _service.Apply(Request(
            tracks,
            settings =>
            {
                settings.NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString();
                settings.Slot1 = "Number";
                settings.Separator1 = "Space";
                settings.Slot2 = "OriginalTagTitle";
                settings.Slot3 = "Empty";
            }));

        Assert.Equal("3 Anschluss", result.RenderedTimeline[2].Title);
        Assert.Equal("3 Anschluss", tracks[1].ChapterTitle);
    }

    private static ChapterAutoEngineRequest Request(
        IList<TrackInfo> tracks,
        Action<ChapterSettings> configure,
        string bookTitle = "",
        string album = "")
    {
        var settings = new ChapterSettings
        {
            Mode = ChapterSettingsService.ModeCustom,
            NumberingStyle = ChapterNumberingStyle.Plain.ToString()
        };
        configure(settings);
        return new ChapterAutoEngineRequest(
            tracks,
            ProjectManifestTypes.FolderProject,
            "custom-test",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(
                1,
                bookTitle,
                MetadataOrigin.User,
                album,
                MetadataOrigin.User),
            settings,
            LegacyUseLeadingZeros: false);
    }
}
