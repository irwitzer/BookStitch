using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Theory]
    [InlineData("mp3", false, 0)]
    [InlineData("matching", false, 2)]
    [InlineData("partial", true, 1)]
    [InlineData("different", true, 2)]
    [InlineData("original", true, 2)]
    [InlineData("all", true, 0)]
    public async Task Compact_decision_drives_real_export_review_and_resume(string scenario, bool prompt, int reused)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var tracks = new List<TrackInfo>();
        for (var i = 0; i < 2; i++)
        {
            var mp3 = scenario == "mp3" || ((scenario == "partial" || scenario == "all") && i == 1);
            var rate = scenario == "different" ? 48000 : 44100;
            var source = Path.Combine(folder.Path, $"source-{i}.{(mp3 ? "mp3" : "m4a")}");
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
                $"anoisesrc=sample_rate={rate}:seed=42", "-t", "2", "-ac", "2", "-c:a", mp3 ? "libmp3lame" : "aac",
                "-b:a", scenario == "original" ? "73k" : "128k", source]);
            tracks.Add(new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, FileName = Path.GetFileName(source), Extension = Path.GetExtension(source) });
        }
        var sourceBytes = tracks.Select(t => File.ReadAllBytes(t.FilePath)).ToArray();
        var options = await new AacDecisionPreflightService().AnalyzeAsync(tracks, folder.Path,
            ExportPreset.Parse("AAC Stereo 128 kbps"), 44100, tools.FfmpegPath, tools.FfprobePath, default);
        var selection = AacDecisionSelectionService.Select(options);
        Assert.Equal(prompt, selection.NeedsDialog);
        var accepted = selection.NeedsDialog ? AacDecisionSelectionService.Confirm(options,
            scenario == "all" ? AacDecisionChoice.EncodeAll : AacDecisionChoice.PassThrough)! : selection.Plan;
        Assert.Equal(reused, accepted.ReusedCount);
        AacDecisionPlanner.Apply(accepted, tracks);
        var project = Path.Combine(folder.Path, "project"); var mainStore = new FolderProjectManifestService();
        mainStore.Create(project, folder.Path, accepted.SampleRateHz); mainStore.SavePlan(project, accepted);
        var output = Path.Combine(folder.Path, "result.m4b");
        var exportPlan = new ExportPlanService().Create(new ExportPlanRequest(tracks, folder.Path, folder.Path,
            output, accepted.EffectivePreset, 2, project));
        var store = new WorkManifestService(); var audio = new AacExportProcessingService();
        var workflow = new ExportWorkflowService(store,
            new ProjectSnapshotService(new Mp3DiscProjectService(), new AudioDiscProjectService(), store), audio,
            new ConvertedFileCleanupService(), new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(store)),
            new ConvertedTrackPreparationWorkflowService(store, audio), new ExportChapterService(), new FinalTagService(), new FinalOutputStorageService());
        var snapshot = new ProjectSnapshotUiState(accepted.EffectivePreset, "2", ".m4b", folder.Path,
            "{Titel}", "AAC decision", "Author", "Album", "", "", "", "", "result.m4b");
        var request = new ExportWorkflowRequest(exportPlan, folder.Path, tools.FfmpegPath, snapshot,
            new FinalAudioTagData { Title = "AAC decision" }, new ChapterSettings { Mode = ChapterSettingsService.ModeNone }, FfprobePath: tools.FfprobePath);
        var callbacks = new ExportWorkflowCallbacks { MergeAutomaticallyAfterConversion = () => false };
        var review = await workflow.RunAsync(request, callbacks, default);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, review.Status);
        var prepared = store.ReadOnly(exportPlan.ManifestPath);
        Assert.Equal(reused, prepared.Tracks.Count(t => t.Action == ProcessingActionIds.Reuse));
        if (scenario == "all")
        {
            Assert.True(mainStore.Read(project)!.AacPlan!.ForceEncodeAll);
            Assert.All(prepared.Tracks, t => Assert.Equal(ProcessingActionIds.Convert, t.Action));
            var copyCandidate = options.Optimized.Tracks.Single(t => t.Reuse);
            var sourcePackets = await PacketHashes(tracks[copyCandidate.Occurrence].FilePath);
            var encodedPackets = await PacketHashes(prepared.Tracks[copyCandidate.Occurrence].ConvertedPath);
            Assert.NotEqual(sourcePackets, encodedPackets);
        }
        var ids = prepared.Tracks.Select(t => t.PreparationId).ToArray();
        AacDecisionPlanner.Apply(mainStore.Read(project)!.AacPlan!, tracks);
        var result = await workflow.RunAsync(request with { ManualMergeRequested = true }, callbacks, default);
        Assert.True(result.Status == ExportWorkflowResultStatus.Completed, result.Error?.ToString());
        Assert.Equal(ids, store.ReadOnly(exportPlan.ManifestPath).Tracks.Select(t => t.PreparationId));
        if (scenario == "all")
        {
            var mono = ExportPreset.Parse("AAC Mono 64 kbps");
            var changedOptions = await new AacDecisionPreflightService().AnalyzeAsync(tracks, folder.Path, mono,
                44100, tools.FfmpegPath, tools.FfprobePath, default, fixedRate: accepted.SampleRateHz,
                forceEncodeAll: mainStore.Read(project)!.AacPlan!.ForceEncodeAll);
            var changed = AacDecisionSelectionService.Select(changedOptions, review: true, forceEncodeAll: true);
            Assert.False(changed.NeedsDialog); Assert.True(changed.Plan.ForceEncodeAll); Assert.Equal(0, changed.Plan.ReusedCount);
            mainStore.SavePlan(project, changed.Plan); AacDecisionPlanner.Apply(changed.Plan, tracks);
            var monoPlan = new ExportPlanService().Create(new ExportPlanRequest(tracks, folder.Path, folder.Path,
                Path.Combine(folder.Path, "mono.m4b"), mono.DisplayName, 2, project));
            var monoReview = await workflow.RunAsync(request with { Plan = monoPlan,
                Snapshot = snapshot with { SelectedExportPreset = mono.DisplayName } }, callbacks, default);
            Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, monoReview.Status);
            var monoEntries = store.ReadOnly(monoPlan.ManifestPath).Tracks.Where(t => t.Preset == mono.DisplayName).ToArray();
            Assert.Equal(2, monoEntries.Length);
            foreach (var entry in monoEntries)
            {
                Assert.Equal(ProcessingActionIds.Convert, entry.Action);
                Assert.Equal(1, (await new AudioInfoService().ProbeAsync(entry.ConvertedPath, tools.FfprobePath)).Channels);
            }
        }
        for (var i = 0; i < tracks.Count; i++) Assert.Equal(sourceBytes[i], await File.ReadAllBytesAsync(tracks[i].FilePath));
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);

        async Task<string> PacketHashes(string path)
        {
            var packets = await new FfprobeRunnerService().RunAsync(tools.FfprobePath,
                ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "csv=p=0", path], TimeSpan.FromMinutes(1), default);
            Assert.Equal(0, packets.ExitCode); return packets.Stdout;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Decision_merge_safety_falls_back_only_for_a_confirmed_regular_profile(bool original)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.m4a");
        var output = Path.Combine(folder.Path, "prepared.m4a");
        foreach (var (path, rate) in new[] { (source, 44100), (output, 48000) })
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
                $"anoisesrc=sample_rate={rate}:seed=42", "-t", "3", "-ac", "2", "-c:a", "aac", "-b:a", original ? "73k" : "128k", path]);
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, FileName = "source.m4a", Extension = ".m4a" };
        var options = await new AacDecisionPreflightService().AnalyzeAsync([track], folder.Path, ExportPreset.Parse("AAC Stereo 128 kbps"),
            44100, tools.FfmpegPath, tools.FfprobePath, default);
        var plan = options.Optimized; Assert.Equal(original, plan.IsOriginal);
        AacDecisionPlanner.Apply(plan, [track]);
        var projectStore = new FolderProjectManifestService(); projectStore.Create(folder.Path, folder.Path); projectStore.SavePlan(folder.Path, plan);
        var workStore = new WorkManifestService(); var manifestPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var work = workStore.LoadOrCreate(manifestPath, ProjectManifestTypes.FolderProject, folder.Path, folder.Path, plan.EffectivePreset);
        var audio = new AacExportProcessingService();
        await using var lifecycle = new PreparedTrackLifecycleSession(workStore, audio, work, manifestPath, new object(), tools.FfprobePath, 1);
        lifecycle.MarkStarted(0, track, source, output, plan.Preset, default);
        lifecycle.Complete(0, track, source, output, plan.Preset, default);
        await lifecycle.DrainAsync();
        var originalSource = await File.ReadAllBytesAsync(source);
        var preparedBytes = await File.ReadAllBytesAsync(output);
        var items = new[] { new ConvertedTrackPreparationPlanItem(0, track, source, output, true) };
        if (original)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => AacMergeSafetyService.ValidateOrFallbackAsync(audio,
                folder.Path, items, plan.Preset, 44100, tools.FfmpegPath, tools.FfprobePath, lifecycle, default));
            Assert.Equal(preparedBytes, await File.ReadAllBytesAsync(output));
            Assert.Equal(0, projectStore.Read(folder.Path)!.AacPlan!.ConvertedCount);
        }
        else
        {
            Assert.True(await AacMergeSafetyService.ValidateOrFallbackAsync(audio, folder.Path, items, plan.Preset, 44100,
                tools.FfmpegPath, tools.FfprobePath, lifecycle, default));
            var saved = projectStore.Read(folder.Path)!.AacPlan!;
            Assert.Equal("SafetyFallback", saved.Decision); Assert.Equal(1, saved.ConvertedCount);
            Assert.Equal(AacConversionReason.Technical, Assert.Single(saved.Tracks).Reason);
            Assert.Equal(44100, (await new AudioInfoService().ProbeAsync(output, tools.FfprobePath)).SampleRateHz);
        }
        Assert.Equal(originalSource, await File.ReadAllBytesAsync(source));
    }

    [Fact]
    public async Task Decision_preflight_remuxes_adts_and_merges_original_mode_without_encoding()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "original.aac");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
            "anoisesrc=sample_rate=44100:seed=42", "-t", "3", "-ac", "2", "-c:a", "aac", "-b:a", "73k", "-f", "adts", source]);
        var bytes = await File.ReadAllBytesAsync(source);
        var tracks = new[] { new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, FileName = "original.aac", Extension = ".aac" } };
        var options = await new AacDecisionPreflightService().AnalyzeAsync(tracks, folder.Path,
            ExportPreset.Parse("AAC Stereo 128 kbps"), 48000, tools.FfmpegPath, tools.FfprobePath, default);
        Assert.True(options.Optimized.IsOriginal);
        Assert.Equal(0, options.Optimized.ConvertedCount);
        Assert.Equal(48000, options.UserValues.SampleRateHz);
        Assert.Equal(1, options.UserValues.ConvertedCount);
        AacDecisionPlanner.Apply(options.Optimized, tracks);
        var prepared = Path.Combine(folder.Path, "prepared.m4a");
        var audio = new AacExportProcessingService();
        await audio.PrepareTrackForExportAsync(tracks[0], source, prepared, options.Optimized.Preset,
            tools.FfmpegPath, default, _ => { }, ffprobePath: tools.FfprobePath);
        await audio.ValidateMergeInputsAsync([prepared], 44100, tools.FfprobePath, default, options.Optimized.Preset);
        var rawPackets = await new FfprobeRunnerService().RunAsync(tools.FfprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "csv=p=0", source], TimeSpan.FromMinutes(1), default);
        var copiedPackets = await new FfprobeRunnerService().RunAsync(tools.FfprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "csv=p=0", prepared], TimeSpan.FromMinutes(1), default);
        Assert.Equal(0, rawPackets.ExitCode); Assert.Equal(0, copiedPackets.ExitCode);
        Assert.NotEmpty(copiedPackets.Stdout);
        var decodedSource = await new FfmpegRunnerService().RunAsync(tools.FfmpegPath,
            ["-v", "error", "-xerror", "-i", source, "-map", "0:a:0", "-f", "hash", "-hash", "sha256", "-"], default);
        var decodedCopy = await new FfmpegRunnerService().RunAsync(tools.FfmpegPath,
            ["-v", "error", "-xerror", "-i", prepared, "-map", "0:a:0", "-f", "hash", "-hash", "sha256", "-"], default);
        Assert.Equal(0, decodedSource.ExitCode); Assert.Equal(0, decodedCopy.ExitCode);
        Assert.Equal(decodedSource.Stdout, decodedCopy.Stdout);
        var concat = Path.Combine(folder.Path, "concat.txt");
        await File.WriteAllLinesAsync(concat, [$"file '{prepared.Replace("\\", "/")}'", $"file '{prepared.Replace("\\", "/")}'"]);
        var metadata = Path.Combine(folder.Path, "chapters.txt");
        await File.WriteAllTextAsync(metadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=3000\ntitle=Original\n");
        var output = Path.Combine(folder.Path, "result.m4b");
        await audio.MergeConvertedTracksAsync(concat, metadata, output, 6 * TimeSpan.TicksPerSecond, tools.FfmpegPath, default, _ => { });
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(source));
        var result = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
        Assert.Equal(44100, result.SampleRateHz); Assert.Single(result.Chapters);
    }

    [Fact]
    public async Task Decision_preflight_calibrates_mixed_plan_and_prepares_compatible_streams()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var tracks = new List<TrackInfo>();
        foreach (var (rate, bitrate, seconds) in new[] { (22050, 96, 6), (48000, 128, 1) })
        {
            var file = Path.Combine(folder.Path, $"{rate}.m4a");
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i",
                $"anoisesrc=sample_rate={rate}:seed=42", "-t", seconds.ToString(), "-ac", "2", "-c:a", "aac", "-b:a", $"{bitrate}k", file]);
            tracks.Add(new TrackInfo { SourceId = Guid.NewGuid(), FilePath = file, FileName = Path.GetFileName(file), Extension = ".m4a" });
        }
        var options = await new AacDecisionPreflightService().AnalyzeAsync(tracks, folder.Path,
            ExportPreset.Parse("AAC Stereo 128 kbps"), 44100, tools.FfmpegPath, tools.FfprobePath, default);
        Assert.Equal(22050, options.Optimized.SampleRateHz); Assert.Equal(96, options.Optimized.BitrateKbps);
        Assert.True(options.Optimized.Tracks[0].Reuse); Assert.False(options.Optimized.Tracks[1].Reuse);
        AacDecisionPlanner.Apply(options.Optimized, tracks);
        var audio = new AacExportProcessingService(); var paths = new List<string>();
        foreach (var track in tracks)
        {
            var path = Path.Combine(folder.Path, $"prepared-{paths.Count}.m4a"); paths.Add(path);
            await audio.PrepareTrackForExportAsync(track, track.FilePath, path, options.Optimized.Preset,
                tools.FfmpegPath, default, _ => { }, ffprobePath: tools.FfprobePath);
        }
        await audio.ValidateMergeInputsAsync(paths, 22050, tools.FfprobePath, default, options.Optimized.Preset);
        foreach (var path in paths) await ConvertedAacTimelineService.AnalyzeAsync(path, tools.FfprobePath, null, default);
    }
}
