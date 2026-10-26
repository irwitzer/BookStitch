using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackAudioProbeApplicationServiceTests
{
    private readonly TrackAudioProbeApplicationService _service = new();

    [Fact]
    public void ApplySuccessfulProbesBySourceId_RehydratesNewTracksWithoutChangingOrderOrUnmatchedTracks()
    {
        var first = new TrackInfo { SourceId = Guid.NewGuid(), FileName = "same.m4a", Index = 2 };
        var second = new TrackInfo { SourceId = Guid.NewGuid(), FileName = "same.m4a", Index = 1 };
        var existing = new TrackInfo { SourceId = Guid.NewGuid(), Index = 3, Codec = "MP3", Warning = "Keep warning" };
        var tracks = new[] { first, second, existing };
        var probes = new Dictionary<Guid, AudioProbeInfo>
        {
            [second.SourceId] = new() { Success = true, CodecType = "audio", CodecName = "flac", Channels = 1, SampleRateHz = 44100, Duration = TimeSpan.FromSeconds(20) },
            [first.SourceId] = new() { Success = true, CodecType = "audio", CodecName = "aac", Channels = 2, SampleRateHz = 44100, Duration = TimeSpan.FromSeconds(10) }
        };
        _service.ApplySuccessfulProbesBySourceId(tracks, probes, ExportPreset.Parse("AAC Stereo 128 kbps"));
        Assert.Equal("AAC", first.Codec);
        Assert.Equal("FLAC", second.Codec);
        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, first.DurationTicks);
        Assert.Equal(TimeSpan.FromSeconds(20).Ticks, second.DurationTicks);
        Assert.Equal(new[] { 2, 1, 3 }, tracks.Select(track => track.Index));
        Assert.Equal("MP3", existing.Codec);
        Assert.Equal("Keep warning", existing.Warning);
    }

    [Fact]
    public void ApplySuccessfulProbe_RehydratesLargeEmbeddedChapterSetAfterResume()
    {
        var track = new TrackInfo
        {
            FileName = "NSA.m4a",
            ChapterTitle = "54 NSA",
            Warning = "Keine gültige Audiodatei erkannt."
        };
        var probe = new AudioProbeInfo
        {
            FilePath = @"C:\Audio\NSA.m4a",
            Success = true,
            CodecType = "audio",
            CodecName = "aac",
            BitrateKbps = 125,
            Channels = 2,
            SampleRateHz = 44100,
            Duration = TimeSpan.FromMinutes(10),
            Chapters = Enumerable.Range(0, 244)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"{index + 1:000} NSA - Nationales Sicherheits-Amt",
                    StartTicks = TimeSpan.FromSeconds(index).Ticks,
                    EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                })
                .ToList()
        };

        _service.ApplySuccessfulProbe(
            track,
            probe,
            ExportPreset.Parse("AAC Mono 64 kbps"),
            markAudioValidationPassed: true);

        Assert.True(track.AudioValidationPassed);
        Assert.Equal("10:00", track.Duration);
        Assert.Equal(TimeSpan.FromMinutes(10).Ticks, track.DurationTicks);
        Assert.Equal(244, track.EmbeddedChapters.Count);
        Assert.Equal("001 NSA - Nationales Sicherheits-Amt", track.EmbeddedChapters[0].Title);
        Assert.Equal("244 NSA - Nationales Sicherheits-Amt", track.EmbeddedChapters[^1].Title);
        Assert.Equal(125, track.BitrateKbps);
        Assert.Equal(2, track.Channels);
        Assert.Equal("Stereo", track.ChannelLayout);
        Assert.Equal("AAC", track.Codec);
        Assert.Equal(ProcessingActionIds.Convert, track.ProcessingAction);
        Assert.Empty(track.Warning);
    }

    [Fact]
    public void ApplySuccessfulProbe_PreservesEffectiveChapterCountForResumedProject()
    {
        var first = new TrackInfo
        {
            DurationTicks = TimeSpan.FromSeconds(52).Ticks,
            EmbeddedChapters = Enumerable.Range(0, 52)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"A {index + 1}",
                    StartTicks = TimeSpan.FromSeconds(index).Ticks,
                    EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                })
                .ToList()
        };
        var middle = new TrackInfo
        {
            ChapterTitle = "53 Intro",
            DurationTicks = TimeSpan.FromSeconds(1).Ticks
        };
        var resumed = new TrackInfo
        {
            ChapterTitle = "54 NSA",
            DurationTicks = TimeSpan.FromSeconds(244).Ticks
        };
        var probe = new AudioProbeInfo
        {
            FilePath = @"C:\Audio\NSA.m4a",
            Success = true,
            CodecType = "audio",
            CodecName = "aac",
            Channels = 2,
            SampleRateHz = 44100,
            Duration = TimeSpan.FromSeconds(244),
            Chapters = Enumerable.Range(0, 244)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"NSA {index + 1}",
                    StartTicks = TimeSpan.FromSeconds(index).Ticks,
                    EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                })
                .ToList()
        };

        _service.ApplySuccessfulProbe(
            resumed,
            probe,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            markAudioValidationPassed: false);

        var tracks = new[] { first, middle, resumed };
        var timeline = new ChapterTimelineService().BuildTimeline(tracks);

        Assert.Equal(297, timeline.Count);
        Assert.Equal(54, timeline[53].OutputChapterIndex);
        Assert.Equal(resumed, timeline[53].SourceTrack);
        Assert.Equal(297, timeline[^1].OutputChapterIndex);
    }
}
