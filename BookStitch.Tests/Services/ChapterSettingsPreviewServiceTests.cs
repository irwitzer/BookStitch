using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterSettingsPreviewServiceTests
{
    private readonly ChapterSettingsPreviewService _service = new();

    [Theory]
    [InlineData("Auto", 0)]
    [InlineData("Auto", 1)]
    [InlineData("Auto", 2)]
    [InlineData("Auto", 3)]
    [InlineData("Auto", 5)]
    [InlineData("Auto", 8)]
    [InlineData("Custom", 0)]
    [InlineData("Custom", 1)]
    [InlineData("Custom", 2)]
    [InlineData("Custom", 3)]
    [InlineData("Custom", 5)]
    [InlineData("Custom", 8)]
    public void GlobalPreviewShowsAtMostFiveExistingTracksWithoutChangingTheProject(string mode, int count)
    {
        var tracks = Enumerable.Range(1, count).Select(i => new TrackInfo
        {
            SourceId = Guid.NewGuid(), Index = i, FileName = $"{i}.mp3",
            TagTitle = $"Tag{i}", ChapterTitle = $"Content{i}", DurationTicks = TimeSpan.TicksPerSecond
        }).ToArray();
        var settings = new ChapterSettings
        {
            Mode = mode, PreviewDataSource = "CurrentProject", NumberingStyle = "Plain",
            Slot1 = "OriginalTagTitle", Slot2 = "Empty", Slot3 = "Empty"
        };
        var preview = _service.CreatePreview(settings, false, new ChapterPreviewData("", "", tracks), maxItems: 5);
        var expected = Enumerable.Range(1, Math.Min(5, count)).Select(i => mode == "Auto" ? $"{i} Content{i}" : $"Tag{i}");
        Assert.Equal(string.Join(Environment.NewLine, expected), preview);
        Assert.Equal(Enumerable.Range(1, count).Select(i => $"Content{i}"), tracks.Select(track => track.ChapterTitle));
        Assert.Equal(Enumerable.Range(1, count), tracks.Select(track => track.Index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void GlobalPreviewUsesOnlyAvailableCanonicalChapters(int count)
    {
        var project = EmbeddedProjectPreview(count, "Chapter");
        var settings = CurrentProjectSettings("Original");
        Assert.Equal(string.Join(Environment.NewLine, project.RenderedTimeline!.Take(5).Select(chapter => chapter.Title)),
            _service.CreatePreview(settings, false, project, maxItems: 5));
    }

    [Fact]
    public void GlobalPreviewSkipsExcludedTracksAndUsesOnlyExistingSampleData()
    {
        var settings = new ChapterSettings { Mode = "Auto", PreviewDataSource = "CurrentProject", NumberingStyle = "Plain" };
        var tracks = Enumerable.Range(1, 7).Select(i => new TrackInfo
        {
            Index = i, ChapterTitle = $"Content{i}", IsExcluded = i == 2, DurationTicks = TimeSpan.TicksPerSecond
        }).ToArray();
        var lines = _service.CreatePreview(settings, false, new ChapterPreviewData("", "", tracks), maxItems: 5).Split(Environment.NewLine);
        Assert.Equal(5, lines.Length);
        Assert.DoesNotContain(lines, line => line.Contains("Content2", StringComparison.Ordinal));
        Assert.EndsWith("Content6", lines.Last());
        settings.PreviewDataSource = "Sample";
        Assert.Equal(5, _service.CreatePreview(settings, false, null, maxItems: 5).Split(Environment.NewLine).Length);
        settings.Mode = "None";
        Assert.Empty(_service.CreatePreview(settings, false, null, maxItems: 5));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void GlobalOriginalPreviewLimitsEmbeddedChaptersWithoutChangingThem(int count)
    {
        var project = EmbeddedProjectPreview(count, "Original") with { RenderedTimeline = null };
        var before = project.Tracks[0].EmbeddedChapters.Select(chapter => chapter.Title).ToArray();
        var preview = _service.CreatePreview(CurrentProjectSettings("Original"), false, project, maxItems: 5);
        Assert.Equal(Math.Min(5, count), preview.Split(Environment.NewLine).Length);
        Assert.Equal(before, project.Tracks[0].EmbeddedChapters.Select(chapter => chapter.Title));
    }

    [Fact]
    public void CreatePreview_DefaultUsesSampleData()
    {
        var result = _service.CreatePreview(new ChapterSettings(), false, currentProjectData: null);

        Assert.Equal("001 Die Mühle im Koselbruch", result);
    }

    [Fact]
    public void CreatePreview_NoChaptersIsEmpty()
    {
        var settings = new ChapterSettings { Mode = ChapterSettingsService.ModeNone };

        Assert.Equal(string.Empty, _service.CreatePreview(settings, false, currentProjectData: null));
    }

    [Fact]
    public void CreatePreview_ChapterMasterInitiallyUsesAutomatedPresentation()
    {
        var settings = new ChapterSettings
        {
            Mode = ChapterSettingsService.ModeChapterMaster,
            NumberingStyle = "Plain"
        };

        Assert.Equal("1 Die Mühle im Koselbruch", _service.CreatePreview(settings, false, currentProjectData: null));
    }

    [Theory]
    [InlineData("Plain", "1 Die Mühle im Koselbruch")]
    [InlineData("ZeroPadded", "001 Die Mühle im Koselbruch")]
    [InlineData("SpacePadded", "1\u2007\u2007 Die Mühle im Koselbruch")]
    public void CreatePreview_SampleUsesVirtualThreeDigitChapterCount(string numberingStyle, string expected)
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "Sample",
            Mode = "Auto",
            NumberingStyle = numberingStyle
        };

        var result = _service.CreatePreview(settings, false, currentProjectData: null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CreatePreview_CurrentProjectUsesRealProjectDataUnchangedForOriginalTag()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Custom",
            NumberingStyle = "Plain",
            Slot1 = "OriginalTagTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };
        var project = new ChapterPreviewData(
            "",
            "",
            [new TrackInfo { FileName = "001 Projekt.mp3", TagTitle = "Track 1" }]);

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("Track 1", result);
    }

    [Fact]
    public void CreatePreview_CurrentProjectFallsBackToSampleWhenNoProjectIsLoaded()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Custom",
            NumberingStyle = "Plain",
            Slot1 = "BookTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.CreatePreview(settings, false, currentProjectData: null);

        Assert.Equal(LocalizationText.Get("Settings.ChapterSample.BookTitle"), result);
    }

    [Fact]
    public void CreatePreview_CurrentProjectUsesSameDynamicNumberWidthAsChapterApplication()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Auto",
            NumberingStyle = "ZeroPadded"
        };
        var project = new ChapterPreviewData(
            "",
            "",
            Enumerable.Range(1, 39)
                .Select(index => new TrackInfo
                {
                    FileName = $"{index:00} Track.mp3",
                    TagTitle = $"Track {index}",
                    ChapterTitle = "Kapitel"
                })
                .ToList());

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("01 Kapitel", result);
    }

    [Fact]
    public void CreatePreview_CurrentProjectKeepsEmbeddedChapterInformationAndUsesSyntheticExampleTrack()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Auto",
            NumberingStyle = "Plain"
        };
        var project = new ChapterPreviewData(
            "",
            "",
            [
                new TrackInfo
                {
                    ChapterTitle = "Container parent",
                    TagTitle = "Container",
                    FileName = "001_container.m4b",
                    DurationTicks = TimeSpan.FromSeconds(2).Ticks,
                    EmbeddedChapters =
                    [
                        new TrackEmbeddedChapterInfo { Title = "Quelle 1", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks },
                        new TrackEmbeddedChapterInfo { Title = "Quelle 2", StartTicks = TimeSpan.FromSeconds(1).Ticks, EndTicks = TimeSpan.FromSeconds(2).Ticks }
                    ]
                },
                new TrackInfo
                {
                    TagTitle = "003 Anschluss",
                    ChapterTitle = "Anschluss",
                    FileName = "003_real_track.mp3",
                    DurationTicks = TimeSpan.FromSeconds(1).Ticks
                }
            ]);

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("3 Anschluss", result);
    }

    [Fact]
    public void CreatePreview_CurrentProjectUsesEffectiveChapterCountForNumberWidth()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Auto",
            NumberingStyle = "ZeroPadded"
        };
        var project = new ChapterPreviewData(
            "",
            "",
            [
                new TrackInfo
                {
                    DurationTicks = TimeSpan.FromSeconds(100).Ticks,
                    EmbeddedChapters = Enumerable.Range(0, 100)
                        .Select(index => new TrackEmbeddedChapterInfo
                        {
                            Title = $"Quelle {index + 1}",
                            StartTicks = TimeSpan.FromSeconds(index).Ticks,
                            EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                        })
                        .ToList()
                },
                new TrackInfo
                {
                    TagTitle = "Anschluss",
                    ChapterTitle = "Anschluss",
                    DurationTicks = TimeSpan.FromSeconds(1).Ticks
                }
            ]);

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("101 Anschluss", result);
    }

    [Theory]
    [InlineData(52)]
    [InlineData(224)]
    public void CreatePreview_CurrentProjectUsesFirstEffectiveRenderedChapterForEmbeddedContainer(int chapterCount)
    {
        var settings = CurrentProjectSettings(ChapterSettingsService.ModeAuto);
        var project = EmbeddedProjectPreview(chapterCount, "NSA");

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("1 NSA 001", result);
        Assert.False(result.StartsWith(chapterCount.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void CreatePreview_ReorderedProjectUsesNumberAndTextFromTheSameCurrentRenderedTimeline()
    {
        var settings = CurrentProjectSettings(ChapterSettingsService.ModeAuto);
        var intro = new TrackInfo { SourceId = Guid.NewGuid(), ChapterTitle = "stale source title" };
        var firstState = new ChapterPreviewData(
            "",
            "",
            [intro],
            RenderedTimeline:
            [
                new EffectiveChapterInfo(1, intro, null, "1 Intro", 0, 1, 0, 1, true)
            ]);
        var secondState = EmbeddedProjectPreview(224, "NSA");

        Assert.Equal("1 Intro", _service.CreatePreview(settings, false, firstState));
        Assert.Equal("1 NSA 001", _service.CreatePreview(settings, false, secondState));
    }

    [Theory]
    [InlineData(ChapterSettingsService.ModeAuto)]
    [InlineData(ChapterSettingsService.ModeCustom)]
    [InlineData(ChapterSettingsService.ModeOriginal)]
    [InlineData(ChapterSettingsService.ModeChapterMaster)]
    public void CreatePreview_CurrentProjectUsesActivePresentationForEveryChapterMode(string mode)
    {
        var result = _service.CreatePreview(
            CurrentProjectSettings(mode),
            false,
            EmbeddedProjectPreview(52, "Aktuell"));

        Assert.Equal("1 Aktuell 001", result);
    }

    [Fact]
    public void CreatePreview_OriginalUsesCurrentRenderedTimelineWithoutNumbering()
    {
        var track = new TrackInfo { ChapterTitle = "stale", DurationTicks = 1 };
        var project = new ChapterPreviewData(
            "",
            "",
            [track],
            RenderedTimeline:
            [
                new EffectiveChapterInfo(1, track, null, "  Original Titel  ", 0, 1, 0, 1, false)
            ]);
        var settings = CurrentProjectSettings(ChapterSettingsService.ModeOriginal);
        settings.NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString();

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("  Original Titel  ", result);
    }

    [Fact]
    public void CreatePreview_NoChaptersIgnoresAnyPreviouslyRenderedTimeline()
    {
        var result = _service.CreatePreview(
            CurrentProjectSettings(ChapterSettingsService.ModeNone),
            false,
            EmbeddedProjectPreview(52, "Alt"));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CreatePreview_ExcludeAndRestoreFollowTheCurrentRenderedTimeline()
    {
        var settings = CurrentProjectSettings(ChapterSettingsService.ModeAuto);
        var excludedState = EmbeddedProjectPreview(1, "Nach Exclude");
        var restoredState = EmbeddedProjectPreview(52, "Wiederhergestellt");

        Assert.Equal("1 Nach Exclude 001", _service.CreatePreview(settings, false, excludedState));
        Assert.Equal("1 Wiederhergestellt 001", _service.CreatePreview(settings, false, restoredState));
    }

    [Fact]
    public void CreatePreview_CurrentProjectUsesCurrentV4ResolvedContent()
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Auto",
            NumberingStyle = "Plain"
        };
        var project = new ChapterPreviewData(
            "Bestechung",
            "Bestechung",
            [
                Mp3DiscTrack(1, 1, "0101John Grisham Bestechung"),
                Mp3DiscTrack(1, 2, "0102John Grisham Bestechung"),
                Mp3DiscTrack(2, 1, "0201John Grisham Bestechung")
            ]);

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal("1 Bestechung", result);
    }

    [Theory]
    [InlineData("Plain", "1 NSA - Nationales Sicherheits-Amt")]
    [InlineData("ZeroPadded", "001 NSA - Nationales Sicherheits-Amt")]
    public void CreatePreview_CurrentProjectNumbersUnnumberedResolveContentOnlyOnce(
        string numberingStyle,
        string expected)
    {
        var sourceId = Guid.NewGuid();
        var tracks = Enumerable.Range(1, 224)
            .Select(index => new TrackInfo
            {
                SourceId = index == 1 ? sourceId : Guid.NewGuid(),
                ChapterTitle = $"{index:000} NSA - Nationales Sicherheits-Amt",
                DurationTicks = 1
            })
            .ToArray();
        var settings = new ChapterSettings
        {
            PreviewDataSource = "CurrentProject",
            Mode = "Auto",
            NumberingStyle = numberingStyle
        };
        var project = new ChapterPreviewData(
            "NSA",
            "NSA",
            tracks,
            new Dictionary<Guid, string>
            {
                [sourceId] = "NSA - Nationales Sicherheits-Amt"
            });

        var result = _service.CreatePreview(settings, false, project);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("BookTitle", "Settings.ChapterSample.BookTitle")]
    [InlineData("Album", "Settings.ChapterSample.Album")]
    [InlineData("OriginalTagTitle", "Settings.ChapterSample.Tag1")]
    [InlineData("CleanTagTitle", "Settings.ChapterSample.Content1")]
    public void CreatePreview_SampleComponentsAreVisiblyDifferent(string slot, string resourceKey)
    {
        var settings = new ChapterSettings
        {
            PreviewDataSource = "Sample",
            Mode = "Custom",
            NumberingStyle = "Plain",
            Slot1 = slot,
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = _service.CreatePreview(settings, false, currentProjectData: null);

        Assert.Equal(LocalizationText.Get(resourceKey), result);
    }

    [Fact]
    public void CreatePreview_UsesLocalizedSampleDataInSpanish()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var sample = ChapterSettingsPreviewService.CreateSampleData();
            Assert.Equal("Krabat y el molino del diablo", sample.BookTitle);
            Assert.StartsWith("001_", sample.Tracks[0].FileName);
            Assert.Contains("El molino del Koselbruch", sample.Tracks[0].TagTitle);

            var settings = new ChapterSettings { PreviewDataSource = "Sample", Mode = "Auto" };
            var preview = _service.CreatePreview(settings, false, currentProjectData: null);
            Assert.Contains("El molino del Koselbruch", preview);
            Assert.DoesNotContain("Ankunft", preview);

            settings.Mode = "Custom";
            settings.NumberingStyle = "Plain";
            settings.Slot1 = "OriginalFileName";
            settings.Slot2 = "Empty";
            settings.Slot3 = "Empty";
            Assert.Equal("001_01_-_El_molino_del_Koselbruch", _service.CreatePreview(settings, false, null));

            settings.Slot1 = "CleanFileName";
            Assert.Equal("El molino del Koselbruch", _service.CreatePreview(settings, false, null));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static TrackInfo Mp3DiscTrack(int discNumber, int trackNumber, string stem) => new()
    {
        DiscNumber = discNumber,
        TrackNumber = trackNumber,
        TagTitle = stem,
        ChapterTitle = "Bestechung",
        FileName = stem + ".mp3",
        Artist = "John Grisham",
        DurationTicks = TimeSpan.FromSeconds(1).Ticks
    };

    private static ChapterSettings CurrentProjectSettings(string mode) => new()
    {
        PreviewDataSource = ChapterSettingsService.PreviewDataSourceCurrentProject,
        Mode = mode,
        NumberingStyle = ChapterNumberingStyle.Plain.ToString()
    };

    private static ChapterPreviewData EmbeddedProjectPreview(int chapterCount, string titlePrefix)
    {
        var source = new TrackInfo
        {
            SourceId = Guid.NewGuid(),
            ChapterTitle = "stale source title",
            DurationTicks = chapterCount
        };
        source.EmbeddedChapters = Enumerable.Range(0, chapterCount)
            .Select(index => new TrackEmbeddedChapterInfo
            {
                Title = $"{titlePrefix} {index + 1:000}",
                StartTicks = index,
                EndTicks = index + 1
            })
            .ToList();
        var timeline = source.EmbeddedChapters
            .Select((chapter, index) => new EffectiveChapterInfo(
                index + 1,
                source,
                chapter,
                $"{index + 1} {chapter.Title}",
                chapter.StartTicks,
                chapter.EndTicks,
                chapter.StartTicks,
                chapter.EndTicks,
                false))
            .ToArray();

        return new ChapterPreviewData("", "", [source], RenderedTimeline: timeline);
    }
}
