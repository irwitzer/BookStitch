using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedAacTimelineServiceTests
{
    [Fact]
    public async Task Bounded_analysis_preserves_order_and_repetitions_with_monotonic_progress()
    {
        var paths = Enumerable.Range(0, 8).Select(i => Path.Combine(Path.GetTempPath(), $"timing-{i}.m4a")).ToArray();
        var requested = paths.Reverse().Concat([paths[0], paths[7]]).ToArray();
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object();
        var active = 0;
        var peak = 0;
        var calls = 0;
        var progress = new List<int>();
        var result = await ConvertedAacTimelineService.AnalyzeManyAsync(requested, 2,
            async (path, token) =>
            {
                lock (gate)
                {
                    active++;
                    peak = Math.Max(peak, active);
                    if (++calls == 2) bothStarted.TrySetResult();
                }
                try
                {
                    await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    return new ConvertedAacTiming(1, 1, 1, Array.IndexOf(paths, path) + 1, 1, 1);
                }
                finally { lock (gate) active--; }
            }, (completed, total) => { Assert.Equal(8, total); progress.Add(completed); }, CancellationToken.None);
        Assert.Equal(2, peak);
        Assert.Equal(8, calls);
        Assert.Equal(requested.Select(path => (long)Array.IndexOf(paths, path) + 1), result.Select(item => item.SpanUnits));
        Assert.Equal(Enumerable.Range(0, 9), progress);
    }

    [Fact]
    public async Task Cancelled_analysis_waits_for_workers_and_does_not_publish_completed_progress()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = 0;
        var progress = new List<int>();
        var task = ConvertedAacTimelineService.AnalyzeManyAsync([Path.Combine(Path.GetTempPath(), "cancel.m4a")], 4,
            async (_, token) =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { Interlocked.Increment(ref exited); }
                throw new InvalidOperationException("Cancellation must stop this worker.");
            }, (completed, _) => progress.Add(completed), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, exited);
        Assert.Equal([0], progress);
    }

    [Fact]
    public void Streaming_compact_packets_match_exact_json_timing_including_discard_padding()
    {
        using var document = JsonDocument.Parse(PacketJson);
        var accumulator = new ConvertedAacTimelineService.PacketAccumulator(
            document.RootElement.GetProperty("streams")[0], 100, 20);
        accumulator.AddCompactPacket("packet|pts=-1024|dts=-1024|duration=1024");
        accumulator.AddCompactPacket("packet|pts=0|dts=0|duration=922|side_data|discard_padding=102");
        Assert.Equal(ConvertedAacTimelineService.Parse(PacketJson, 100, 20), accumulator.Complete());
    }

    [Fact]
    public void Streaming_large_packet_count_retains_exact_span_without_retaining_packets()
    {
        using var document = JsonDocument.Parse(PacketJson);
        var accumulator = new ConvertedAacTimelineService.PacketAccumulator(
            document.RootElement.GetProperty("streams")[0], 100, 20);
        for (long index = 0; index < 200_000; index++)
            accumulator.AddCompactPacket($"packet|pts={index * 1024}|dts={index * 1024}|duration=1024");
        Assert.Equal(200_000L * 1024, accumulator.Complete().SpanUnits);
    }

    private const string PacketJson = """
        {"streams":[{"codec_name":"aac","time_base":"1/44100","sample_rate":"44100"}],
        "packets":[{"pts":-1024,"dts":-1024,"duration":1024},
        {"pts":0,"dts":0,"duration":922,"side_data_list":[{"discard_padding":102}]}]}
        """;

    [Fact]
    public void Parse_UsesActualPacketSpanIncludingPrimingAndDiscardedCodedSamples()
    {
        var timing = ConvertedAacTimelineService.Parse(PacketJson, 100, 20);
        Assert.Equal(2048, timing.SpanUnits);
        Assert.Equal(1, timing.TimeBaseNumerator);
        Assert.Equal(44100, timing.TimeBaseDenominator);
    }

    [Theory]
    [InlineData("\"duration\":922", "\"duration\":0")]
    [InlineData("\"pts\":0", "\"pts\":1")]
    [InlineData("\"discard_padding\":102", "\"discard_padding\":-1")]
    [InlineData("\"codec_name\":\"aac\"", "\"codec_name\":\"mp3\"")]
    public void Parse_RejectsUnusableTimeline(string original, string replacement) =>
        Assert.Throws<InvalidDataException>(() => ConvertedAacTimelineService.Parse(
            PacketJson.Replace(original, replacement), 100, 20));

    [Fact]
    public void BuildSegments_RoundsCumulativeRationalTimeWithoutPerTrackRoundingDrift()
    {
        var timing = new ConvertedAacTiming(1, 100, 20, 7168, 1, 44100);
        var segments = ConvertedAacTimelineService.BuildSegments(Enumerable.Repeat(timing, 10000).ToArray());
        Assert.Equal((long)decimal.Round(10000m * 7168 / 44100 * 1000000), segments[^1].EndMicroseconds);
        Assert.Equal(segments[^1].EndMicroseconds,
            segments.Sum(segment => segment.EndMicroseconds - segment.StartMicroseconds));
    }

    [Fact]
    public void MapChapters_MixedProjectPreservesEmbeddedOffsetsTitlesOrderAndSourceTiming()
    {
        var tracks = Enumerable.Range(1, 3).Select(index => new TrackInfo
        {
            Index = index, SourceId = Guid.NewGuid(), DurationTicks = TimeSpan.TicksPerSecond,
            ChapterTitle = "Chapter " + index
        }).ToArray();
        tracks[1].EmbeddedChapters =
        [new TrackEmbeddedChapterInfo { Title = "Embedded", StartTicks = 1234567, EndTicks = 7654321 }];
        var original = new ChapterTimelineService().BuildTimeline(tracks);
        var timings = Enumerable.Range(0, 3).Select(_ =>
            new ConvertedAacTiming(1, 100, 20, 46080, 1, 44100)).ToArray();
        var segments = ConvertedAacTimelineService.BuildSegments(timings);
        var result = ConvertedAacTimelineService.MapChapters(original, tracks, segments);
        Assert.Equal(segments[0].StartMicroseconds * 10, result[0].StartTicks);
        Assert.Equal(segments[0].EndMicroseconds * 10, result[0].EndTicks);
        Assert.Equal(segments[1].StartMicroseconds * 10 + 1234567, result[1].StartTicks);
        Assert.Equal(segments[1].StartMicroseconds * 10 + 7654321, result[1].EndTicks);
        Assert.Equal(segments[2].StartMicroseconds * 10, result[2].StartTicks);
        Assert.Equal(original.Select(item => item.Title), result.Select(item => item.Title));
        Assert.Equal(original.Select(item => item.SourceStartTicks), result.Select(item => item.SourceStartTicks));
        Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(track => track.Index));
    }

    [Fact]
    public void Manifest_RoundTripPreservesPresetTimingAndLegacyProjectsHaveNoCache()
    {
        var timing = ConvertedAacTimelineService.Parse(PacketJson, 100, 20);
        var manifest = new ExportWorkManifest
        {
            Tracks = [new ExportWorkManifestTrack { SourceId = Guid.NewGuid(), Preset = "preset-a", AacTiming = timing },
                new ExportWorkManifestTrack { SourceId = Guid.NewGuid(), Preset = "preset-b" }]
        };
        var reloaded = JsonSerializer.Deserialize<ExportWorkManifest>(JsonSerializer.Serialize(manifest))!;
        Assert.Equal(timing, reloaded.Tracks[0].AacTiming);
        Assert.Null(reloaded.Tracks[1].AacTiming);
        Assert.Null(JsonSerializer.Deserialize<ExportWorkManifestTrack>("{}")!.AacTiming);
    }
}
