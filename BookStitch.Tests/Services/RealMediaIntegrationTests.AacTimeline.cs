using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    private readonly ITestOutputHelper _output;
    public RealMediaIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Mixed_rates_and_repeated_source_prepare_separately_merge_and_decode_with_auto_chapters()
    {
        var tools = ResolveFfmpegToolchain();
        Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var tracks = new List<TrackInfo>();
        var processing = new AacExportProcessingService();
        foreach (var (rate, extension) in new[] { (44100, "wav"), (48000, "wav"), (48000, "m4a") })
        {
            var source = Path.Combine(temp.Path, $"source-{tracks.Count}.{extension}");
            await RunProcessAsync(tools.FfmpegPath,
                ["-v", "error", "-y", "-f", "lavfi", "-i", $"sine=frequency=500:sample_rate={rate}",
                    "-t", "2", source]);
            tracks.Add(new TrackInfo { SourceId = Guid.NewGuid(), Index = tracks.Count + 1,
                FilePath = source, FileName = Path.GetFileName(source), DurationTicks = 2 * TimeSpan.TicksPerSecond,
                TagTitle = "Abschnitt", SampleRateHz = rate,
                ProcessingAction = extension == "m4a" ? ProcessingActionIds.Reuse : ProcessingActionIds.Convert });
        }
        tracks.Add(new TrackInfo { SourceId = tracks[0].SourceId, Index = 4, FilePath = tracks[0].FilePath,
            FileName = tracks[0].FileName, DurationTicks = tracks[0].DurationTicks,
            TagTitle = "Abschnitt", ProcessingAction = ProcessingActionIds.Convert });
        var store = new WorkManifestService();
        var manifest = new ExportWorkManifest();
        var plan = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store))
            .Build(manifest, ProjectManifestTypes.FolderProject, tracks, temp.Path, Path.Combine(temp.Path, "converted"), Stereo96);
        await new ConvertedTrackPreparationWorkflowService(store, processing).RunAsync(
            new ConvertedTrackPreparationWorkflowRequest(plan, manifest, Path.Combine(temp.Path, "manifest.json"),
                Stereo96, tools.FfmpegPath, 4, 8 * TimeSpan.TicksPerSecond, new object()),
            new ConvertedTrackPreparationWorkflowCallbacks(), CancellationToken.None);
        Assert.Equal(4, manifest.Tracks.Count);
        Assert.Equal(4, plan.Items.Select(item => item.ConvertedPath).Distinct().Count());
        foreach (var item in plan.Items)
        {
            var probe = await new AudioInfoService().ProbeAsync(item.ConvertedPath, tools.FfprobePath);
            Assert.Equal(44100, probe.SampleRateHz);
        }
        var timings = await ConvertedAacTimelineService.AnalyzeManyAsync(
            plan.Items.Select(item => item.ConvertedPath).ToArray(), 4,
            (path, token) => processing.AnalyzeConvertedTimingAsync(path, tools.FfprobePath, null, token),
            null, CancellationToken.None);
        var chapters = new ChapterAutoEngineService().Apply(new ChapterAutoEngineRequest(tracks,
            ProjectManifestTypes.FolderProject, "mixed-rate-test", "",
            new ChapterCanonicalMetadataSnapshot(1, "Buch", MetadataOrigin.User, null, MetadataOrigin.None),
            new ChapterSettings { Mode = ChapterSettingsService.ModeAuto }, false));
        var segments = ConvertedAacTimelineService.BuildSegments(timings);
        var timeline = ConvertedAacTimelineService.MapChapters(chapters.RenderedTimeline, tracks, segments);
        var concat = Path.Combine(temp.Path, "concat.txt");
        var metadata = Path.Combine(temp.Path, "chapters.txt");
        var output = Path.Combine(temp.Path, "merged.m4b");
        await File.WriteAllLinesAsync(concat, plan.Items.SelectMany((item, index) => new[] {
            $"file '{item.ConvertedPath.Replace("\\", "/")}'", "duration " + segments[index].ConcatDuration }));
        await File.WriteAllTextAsync(metadata, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(timeline));
        await processing.MergeConvertedTracksAsync(concat, metadata, output, segments[^1].EndMicroseconds * 10,
            tools.FfmpegPath, CancellationToken.None, _ => { });
        var final = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
        Assert.True(final.Success, final.ErrorMessage);
        Assert.Equal(4, final.Chapters.Count);
        Assert.InRange(Math.Abs(final.Duration!.Value.Ticks - segments[^1].EndMicroseconds * 10), 0, TimeSpan.TicksPerMillisecond);
        Assert.Equal(timeline.Select(chapter => chapter.Title), final.Chapters.Select(chapter => chapter.Title));
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", output, "-map", "0:a:0", "-f", "null", "-"]);
        var reusable = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store))
            .Build(manifest, ProjectManifestTypes.FolderProject, tracks, temp.Path, Path.Combine(temp.Path, "converted"), Stereo96);
        Assert.Equal(4, reusable.ReusableCount);
        var damaged = reusable.Items[0].ConvertedPath;
        var timestamp = File.GetLastWriteTimeUtc(damaged);
        var bytes = await File.ReadAllBytesAsync(damaged);
        bytes[FindBoxType(bytes, "mdat") + 8] ^= 1;
        await File.WriteAllBytesAsync(damaged, bytes);
        File.SetLastWriteTimeUtc(damaged, timestamp);
        var recovery = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store))
            .Build(manifest, ProjectManifestTypes.FolderProject, tracks, temp.Path, Path.Combine(temp.Path, "converted"), Stereo96);
        // Same size/timestamp intentionally remain reusable without content hashing.
        Assert.Empty(recovery.PendingItems);
        Assert.Equal(4, recovery.ReusableCount);
        File.SetLastWriteTimeUtc(damaged, timestamp.AddSeconds(2));
        var changedAttributes = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store))
            .Build(manifest, ProjectManifestTypes.FolderProject, tracks, temp.Path, Path.Combine(temp.Path, "converted"), Stereo96);
        Assert.Equal(damaged, Assert.Single(changedAttributes.PendingItems).ConvertedPath);
        Assert.Equal(3, changedAttributes.ReusableCount);
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public async Task AacStreamCopy_ManyShortSegmentsHaveNoCumulativeChapterDriftAndKeepEveryPacket(int rate)
    {
        var tools = ResolveFfmpegToolchain();
        Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        const int count = 32;
        var processing = new AacExportProcessingService();
        var tracks = new List<TrackInfo>();
        var paths = new List<string>();
        var timings = new List<ConvertedAacTiming>();
        var packetHashes = new List<string>();
        var packetCounts = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var source = Path.Combine(temp.Path, $"source-{index}.wav");
            var converted = Path.Combine(temp.Path, $"converted-{index}.m4a");
            var seconds = 0.137m + index % 4 * 0.001m;
            await RunProcessAsync(tools.FfmpegPath,
                ["-v", "error", "-y", "-f", "lavfi", "-i",
                 $"sine=frequency={400 + index * 30}:sample_rate={rate}", "-t",
                 seconds.ToString(CultureInfo.InvariantCulture), source]);
            var track = new TrackInfo { SourceId = Guid.NewGuid(), Index = index + 1,
                FilePath = source, FileName = Path.GetFileName(source),
                DurationTicks = (long)(seconds * TimeSpan.TicksPerSecond), ProcessingAction = ProcessingActionIds.Convert };
            if (index % 3 == 1)
                track.EmbeddedChapters = [new TrackEmbeddedChapterInfo
                { Title = "Embedded", StartTicks = 400000, EndTicks = 900000 }];
            await processing.PrepareTrackForExportAsync(track, source, converted, Stereo96,
                tools.FfmpegPath, CancellationToken.None, _ => { });
            timings.Add(await processing.AnalyzeConvertedTimingAsync(converted, tools.FfprobePath, null, CancellationToken.None));
            var packets = await ProbePacketsAsync(converted, tools.FfprobePath);
            packetCounts.Add(packets.Count);
            packetHashes.AddRange(packets.Select(packet => packet.Hash));
            tracks.Add(track);
            paths.Add(converted);
        }

        var original = new ChapterTimelineService().BuildTimeline(tracks);
        var segments = ConvertedAacTimelineService.BuildSegments(timings);
        var timeline = ConvertedAacTimelineService.MapChapters(original, tracks, segments);
        var manifest = new ExportWorkManifest { Tracks = tracks.Select((track, index) =>
            new ExportWorkManifestTrack { SourceId = track.SourceId, Preset = Stereo96.DisplayName,
                ConvertedPath = paths[index], AacTiming = timings[index] }).ToList() };
        var manifestPath = Path.Combine(temp.Path, "project.json");
        var store = new WorkManifestService();
        store.Save(manifestPath, manifest);
        var loaded = store.LoadOrCreate(manifestPath, temp.Path, temp.Path, Stereo96.DisplayName);
        var restored = new List<ConvertedAacTiming>();
        for (var index = 0; index < count; index++)
        {
            // A valid cache is usable even when FFprobe is unavailable: no re-encode or re-analysis.
            var cached = await processing.AnalyzeConvertedTimingAsync(paths[index], "not-a-tool",
                loaded.Tracks[index].AacTiming, CancellationToken.None);
            Assert.Same(loaded.Tracks[index].AacTiming, cached);
            restored.Add(cached);
        }
        Assert.Equal(segments, ConvertedAacTimelineService.BuildSegments(restored));
        var concat = Path.Combine(temp.Path, "concat.txt");
        await File.WriteAllLinesAsync(concat, paths.SelectMany((path, index) => new[]
            { "file '" + path.Replace('\\', '/') + "'", "duration " + segments[index].ConcatDuration }),
            new UTF8Encoding(false));
        var metadata = Path.Combine(temp.Path, "chapters.txt");
        await File.WriteAllTextAsync(metadata, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(timeline));
        decimal maximumBoundaryError = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            var output = Path.Combine(temp.Path, $"final-{pass}.m4b");
            await processing.MergeConvertedTracksAsync(concat, metadata, output,
                segments[^1].EndMicroseconds * 10, tools.FfmpegPath, CancellationToken.None, _ => { });
            new FinalTagService().WriteFinalTags(output, new FinalAudioTagData { Title = "Metadata pass " + pass });
            var packets = await ProbePacketsAsync(output, tools.FfprobePath);
            Assert.Equal(packetHashes, packets.Select(packet => packet.Hash));
            var packetIndex = 0;
            for (var index = 0; index < count; index++)
            {
                var actual = packets[packetIndex].PtsSeconds - packets[0].PtsSeconds;
                var expected = segments[index].StartMicroseconds / 1_000_000m;
                var error = Math.Abs(actual - expected);
                maximumBoundaryError = Math.Max(maximumBoundaryError, error);
                Assert.True(error <= 1m / rate, $"Segment {index}: {error * 1000} ms");
                packetIndex += packetCounts[index];
            }
            var probe = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
            Assert.True(probe.Success, probe.ErrorMessage);
            Assert.Equal(timeline.Count, probe.Chapters.Count);
            for (var index = 0; index < timeline.Count; index++)
                Assert.InRange(Math.Abs(probe.Chapters[index].StartTicks - timeline[index].StartTicks),
                    0, TimeSpan.TicksPerMillisecond / 2 + 1);
        }
        var sourceLastStart = tracks.Take(count - 1).Sum(track => track.DurationTicks!.Value);
        var drift = (segments[^1].StartMicroseconds * 10 - sourceLastStart) / 10000m;
        Assert.True(drift > 500, $"The fixture must expose accumulated drift: {drift} ms");
        _output.WriteLine($"rate={rate}; segments={count}; legacy source-timeline error={drift:F3} ms; " +
            $"maximum corrected packet boundary error={maximumBoundaryError * 1000:F6} ms; every AAC packet unchanged.");
    }

    [Fact]
    public async Task AacTimingCache_ReconstructsInvalidTimingAndTrustsMatchingStructuralFileState()
    {
        var tools = ResolveFfmpegToolchain();
        Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "converted.m4a");
        await CreateSilentAacAsync(tools, path, 1);
        var first = await ConvertedAacTimelineService.AnalyzeAsync(path, tools.FfprobePath, null, CancellationToken.None);
        var broken = first with { SpanUnits = 0 };
        var repaired = await ConvertedAacTimelineService.AnalyzeAsync(path, tools.FfprobePath, broken, CancellationToken.None);
        Assert.Equal(first, repaired);
        var modified = File.GetLastWriteTimeUtc(path);
        var bytes = await File.ReadAllBytesAsync(path);
        // Change an audio payload byte while retaining the MP4 structure and timing.
        var mdat = FindBoxType(bytes, "mdat");
        bytes[mdat + 8] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        File.SetLastWriteTimeUtc(path, modified);
        var changed = await ConvertedAacTimelineService.AnalyzeAsync(path, tools.FfprobePath, first, CancellationToken.None);
        Assert.Same(first, changed);
        Assert.Equal(first.SpanUnits, changed.SpanUnits);
        await CreateSilentAacAsync(tools, path, 2);
        var reconverted = await ConvertedAacTimelineService.AnalyzeAsync(path, tools.FfprobePath, changed, CancellationToken.None);
        Assert.True(reconverted.SpanUnits > changed.SpanUnits);
    }

    private static int FindBoxType(byte[] bytes, string type)
    {
        var index = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(type));
        Assert.True(index >= 4);
        return index;
    }

    private static async Task<List<(string Hash, decimal PtsSeconds)>> ProbePacketsAsync(string path, string ffprobePath)
    {
        var result = await new FfprobeRunnerService().RunAsync(ffprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256",
             "-show_entries", "packet=pts_time,data_hash", "-of", "json", path],
            FfprobeRunnerService.DefaultTimeout, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Stdout);
        return document.RootElement.GetProperty("packets").EnumerateArray().Select(packet =>
            (packet.GetProperty("data_hash").GetString()!,
             decimal.Parse(packet.GetProperty("pts_time").GetString()!, CultureInfo.InvariantCulture))).ToList();
    }
}
