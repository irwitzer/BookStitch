using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterModeEngineServiceTests
{
    private readonly ChapterModeEngineService _service = new();

    [Fact]
    public void Apply_AutomatedToUserGeneratedToAutomatedRestoresFreshV4Result()
    {
        var track = Track("Quelltitel");

        var automated = _service.Apply(Request(track, ChapterSettingsService.ModeAuto));
        var custom = _service.Apply(Request(
            track,
            ChapterSettingsService.ModeCustom,
            settings =>
            {
                settings.Slot1 = "BookTitle";
                settings.Slot2 = "Empty";
                settings.Slot3 = "Empty";
            }));
        var automatedAgain = _service.Apply(Request(track, ChapterSettingsService.ModeAuto));

        Assert.Equal("Aktueller Buchtitel", Assert.Single(automated.RenderedTimeline).Title);
        Assert.Equal("Aktueller Buchtitel", Assert.Single(custom.RenderedTimeline).Title);
        Assert.Equal("Aktueller Buchtitel", Assert.Single(automatedAgain.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_AutomatedToNoChaptersToAutomatedClearsAndRestoresPresentation()
    {
        var track = Track("Quelltitel");

        _service.Apply(Request(track, ChapterSettingsService.ModeAuto));
        var none = _service.Apply(Request(track, ChapterSettingsService.ModeNone));

        Assert.Empty(none.RenderedTimeline);
        Assert.Equal(string.Empty, track.ChapterTitle);
        Assert.DoesNotContain(
            "[CHAPTER]",
            new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(none.RenderedTimeline),
            StringComparison.Ordinal);

        var automatedAgain = _service.Apply(Request(track, ChapterSettingsService.ModeAuto));
        Assert.Equal("Aktueller Buchtitel", Assert.Single(automatedAgain.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_ChapterMasterStartsIdenticalButUsesDedicatedModeResult()
    {
        var automatedTrack = Track("Quelltitel");
        var masterTrack = Track("Quelltitel");

        var automated = _service.Apply(Request(automatedTrack, ChapterSettingsService.ModeAuto));
        var master = _service.Apply(Request(masterTrack, ChapterSettingsService.ModeChapterMaster));

        Assert.Equal(ChapterSettingsService.ModeAuto, automated.Mode);
        Assert.Equal(ChapterSettingsService.ModeChapterMaster, master.Mode);
        Assert.Equal(
            automated.RenderedTimeline.Select(chapter => chapter.Title),
            master.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.NotSame(automated.EngineResult, master.EngineResult);

        _service.ResetChapterMasterToAutomatedBaseline();
        var resetMaster = _service.Apply(Request(masterTrack, ChapterSettingsService.ModeChapterMaster));
        Assert.Equal("Aktueller Buchtitel", Assert.Single(resetMaster.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_OriginalUsesDedicatedTimelineWithoutAutoEngineResult()
    {
        var track = Track("Quelltitel");
        track.EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo
            {
                Title = "  Original bleibt original  ",
                StartTicks = 0,
                EndTicks = TimeSpan.FromSeconds(1).Ticks
            }
        ];

        var result = _service.Apply(Request(
            track,
            ChapterSettingsService.ModeOriginal,
            settings => settings.NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString(),
            canonicalTitle: "Neuer Titel"));

        Assert.Equal(ChapterSettingsService.ModeOriginal, result.Mode);
        Assert.Null(result.EngineResult);
        Assert.Equal("  Original bleibt original  ", Assert.Single(result.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_ModeSwitchingDoesNotLeakOriginalCustomOrNoneState()
    {
        var track = Track("Quelltitel");
        track.EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo { Title = "Original", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks }
        ];

        var original = _service.Apply(Request(track, ChapterSettingsService.ModeOriginal));
        var custom = _service.Apply(Request(
            track,
            ChapterSettingsService.ModeCustom,
            settings =>
            {
                settings.Slot1 = "BookTitle";
                settings.Slot2 = "Empty";
                settings.Slot3 = "Empty";
            },
            canonicalTitle: "Benutzer"));
        var none = _service.Apply(Request(track, ChapterSettingsService.ModeNone));
        var automated = _service.Apply(Request(track, ChapterSettingsService.ModeAuto, canonicalTitle: "Auto"));

        Assert.Equal("Original", Assert.Single(original.RenderedTimeline).Title);
        Assert.Equal("Benutzer", Assert.Single(custom.RenderedTimeline).Title);
        Assert.Empty(none.RenderedTimeline);
        Assert.Equal("Auto", Assert.Single(automated.RenderedTimeline).Title);
    }

    [Theory]
    [InlineData(ChapterSettingsService.ModeAuto, "Neuer Titel")]
    [InlineData(ChapterSettingsService.ModeChapterMaster, "Neuer Titel")]
    [InlineData(ChapterSettingsService.ModeCustom, "Neuer Titel")]
    [InlineData(ChapterSettingsService.ModeOriginal, "Original")]
    [InlineData(ChapterSettingsService.ModeNone, null)]
    public void Apply_CanonicalTitleChangeUsesCurrentMode(string mode, string? expected)
    {
        var track = Track("Track 01");
        track.EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo { Title = "Original", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks }
        ];
        var result = _service.Apply(Request(
            track,
            mode,
            settings =>
            {
                settings.Slot1 = "BookTitle";
                settings.Slot2 = "Empty";
                settings.Slot3 = "Empty";
            },
            canonicalTitle: "Neuer Titel"));

        if (expected is null)
            Assert.Empty(result.RenderedTimeline);
        else
            Assert.Equal(expected, Assert.Single(result.RenderedTimeline).Title);
    }

    private static ChapterAutoEngineRequest Request(
        TrackInfo track,
        string mode,
        Action<ChapterSettings>? configure = null,
        string canonicalTitle = "Aktueller Buchtitel")
    {
        var settings = new ChapterSettings
        {
            Mode = mode,
            NumberingStyle = "Plain"
        };
        configure?.Invoke(settings);
        return new ChapterAutoEngineRequest(
            [track],
            ProjectManifestTypes.FolderProject,
            "mode-test",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(
                1,
                canonicalTitle,
                MetadataOrigin.SourceAggregation,
                null,
                MetadataOrigin.None),
            settings,
            LegacyUseLeadingZeros: false);
    }

    private static TrackInfo Track(string sourceTitle) => new()
    {
        FileName = "source.mp3",
        FilePath = "source.mp3",
        TagTitle = sourceTitle,
        SourceMetadataFields = new SourceMetadataCanonicalFields { Title = sourceTitle },
        DurationTicks = TimeSpan.FromSeconds(1).Ticks
    };
}
