using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterTimelinePresentationServiceTests
{
    private readonly ChapterTimelinePresentationService _service = new();

    [Fact]
    public void Apply_ProjectsGlobalChapterTitlesAndStartsForMixedEffectiveChapters()
    {
        var tracks = new List<TrackInfo>
        {
            EmbeddedTrack("Apokryphen", 52, 52, expanded: true),
            new()
            {
                ChapterTitle = "053 Intro",
                DurationTicks = TimeSpan.FromSeconds(90).Ticks
            },
            EmbeddedTrack("001 NSA - Nationales Sicherheits-Amt", 224, 224, expanded: true)
        };

        ApplyResolved(
            tracks,
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString() },
            legacyUseLeadingZeros: true,
            bookTitle: "",
            album: "");

        Assert.Equal("52 eingebettete Kapitel", tracks[0].DisplayChapterProposal);
        Assert.Equal(string.Empty, tracks[0].DisplayChapterStart);
        Assert.Equal("001 Apokryphen 1", tracks[0].EmbeddedChapterRows[0].ChapterProposal);
        Assert.Equal("052 Apokryphen 52", tracks[0].EmbeddedChapterRows[^1].ChapterProposal);
        Assert.Equal("00:00:00", tracks[0].EmbeddedChapterRows[0].TimelineStart);

        Assert.Equal("053 Intro", tracks[1].DisplayChapterProposal);
        Assert.Equal("00:00:52", tracks[1].DisplayChapterStart);

        Assert.Equal("054 NSA - Nationales Sicherheits-Amt 1", tracks[2].EmbeddedChapterRows[0].ChapterProposal);
        Assert.Equal("00:02:22", tracks[2].EmbeddedChapterRows[0].TimelineStart);
    }

    [Fact]
    public void Apply_CustomSettingsRenderEmbeddedChapters()
    {
        var tracks = new List<TrackInfo>
        {
            EmbeddedTrack("Quelle", 2, 2, expanded: true)
        };

        ApplyResolved(
            tracks,
            new ChapterSettings
            {
                Mode = "Custom",
                NumberingStyle = "Plain",
                Slot1 = "Number",
                Separator1 = "Dash",
                Slot2 = "OriginalTagTitle",
                Slot3 = "Empty"
            },
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.Equal("1 - Quelle 1", tracks[0].EmbeddedChapterRows[0].ChapterProposal);
        Assert.Equal("2 - Quelle 2", tracks[0].EmbeddedChapterRows[1].ChapterProposal);
    }

    [Fact]
    public void Apply_SingleEmbeddedChapterDisplaysFinalEmbeddedChapterWithoutExpander()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                ChapterTitle = "Parent title should not win",
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Embedded only",
                        StartTicks = TimeSpan.FromSeconds(2).Ticks,
                        EndTicks = TimeSpan.FromSeconds(8).Ticks
                    }
                ]
            }
        };

        ApplyResolved(
            tracks,
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.False(tracks[0].HasEmbeddedChapterDetails);
        Assert.Equal("1 Embedded only", tracks[0].DisplayChapterProposal);
        Assert.Equal("00:00:02", tracks[0].DisplayChapterStart);
    }

    [Fact]
    public void Apply_UsesNormalizedEmbeddedChapterIdentityForChildPresentation()
    {
        var track = new TrackInfo
        {
            DurationTicks = TimeSpan.FromSeconds(10).Ticks,
            IsChapterDetailsExpanded = true,
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo
                {
                    Title = "NegativeStart",
                    StartTicks = -TimeSpan.FromSeconds(2).Ticks,
                    EndTicks = TimeSpan.FromSeconds(2).Ticks
                },
                new TrackEmbeddedChapterInfo
                {
                    Title = "ClampedEnd",
                    StartTicks = TimeSpan.FromSeconds(8).Ticks,
                    EndTicks = TimeSpan.FromSeconds(12).Ticks
                }
            ]
        };
        _ = track.EmbeddedChapterRows;

        ApplyResolved(
            [track],
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.Equal("1 NegativeStart", track.EmbeddedChapterRows[0].ChapterProposal);
        Assert.Equal("00:00:00", track.EmbeddedChapterRows[0].TimelineStart);
        Assert.Equal("2 ClampedEnd", track.EmbeddedChapterRows[1].ChapterProposal);
        Assert.Equal("00:00:08", track.EmbeddedChapterRows[1].TimelineStart);
    }

    [Fact]
    public void Apply_CustomBookAndAlbumTitlesMatchExportMetadataAcrossMixedTimeline()
    {
        var tracks = new List<TrackInfo>
        {
            EmbeddedTrack("Embedded", 4, 2, expanded: true),
            new()
            {
                TagTitle = "Plain",
                DurationTicks = TimeSpan.FromSeconds(3).Ticks
            },
            new()
            {
                ChapterTitle = "Single parent",
                DurationTicks = TimeSpan.FromSeconds(5).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Single embedded",
                        StartTicks = TimeSpan.FromSeconds(1).Ticks,
                        EndTicks = TimeSpan.FromSeconds(4).Ticks
                    }
                ]
            }
        };
        var settings = new ChapterSettings
        {
            Mode = "Custom",
            NumberingStyle = "ZeroPadded",
            Slot1 = "BookTitle",
            Separator1 = "Dash",
            Slot2 = "Album",
            Separator2 = "Dash",
            Slot3 = "Number"
        };
        const string bookTitle = "Book Context";
        const string album = "Album Context";

        var result = ApplyResolved(tracks, settings, legacyUseLeadingZeros: true, bookTitle, album);
        var metadata = new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(result.RenderedTimeline);

        var uiTitles = new[]
        {
            tracks[0].EmbeddedChapterRows[0].ChapterProposal,
            tracks[0].EmbeddedChapterRows[1].ChapterProposal,
            tracks[1].DisplayChapterProposal,
            tracks[2].DisplayChapterProposal
        };

        Assert.Equal(ExtractChapterTitles(metadata), uiTitles);
    }

    [Fact]
    public void Apply_ExcludedParentRemovesChildrenFromEffectiveTimeline()
    {
        var excluded = EmbeddedTrack("Excluded", 2, 2, expanded: true);
        excluded.IsExcluded = true;
        var tracks = new List<TrackInfo>
        {
            excluded,
            new()
            {
                ChapterTitle = "1 Next",
                DurationTicks = TimeSpan.FromSeconds(1).Ticks
            }
        };

        ApplyResolved(
            tracks,
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.All(excluded.EmbeddedChapterRows, row =>
        {
            Assert.Equal(string.Empty, row.ChapterProposal);
            Assert.Equal(string.Empty, row.TimelineStart);
        });
        Assert.Equal("1 Next", tracks[1].DisplayChapterProposal);
        Assert.Equal("00:00:00", tracks[1].DisplayChapterStart);
    }

    [Fact]
    public void Apply_RecomputesAfterSorting()
    {
        var first = new TrackInfo { ChapterTitle = "001 First", TagTitle = "First", DurationTicks = TimeSpan.FromSeconds(5).Ticks };
        var second = new TrackInfo { ChapterTitle = "002 Second", TagTitle = "Second", DurationTicks = TimeSpan.FromSeconds(7).Ticks };
        var tracks = new List<TrackInfo> { second, first };
        var settings = new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() };

        ApplyResolved(
            tracks,
            settings,
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.Equal("1 Second", tracks[0].DisplayChapterProposal);
        Assert.Equal("00:00:00", tracks[0].DisplayChapterStart);
        Assert.Equal("2 First", tracks[1].DisplayChapterProposal);
        Assert.Equal("00:00:07", tracks[1].DisplayChapterStart);
    }

    [Fact]
    public void Apply_RaisesPresentationChangesAfterMoveExcludeRestoreAndSettingsChange()
    {
        var first = new TrackInfo { ChapterTitle = "001 First", TagTitle = "First", DurationTicks = TimeSpan.FromSeconds(5).Ticks };
        var second = new TrackInfo { ChapterTitle = "002 Second", TagTitle = "Second", DurationTicks = TimeSpan.FromSeconds(7).Ticks };
        var tracks = new List<TrackInfo> { first, second };
        var firstChanges = new List<string?>();
        var secondChanges = new List<string?>();
        first.PropertyChanged += (_, args) => firstChanges.Add(args.PropertyName);
        second.PropertyChanged += (_, args) => secondChanges.Add(args.PropertyName);
        var plain = new ChapterSettings { Mode = "Auto", NumberingStyle = ChapterNumberingStyle.Plain.ToString() };
        var padded = new ChapterSettings { Mode = "Auto", NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString() };

        ApplyResolved(tracks, plain, legacyUseLeadingZeros: false, bookTitle: "", album: "");
        firstChanges.Clear();
        secondChanges.Clear();

        tracks = [second, first];
        ApplyResolved(tracks, plain, legacyUseLeadingZeros: false, bookTitle: "", album: "");

        Assert.Equal("00:00:00", second.DisplayChapterStart);
        Assert.Equal("00:00:07", first.DisplayChapterStart);
        Assert.Contains(nameof(TrackInfo.DisplayChapterStart), firstChanges);
        Assert.Contains(nameof(TrackInfo.DisplayChapterStart), secondChanges);

        firstChanges.Clear();
        secondChanges.Clear();
        new TrackListActionService().ExcludeSelected([second]);
        ApplyResolved(tracks, plain, legacyUseLeadingZeros: false, bookTitle: "", album: "");

        Assert.Equal(string.Empty, second.DisplayChapterProposal);
        Assert.Equal(string.Empty, second.DisplayChapterStart);
        Assert.Equal("1 First", first.DisplayChapterProposal);
        Assert.Equal("00:00:00", first.DisplayChapterStart);
        Assert.Contains(nameof(TrackInfo.DisplayChapterProposal), secondChanges);
        Assert.Contains(nameof(TrackInfo.DisplayChapterStart), secondChanges);

        firstChanges.Clear();
        secondChanges.Clear();
        new TrackListActionService().RestoreSelected([second]);
        ApplyResolved(tracks, padded, legacyUseLeadingZeros: true, bookTitle: "", album: "");

        Assert.Equal("1 Second", second.DisplayChapterProposal);
        Assert.Equal("2 First", first.DisplayChapterProposal);
        Assert.Contains(nameof(TrackInfo.DisplayChapterProposal), firstChanges);
        Assert.Contains(nameof(TrackInfo.DisplayChapterProposal), secondChanges);
    }

    [Fact]
    public void Apply_NumberingStyleChangeUpdatesDisplayedChapterProposals()
    {
        var tracks = Enumerable.Range(1, 10)
            .Select(index => new TrackInfo
            {
                ChapterTitle = $"{index:000} Track {index}",
                TagTitle = $"Track {index}",
                DurationTicks = TimeSpan.FromSeconds(1).Ticks
            })
            .ToList();
        var changes = new List<string?>();
        tracks[0].PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var plain = new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() };
        var padded = new ChapterSettings { NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString() };

        ApplyResolved(tracks, plain, legacyUseLeadingZeros: false, bookTitle: "", album: "");
        changes.Clear();

        ApplyResolved(tracks, padded, legacyUseLeadingZeros: true, bookTitle: "", album: "");

        Assert.Equal("01 Track 1", tracks[0].DisplayChapterProposal);
        Assert.Contains(nameof(TrackInfo.DisplayChapterProposal), changes);
    }

    [Fact]
    public void Apply_FormatsTimelineStartsBeyondTwentyFourHoursWithTotalHours()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                ChapterTitle = "Long first",
                DurationTicks = new TimeSpan(27, 15, 42).Ticks
            },
            new()
            {
                ChapterTitle = "After day",
                DurationTicks = TimeSpan.FromSeconds(1).Ticks
            }
        };

        ApplyResolved(
            tracks,
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            legacyUseLeadingZeros: false,
            bookTitle: "",
            album: "");

        Assert.Equal("27:15:42", tracks[1].DisplayChapterStart);
    }

    private static TrackInfo EmbeddedTrack(string prefix, int durationSeconds, int chapterCount, bool expanded = false)
    {
        var chapterDuration = TimeSpan.FromSeconds(durationSeconds / (double)chapterCount);
        return new TrackInfo
        {
            ChapterTitle = prefix,
            DurationTicks = TimeSpan.FromSeconds(durationSeconds).Ticks,
            IsChapterDetailsExpanded = expanded,
            EmbeddedChapters = Enumerable.Range(0, chapterCount)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"{prefix} {index + 1}",
                    StartTicks = (long)Math.Round(chapterDuration.Ticks * (double)index),
                    EndTicks = (long)Math.Round(chapterDuration.Ticks * (double)(index + 1))
                })
                .ToList()
        };
    }

    private ActiveChapterResult ApplyResolved(
        IList<TrackInfo> tracks,
        ChapterSettings settings,
        bool legacyUseLeadingZeros,
        string? bookTitle,
        string? album)
    {
        ActiveChapterResult result;
        if (string.Equals(settings.Mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal))
        {
            result = new ChapterCustomEngineService().Apply(new ChapterAutoEngineRequest(
                tracks,
                ProjectManifestTypes.FolderProject,
                "presentation-test",
                string.Empty,
                new ChapterCanonicalMetadataSnapshot(
                    1,
                    bookTitle ?? string.Empty,
                    MetadataOrigin.User,
                    album ?? string.Empty,
                    MetadataOrigin.User),
                settings,
                legacyUseLeadingZeros));
        }
        else
        {
            var normalized = ChapterSettingsService.Normalize(settings, legacyUseLeadingZeros);
            var numberingStyle = ChapterSettingsService.GetNumberingStyle(normalized, legacyUseLeadingZeros);
            var renderer = new ChapterTitleRenderer();
            var timeline = new ChapterTimelineService()
                .BuildTimeline(tracks.Where(track => !track.IsExcluded).ToArray());
            var renderedTimeline = timeline
                .Select(chapter =>
                {
                    var titleContent = renderer.TryGetTitleWithoutNumber(chapter.Title, out var titleWithoutNumber)
                        ? titleWithoutNumber
                        : chapter.Title;
                    return chapter with
                    {
                        Title = renderer.RenderContent(
                            chapter.OutputChapterIndex,
                            timeline.Count,
                            titleContent,
                            numberingStyle)
                    };
                })
                .ToArray();
            result = new ActiveChapterResult(normalized.Mode, renderedTimeline);
        }

        _service.ApplyResolvedTimeline(tracks.ToArray(), result.RenderedTimeline);
        return result;
    }

    private static List<string> ExtractChapterTitles(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("title=", StringComparison.Ordinal))
            .Select(line => line["title=".Length..])
            .ToList();
    }
}
