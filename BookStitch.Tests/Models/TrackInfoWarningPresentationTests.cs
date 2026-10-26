using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class TrackInfoWarningPresentationTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "")]
    [InlineData(1, "Mono")]
    [InlineData(2, "Stereo")]
    public void Original_channel_layout_uses_source_channels_independently_of_the_preset(int? channels, string expected)
    {
        var track = new TrackInfo { Channels = channels, PreparedConvertedPreset = "AAC Mono 64 kbps", HasReusableConvertedFile = true };
        Assert.Equal(expected, track.DisplayOriginalChannelLayout);
        Assert.Equal("Mono", track.DisplayPreparedChannelLayout);
        var notifications = new List<string?>();
        track.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        track.Channels = 6;
        Assert.Equal(AudioProcessingService.FormatChannelLayout(6), track.DisplayOriginalChannelLayout);
        Assert.Contains(nameof(TrackInfo.DisplayOriginalChannelLayout), notifications);
    }

    [Theory]
    [InlineData("de-DE", 44100, "44.100")]
    [InlineData("en-US", 44100, "44.100")]
    [InlineData("fr-FR", 44100, "44.100")]
    [InlineData("ja-JP", 48000, "48.000")]
    [InlineData("de-DE", 48000, "48.000")]
    [InlineData("en-US", 11025, "11.025")]
    [InlineData("en-US", 0, "")]
    [InlineData("en-US", null, "")]
    public void Original_sample_rate_displays_hertz_with_fixed_dot_grouping_in_every_culture(string culture, int? hz, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var track = new TrackInfo { SampleRateHz = hz, TargetSampleRateHz = 48000 };
            Assert.Equal(expected, track.DisplayOriginalSampleRateHz);
            track.PreparedConvertedPreset = "AAC Mono 64 kbps";
            track.HasReusableConvertedFile = true;
            Assert.Equal(expected, track.DisplayOriginalSampleRateHz);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Prepared_audio_columns_follow_cache_state_without_overwriting_source_probe()
    {
        var track = new TrackInfo { BitrateKbps = 195, Channels = 2, ChannelLayout = "Stereo" };
        var notifications = new List<string?>();
        track.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        Assert.Null(track.DisplayPreparedBitrateKbps);
        Assert.Empty(track.DisplayPreparedChannelLayout);
        track.PreparedConvertedPreset = "AAC Mono 64 kbps";
        track.HasReusableConvertedFile = true;
        Assert.Equal(64, track.DisplayPreparedBitrateKbps);
        Assert.Equal("Mono", track.DisplayPreparedChannelLayout);
        track.HasReusableConvertedFile = false;
        Assert.Null(track.DisplayPreparedBitrateKbps);
        Assert.Empty(track.DisplayPreparedChannelLayout);
        track.PreparedConvertedPreset = "AAC Stereo 128 kbps";
        track.HasReusableConvertedFile = true;
        Assert.Equal(128, track.DisplayPreparedBitrateKbps);
        Assert.Equal("Stereo", track.DisplayPreparedChannelLayout);
        Assert.Equal(195, track.BitrateKbps);
        Assert.Equal(2, track.Channels);
        Assert.Contains(nameof(TrackInfo.DisplayPreparedBitrateKbps), notifications);
        Assert.Contains(nameof(TrackInfo.DisplayPreparedChannelLayout), notifications);
    }
    [Fact]
    public void DisplayFileWarning_ShowsMessageWithoutIndicatorSymbol()
    {
        var track = new TrackInfo
        {
            FileWarningText = "Quelldatei fehlt"
        };

        Assert.Equal("Quelldatei fehlt", track.DisplayFileWarning);
    }

    [Fact]
    public void DisplayChapterWarning_ShowsMessageWithoutIndicatorSymbol()
    {
        var track = new TrackInfo
        {
            ChapterWarningText = "Kapitel prüfen"
        };

        Assert.Equal("Kapitel prüfen", track.DisplayChapterWarning);
    }

    [Fact]
    public void WarningPresentations_RemainEmptyWithoutWarningText()
    {
        var track = new TrackInfo();

        Assert.Equal(string.Empty, track.DisplayFileWarning);
        Assert.Equal(string.Empty, track.DisplayChapterWarning);
    }

    [Fact]
    public void DisplayFolder_UsesProjectFolderForLocalProjectsUnlessHidden()
    {
        var track = new TrackInfo
        {
            RelativeFolder = "DiscSource",
            ProjectRelativeFolder = "Audiobook\\Part 1"
        };

        Assert.Equal("Audiobook\\Part 1", track.DisplayFolder);

        track.HideFolderDisplay = true;

        Assert.Equal(string.Empty, track.DisplayFolder);
        Assert.Equal("DiscSource", track.RelativeFolder);
        Assert.Equal("Audiobook\\Part 1", track.ProjectRelativeFolder);
    }

    [Fact]
    public void DisplayChapterProposal_SummarizesMultiChapterParents()
    {
        var track = new TrackInfo
        {
            ChapterTitle = "1 Container",
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo { Title = "B", StartTicks = TimeSpan.FromSeconds(1).Ticks, EndTicks = TimeSpan.FromSeconds(2).Ticks },
                new TrackEmbeddedChapterInfo { Title = "A", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks }
            ]
        };

        Assert.True(track.HasEmbeddedChapterDetails);
        Assert.Equal("2 eingebettete Kapitel", track.DisplayChapterProposal);

        var rows = track.EmbeddedChapterRows;
        Assert.Equal("A", rows[0].Title);
        Assert.Equal("00:00", rows[0].Start);
        Assert.Equal(string.Empty, rows[0].TimelineStart);
        Assert.Equal(string.Empty, rows[0].ChapterProposal);
        Assert.Equal("00:01", rows[0].Duration);
        Assert.Same(track, rows[0].SourceTrack);
        Assert.Same(track.EmbeddedChapters[1], rows[0].SourceChapter);
        Assert.Equal(0, rows[0].StartTicks);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, rows[0].EndTicks);
        Assert.False(rows[0].IsSelected);
        Assert.Equal("B", rows[1].Title);
    }

    [Fact]
    public void EmbeddedChapterRows_KeepOriginalTitleSeparateFromTimelineProposal()
    {
        var track = new TrackInfo
        {
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo { Title = "001 Quelle", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks }
            ]
        };

        var row = track.EmbeddedChapterRows[0];

        row.ApplyTimelinePresentation("054 Quelle", "00:53:18");

        Assert.Equal("001 Quelle", row.Title);
        Assert.Equal("054 Quelle", row.ChapterProposal);
        Assert.Equal("00:53:18", row.TimelineStart);
        Assert.Equal("00:00", row.Start);
    }

    [Fact]
    public void EmbeddedChapterRows_MarksSelectedChapter()
    {
        var track = new TrackInfo
        {
            SelectedEmbeddedChapterStartTicks = TimeSpan.FromSeconds(1).Ticks,
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo { Title = "First", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks },
                new TrackEmbeddedChapterInfo { Title = "Second", StartTicks = TimeSpan.FromSeconds(1).Ticks, EndTicks = TimeSpan.FromSeconds(2).Ticks }
            ]
        };

        var rows = track.EmbeddedChapterRows;

        Assert.False(rows[0].IsSelected);
        Assert.True(rows[1].IsSelected);
    }

    [Fact]
    public void SelectEmbeddedChapter_UpdatesExistingChapterRows()
    {
        var track = new TrackInfo
        {
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo { Title = "First", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks },
                new TrackEmbeddedChapterInfo { Title = "Second", StartTicks = TimeSpan.FromSeconds(1).Ticks, EndTicks = TimeSpan.FromSeconds(2).Ticks }
            ]
        };

        var rows = track.EmbeddedChapterRows;

        track.SelectEmbeddedChapter(TimeSpan.FromSeconds(1).Ticks);

        Assert.Same(rows, track.EmbeddedChapterRows);
        Assert.False(rows[0].IsSelected);
        Assert.True(rows[1].IsSelected);

        track.SelectEmbeddedChapter(0);

        Assert.True(rows[0].IsSelected);
        Assert.False(rows[1].IsSelected);
    }

    [Fact]
    public void RawTagWarnings_AreChapterWarnings()
    {
        var track = new TrackInfo
        {
            Warning = "Kein Tag-Titel"
        };

        Assert.Equal(string.Empty, track.DisplayFileWarning);
        Assert.Equal("Kein Tag-Titel", track.DisplayChapterWarning);
        Assert.Equal(string.Empty, track.DisplayHintWarning);
    }

    [Fact]
    public void WarningPresentation_SplitsMultipleMessagesWithoutTooltipState()
    {
        var track = new TrackInfo
        {
            ChapterWarningText = "Kein Tag-Titel · Tags konnten nicht gelesen werden"
        };

        Assert.Equal("Kein Tag-Titel · Tags konnten nicht gelesen werden", track.DisplayChapterWarning);
        Assert.Equal(
            new[] { "Kein Tag-Titel", "Tags konnten nicht gelesen werden" },
            TrackInfo.SplitWarningMessages(track.DisplayChapterWarning));
        Assert.True(track.HasChapterWarning);
    }

    [Fact]
    public void DisplayChapterProposal_UsesChapterTitleForSingleChapterTracks()
    {
        var track = new TrackInfo
        {
            ChapterTitle = "1 Intro",
            EmbeddedChapters =
            [
                new TrackEmbeddedChapterInfo { Title = "Intro", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(1).Ticks }
            ]
        };

        Assert.False(track.HasEmbeddedChapterDetails);
        Assert.Equal("1 Intro", track.DisplayChapterProposal);
    }
}
