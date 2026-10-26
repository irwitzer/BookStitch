using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Fact]
    public async Task Unknown_source_rate_uses_44100_conversion_instead_of_unverified_copy()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "unknown.m4a");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
            "sine=sample_rate=48000", "-t", "1", "-ac", "2", "-c:a", "aac", "-b:a", "128k", source]);
        var original = await File.ReadAllBytesAsync(source);
        var track = new TrackInfo { ProcessingAction = ProcessingActionIds.Reuse, SampleRateHz = null };
        FolderSampleRateService.Apply([track], FolderSampleRateService.SelectTarget([track]));
        var prepared = Path.Combine(folder.Path, "prepared.m4a");
        await new AacExportProcessingService().PrepareTrackForExportAsync(track, source, prepared,
            ExportPreset.Parse("AAC Stereo 128 kbps"), tools.FfmpegPath, default, _ => { },
            ffprobePath: Path.Combine(folder.Path, "missing-ffprobe"));
        Assert.Equal(44100, (await new AudioInfoService().ProbeAsync(prepared, tools.FfprobePath)).SampleRateHz);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
    }

    [Theory]
    [InlineData(16000)] [InlineData(22050)] [InlineData(24000)] [InlineData(32000)]
    [InlineData(44100)] [InlineData(48000)] [InlineData(64000)] [InlineData(88200)] [InlineData(96000)]
    public async Task Matching_folder_aac_retains_rate_and_bytes_while_other_presets_reencode(int rate)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.m4a");
        var bitrate = rate <= 24000 ? 64 : 128;
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
            $"anoisesrc=sample_rate={rate}:seed=42", "-t", "2", "-ac", "2", "-c:a", "aac", "-b:a", $"{bitrate}k", source]);
        var original = await File.ReadAllBytesAsync(source);
        var probe = await new AudioInfoService().ProbeAsync(source, tools.FfprobePath);
        Assert.True(probe.Success, probe.ErrorMessage);
        var preset = ExportPreset.Parse($"AAC Stereo {bitrate} kbps");
        var track = new TrackInfo { FilePath = source, FileName = "source.m4a", Extension = ".m4a" };
        new TrackAudioProbeApplicationService().ApplySuccessfulProbe(track, probe, preset, true);
        FolderSampleRateService.Apply([track], FolderSampleRateService.SelectTarget([track]));
        track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
        Assert.Equal(ProcessingActionIds.Reuse, track.ProcessingAction);
        var processing = new AacExportProcessingService();
        var copied = Path.Combine(folder.Path, "prepared.m4a");
        await processing.PrepareTrackForExportAsync(track, source, copied, preset, tools.FfmpegPath,
            default, _ => { }, ffprobePath: tools.FfprobePath);
        Assert.Equal(original, await File.ReadAllBytesAsync(copied));
        await processing.ValidateMergeInputsAsync([copied], rate, tools.FfprobePath, default, preset);
        await Assert.ThrowsAsync<InvalidDataException>(() => processing.ValidateMergeInputsAsync(
            [copied], rate, tools.FfprobePath, default, ExportPreset.Parse("AAC Mono 64 kbps")));
        await Assert.ThrowsAsync<InvalidDataException>(() => processing.ValidateMergeInputsAsync(
            [copied], rate == 48000 ? 44100 : 48000, tools.FfprobePath, default));
        var other = ExportPreset.Parse("AAC Mono 64 kbps");
        track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, other);
        var converted = Path.Combine(folder.Path, "converted.m4a");
        await processing.PrepareTrackForExportAsync(track, source, converted, other, tools.FfmpegPath,
            default, _ => { }, ffprobePath: tools.FfprobePath);
        var output = await new AudioInfoService().ProbeAsync(converted, tools.FfprobePath);
        Assert.Equal(rate, output.SampleRateHz); Assert.Equal(1, output.Channels);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", converted, "-f", "null", "-"]);
        await VerifyFolderReviewAndResumeAsync(folder.Path, [track], preset, rate, tools.FfmpegPath, tools.FfprobePath);
    }

    [Fact]
    public async Task Mixed_folder_aac_normalizes_only_minority_and_merges_at_fixed_rate()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var tracks = new List<TrackInfo>();
        foreach (var rate in new[] { 48000, 44100, 48000 })
        {
            var source = Path.Combine(folder.Path, $"source-{tracks.Count}.m4a");
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
                $"anoisesrc=sample_rate={rate}:seed=42", "-t", "2", "-ac", "2", "-c:a", "aac", "-b:a", "128k", source]);
            var track = new TrackInfo { FilePath = source, FileName = Path.GetFileName(source), Extension = ".m4a" };
            new TrackAudioProbeApplicationService().ApplySuccessfulProbe(track,
                await new AudioInfoService().ProbeAsync(source, tools.FfprobePath), preset, true);
            tracks.Add(track);
        }
        var target = FolderSampleRateService.SelectTarget(tracks); Assert.Equal(48000, target);
        FolderSampleRateService.Apply(tracks, target);
        var processing = new AacExportProcessingService(); var paths = new List<string>();
        foreach (var track in tracks)
        {
            var before = await File.ReadAllBytesAsync(track.FilePath);
            track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
            Assert.Equal(track.SampleRateHz == target ? ProcessingActionIds.Reuse : ProcessingActionIds.Convert, track.ProcessingAction);
            var path = Path.Combine(folder.Path, $"prepared-{paths.Count}.m4a"); paths.Add(path);
            await processing.PrepareTrackForExportAsync(track, track.FilePath, path, preset, tools.FfmpegPath,
                default, _ => { }, ffprobePath: tools.FfprobePath);
            Assert.Equal(before, await File.ReadAllBytesAsync(track.FilePath));
            if (track.SampleRateHz == target) Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        await processing.ValidateMergeInputsAsync(paths, target, tools.FfprobePath, default);
        var concat = Path.Combine(folder.Path, "concat.txt"); var chapters = Path.Combine(folder.Path, "chapters.txt");
        await File.WriteAllLinesAsync(concat, paths.Select(path => $"file '{path.Replace("\\", "/")}'"));
        await File.WriteAllTextAsync(chapters, ";FFMETADATA1\n");
        var merged = Path.Combine(folder.Path, "merged.m4b");
        await processing.MergeConvertedTracksAsync(concat, chapters, merged, 6 * TimeSpan.TicksPerSecond,
            tools.FfmpegPath, default, _ => { });
        Assert.Equal(target, (await new AudioInfoService().ProbeAsync(merged, tools.FfprobePath)).SampleRateHz);
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", merged, "-f", "null", "-"]);
        await VerifyFolderReviewAndResumeAsync(folder.Path, tracks, preset, target, tools.FfmpegPath, tools.FfprobePath);
    }

    private static async Task VerifyFolderReviewAndResumeAsync(string root, IReadOnlyList<TrackInfo> tracks,
        ExportPreset preset, int rate, string ffmpeg, string ffprobe)
    {
        var project = Path.Combine(root, "project");
        new FolderProjectManifestService().Create(project, root, rate);
        foreach (var track in tracks)
            track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
        var output = Path.Combine(root, "final.m4b");
        var plan = new ExportPlanService().Create(new ExportPlanRequest(tracks, root, root,
            output, preset.DisplayName, 2, project));
        Assert.Equal(rate, plan.TargetSampleRateHz);
        var store = new WorkManifestService(); var audio = new AacExportProcessingService();
        var workflow = new ExportWorkflowService(store,
            new ProjectSnapshotService(new Mp3DiscProjectService(), new AudioDiscProjectService(), store), audio,
            new ConvertedFileCleanupService(),
            new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store)),
            new ConvertedTrackPreparationWorkflowService(store, audio), new ExportChapterService(),
            new FinalTagService(), new FinalOutputStorageService());
        var snapshot = new ProjectSnapshotUiState(preset.DisplayName, "2", ".m4b", root,
            "{Titel}", "Sample rate test", "Author", "Album", "", "", "", "", "final.m4b");
        var request = new ExportWorkflowRequest(plan, root, ffmpeg, snapshot,
            new FinalAudioTagData { Title = "Sample rate test" },
            new ChapterSettings { Mode = ChapterSettingsService.ModeNone }, FfprobePath: ffprobe);
        var states = new List<ProjectPipelineState>(); var decisions = 0;
        var callbacks = new ExportWorkflowCallbacks { MergeAutomaticallyAfterConversion = () => { decisions++; return false; },
            SetPipelineState = state => states.Add(state) };
        var review = await workflow.RunAsync(request, callbacks, default);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, review.Status);
        Assert.Equal(1, decisions); Assert.False(File.Exists(output));
        Assert.Contains(ProjectPipelineState.ReviewBeforeMerge, states);
        var evidence = store.ReadOnly(plan.ManifestPath).Tracks.Select(entry => entry.PreparationId).ToArray();
        var resumed = await workflow.RunAsync(request with { ManualMergeRequested = true }, callbacks, default);
        Assert.True(resumed.Status == ExportWorkflowResultStatus.Completed, resumed.Error?.ToString());
        Assert.Equal(2, decisions);
        Assert.Equal(evidence, store.ReadOnly(plan.ManifestPath).Tracks.Select(entry => entry.PreparationId));
        Assert.Equal(rate, (await new AudioInfoService().ProbeAsync(output, ffprobe)).SampleRateHz);
        await RunProcessAsync(ffmpeg, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);
    }
}
