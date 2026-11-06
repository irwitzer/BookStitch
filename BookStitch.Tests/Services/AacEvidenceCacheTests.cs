using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Services;

[CollectionDefinition("AAC evidence diagnostics", DisableParallelization = true)]
public sealed class AacEvidenceCollection { }

[Collection("AAC evidence diagnostics")]
public sealed class AacEvidenceCacheTests
{
    private static AudioProbeInfo Evidence(string path) => new() { FilePath = path, Success = true, CodecName = "aac",
        CodecType = "audio", AudioStreamCount = 1, AudioProfile = "LC", SampleFormat = "fltp", Channels = 2,
        AudioChannelLayout = "stereo", SampleRateHz = 44100, AudioTimeBase = "1/44100", AacExtradataSize = 5,
        AacConfigurationId = "configuration", Duration = TimeSpan.FromSeconds(3), DurationPrecision = "Stream",
        AudioBitrateBitsPerSecond = 128000 };

    [Fact]
    public async Task Concurrent_references_share_one_complete_probe_and_persist_without_mutable_aliases()
    {
        using var folder = new TemporaryFolder(); var path = folder.CreateFile("source.m4a");
        var cache = new AudioEvidenceCache(); var calls = 0;
        async Task<AudioProbeInfo> Probe() { Interlocked.Increment(ref calls); await Task.Delay(30); return Evidence(path); }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => cache.GetOrProbeAsync(path, "version", Probe, default)));
        Assert.Equal(1, calls); results[0].SampleRateHz = 123;
        Assert.True(cache.TryGet(path, "version", out var saved)); Assert.Equal(44100, saved!.Probe.SampleRateHz);
        cache.Save(folder.Path); var loaded = AudioEvidenceCache.Load(folder.Path);
        await loaded.GetOrProbeAsync(path, "version", Probe, default); Assert.Equal(1, calls);
        Assert.False(loaded.TryGet(path, "different toolchain or schema", out _));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, nint reserved);

    [Fact]
    public async Task Physical_file_aliases_share_analysis_but_each_logical_track_keeps_its_weight()
    {
        using var folder = new TemporaryFolder(); var source = folder.CreateFile("first.m4a");
        var alias = Path.Combine(folder.Path, "second.m4a"); Assert.True(CreateHardLink(alias, source, 0));
        Assert.Equal(AudioEvidenceFileIdentity.Read(source).CacheKey, AudioEvidenceFileIdentity.Read(alias).CacheKey);
        var cache = new AudioEvidenceCache(); var calls = 0;
        async Task<AudioProbeInfo> Probe(string path) { Interlocked.Increment(ref calls); await Task.Delay(10); return Evidence(path); }
        var probes = await Task.WhenAll(new[] { source, alias }.Select(path => cache.GetOrProbeAsync(path, "v", () => Probe(path), default)));
        Assert.Equal(1, calls);
        var tracks = probes.Select(probe =>
        {
            var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = probe.FilePath };
            new TrackAudioProbeApplicationService().ApplySuccessfulProbe(track, probe, ExportPreset.Parse("AAC Stereo 128 kbps"), true);
            return track;
        }).ToArray();
        var plan = AacDecisionPlanner.Build(tracks, ExportPreset.Parse("AAC Stereo 128 kbps"), 44100, new Dictionary<string, string?>());
        Assert.Equal(2, plan.ReusedCount); Assert.Equal(6 * TimeSpan.TicksPerSecond, plan.ReusedDurationTicks);
    }

    [Fact]
    public async Task Source_changes_even_with_restored_size_and_mtime_and_modified_toolchains_invalidate()
    {
        using var folder = new TemporaryFolder(); var path = folder.CreateFile("source.m4a"); File.WriteAllText(path, "AAAA");
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var cache = new AudioEvidenceCache(); await cache.GetOrProbeAsync(path, "v", () => Task.FromResult(Evidence(path)), default);
            var identity = AudioEvidenceFileIdentity.Read(path); var hash = AacToolchainIdentityCache.Identity(path);
            File.WriteAllText(path, iteration.ToString("D4"));
            File.SetLastWriteTimeUtc(path, new DateTime(identity.ModifiedUtc, DateTimeKind.Utc));
            Assert.False(cache.TryGet(path, "v", out _)); Assert.NotEqual(hash, AacToolchainIdentityCache.Identity(path));
        }
    }

    [Fact]
    public async Task Cancellation_failure_and_corrupt_persisted_evidence_are_never_cache_hits()
    {
        using var folder = new TemporaryFolder(); var path = folder.CreateFile("source.m4a"); var cache = new AudioEvidenceCache();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrProbeAsync(path, "v", () =>
        { cancel.Cancel(); return Task.FromResult(Evidence(path)); }, cancel.Token)); Assert.False(cache.HasEntries);
        await cache.GetOrProbeAsync(path, "v", () => Task.FromResult(new AudioProbeInfo()), default); Assert.False(cache.HasEntries);
        var failed = Evidence(path); failed.HasProbeErrors = true;
        await cache.GetOrProbeAsync(path, "v", () => Task.FromResult(failed), default); Assert.False(cache.HasEntries);
        await cache.GetOrProbeAsync(path, "v", () => Task.FromResult(Evidence(path)), default); cache.Save(folder.Path);
        var persisted = Path.Combine(ProjectFolderLayout.GetSettingsFolder(folder.Path), "audio-evidence.json");
        File.WriteAllText(persisted, File.ReadAllText(persisted).Replace("128000", "64000"));
        Assert.False(AudioEvidenceCache.Load(folder.Path).TryGet(path, "v", out _));
    }

    [Theory]
    [InlineData("mp3", 0)] [InlineData("m4a", 1)] [InlineData("aac", 1)]
    public async Task Imported_sources_need_no_regular_reprobe_or_decode_test_and_repeat_without_native_work(string extension, int reuse)
    {
        using var folder = new TemporaryFolder(); var tools = Tools(); var path = Path.Combine(folder.Path, "source." + extension);
        var generated = await new FfmpegRunnerService().RunAsync(tools.Ffmpeg,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=750:sample_rate=44100", "-t", "3", "-ac", "2",
             "-c:a", extension == "mp3" ? "libmp3lame" : "aac", "-b:a", "128k", path], default);
        Assert.Equal(0, generated.ExitCode);
        var cache = new AudioEvidenceCache(); var audio = new AudioInfoService();
        var initial = await audio.ProbeAsync(path, tools.Ffprobe, evidenceCache: cache); Assert.True(initial.Success);
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = path, Extension = "." + extension };
        var previous = PerformanceMeasurement.ApplicationTrace;
        try
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var trace = new PerformanceTraceService(true); trace.TryStartSession(new()); PerformanceMeasurement.ApplicationTrace = trace;
                // Repeated preset changes retain the same analysis, even when a conversion is required.
                var preset = ExportPreset.Parse(iteration == 1 ? "AAC Stereo 96 kbps" : "AAC Stereo 128 kbps");
                var options = await new AacDecisionPreflightService().AnalyzeAsync([track], folder.Path, preset, 44100,
                    tools.Ffmpeg, tools.Ffprobe, default, evidenceCache: cache);
                var report = trace.CompleteSession(PerformanceOutcome.Success)!;
                var operation = Assert.Single(report.Operations, op => op.Name == "aac.preflight");
                double Metric(string name) => operation.Metrics.Single(metric => metric.Name == name).Value;
                Assert.Equal(1, Metric("CacheHits")); Assert.Equal(0, Metric("CacheMisses"));
                Assert.Equal(0, Metric("RoutineDecodeTests")); Assert.Equal(0, Metric("Calibrations"));
                Assert.DoesNotContain(operation.ExternalProcessAggregates, item => item.Purpose == "aac.source.probe");
                Assert.Equal(reuse, options.Optimized.ReusedCount);
                if (extension == "mp3") { Assert.False(AacDecisionSelectionService.Select(options).NeedsDialog); Assert.Equal(1, Metric("ZeroCandidateSkips")); }
                if (extension != "aac" || iteration > 0) Assert.Empty(operation.ExternalProcessAggregates);
                else { Assert.Equal(1, Metric("PacketMeasurements")); Assert.Equal(1, Metric("AdtsRemuxes")); Assert.Equal(0, Metric("FullAdtsRemuxes")); }
                cache.Save(folder.Path); cache = AudioEvidenceCache.Load(folder.Path);
            }
        }
        finally { PerformanceMeasurement.ApplicationTrace = previous; }
    }

    [Fact]
    public async Task Add_sources_probes_only_new_files_and_verified_acquisition_rebinds_without_probe()
    {
        using var folder = new TemporaryFolder(); var cache = new AudioEvidenceCache(); var old = folder.CreateFile("old.m4a");
        var added = folder.CreateFile("added.m4a"); var calls = 0;
        Task<AudioProbeInfo> Probe(string path) { calls++; return Task.FromResult(Evidence(path)); }
        await cache.GetOrProbeAsync(old, "v", () => Probe(old), default);
        await Task.WhenAll(new[] { old, added }.Select(path => cache.GetOrProbeAsync(path, "v", () => Probe(path), default)));
        Assert.Equal(2, calls);
        var id = Guid.NewGuid(); cache.Update(old, "v", Evidence(old), null, id);
        var copy = Path.Combine(folder.Path, "acquired.m4a"); File.Copy(old, copy);
        var acquiredId = Guid.NewGuid();
        cache.RebindVerifiedCopy(old, copy, acquiredId);
        cache.RebindVerifiedCopies([new TrackInfo { SourceId = acquiredId, FilePath = copy }], folder.Path);
        await cache.GetOrProbeAsync(copy, "v", () => Probe(copy), default); Assert.Equal(2, calls);
        cache.Save(folder.Path);
        Assert.True(AudioEvidenceCache.Load(folder.Path).TryGet(copy, "v", out var persisted));
        Assert.Contains(acquiredId, persisted!.SourceIds);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_required_measurements_are_targeted_and_damage_is_a_source_error(bool missingDuration)
    {
        using var folder = new TemporaryFolder(); var tools = Tools(); var path = Path.Combine(folder.Path, "measure.m4a");
        Assert.Equal(0, (await new FfmpegRunnerService().RunAsync(tools.Ffmpeg,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=44100", "-t", "3", "-ac", "2", "-c:a", "aac", "-b:a", "128k", path], default)).ExitCode);
        var cache = new AudioEvidenceCache(); var probe = await new AudioInfoService().ProbeAsync(path, tools.Ffprobe, evidenceCache: cache);
        var probeId = AudioEvidenceCache.ProbeSchemaIdentity + AacToolchainIdentityCache.Identity(tools.Ffprobe);
        probe.AudioBitrateBitsPerSecond = null;
        if (missingDuration) { probe.Duration = null; probe.AudioStreamDuration = null; }
        var id = Guid.NewGuid(); cache.Update(path, probeId, probe, null, id);
        var previous = PerformanceMeasurement.ApplicationTrace;
        try
        {
            var trace = new PerformanceTraceService(true); trace.TryStartSession(new()); PerformanceMeasurement.ApplicationTrace = trace;
            var options = await new AacDecisionPreflightService().AnalyzeAsync([new TrackInfo { SourceId = id, FilePath = path }], folder.Path,
                ExportPreset.Parse("AAC Stereo 128 kbps"), 44100, tools.Ffmpeg, tools.Ffprobe, default, evidenceCache: cache);
            Assert.True(options.Optimized.ReusedDurationTicks > 0);
            var op = Assert.Single(trace.CompleteSession(PerformanceOutcome.Success)!.Operations, item => item.Name == "aac.preflight");
            Assert.Equal(1, op.Metrics.Single(metric => metric.Name == "PacketMeasurements").Value);
            Assert.DoesNotContain(op.ExternalProcessAggregates, item => item.Purpose == "aac.source.probe");
        }
        finally { PerformanceMeasurement.ApplicationTrace = previous; }
        File.WriteAllText(path, "not audio");
        await Assert.ThrowsAsync<InvalidDataException>(() => new AacDecisionPreflightService().AnalyzeAsync(
            [new TrackInfo { SourceId = id, FilePath = path }], folder.Path, ExportPreset.Parse("AAC Stereo 128 kbps"),
            44100, tools.Ffmpeg, tools.Ffprobe, default, evidenceCache: cache));
    }

    [Fact]
    public async Task Mixed_plan_validates_only_a_competitive_profile_and_force_all_does_no_calibration()
    {
        using var folder = new TemporaryFolder(); var tools = Tools(); var cache = new AudioEvidenceCache();
        var tracks = new List<TrackInfo>();
        foreach (var (name, rate, seconds, codec, bitrate) in new[] {
            ("long.m4a", 44100, 8, "aac", "128k"), ("short.m4a", 48000, 3, "aac", "96k"), ("other.mp3", 44100, 3, "libmp3lame", "128k") })
        {
            var path = Path.Combine(folder.Path, name);
            Assert.Equal(0, (await new FfmpegRunnerService().RunAsync(tools.Ffmpeg,
                ["-v", "error", "-y", "-f", "lavfi", "-i", $"anoisesrc=sample_rate={rate}:seed=42", "-t", seconds.ToString(),
                 "-ac", "2", "-c:a", codec, "-b:a", bitrate, path], default)).ExitCode);
            await new AudioInfoService().ProbeAsync(path, tools.Ffprobe, evidenceCache: cache);
            tracks.Add(new TrackInfo { SourceId = Guid.NewGuid(), FilePath = path });
        }
        var previous = PerformanceMeasurement.ApplicationTrace;
        try
        {
            foreach (var force in new[] { false, true })
            {
                var trace = new PerformanceTraceService(true); trace.TryStartSession(new()); PerformanceMeasurement.ApplicationTrace = trace;
                var result = await new AacDecisionPreflightService().AnalyzeAsync(tracks, folder.Path, ExportPreset.Parse("AAC Stereo 128 kbps"),
                    44100, tools.Ffmpeg, tools.Ffprobe, default, forceEncodeAll: force, evidenceCache: cache);
                var op = Assert.Single(trace.CompleteSession(PerformanceOutcome.Success)!.Operations, item => item.Name == "aac.preflight");
                Assert.InRange(op.Metrics.Single(metric => metric.Name == "Calibrations").Value, 0, force ? 0 : 1);
                Assert.Equal(force ? 0 : 1, result.Optimized.ReusedCount);
                if (!force) Assert.Equal(0, result.Optimized.Tracks.Single(item => item.Reuse).Occurrence);
                Assert.DoesNotContain(op.ExternalProcessAggregates, item => item.Purpose == "aac.source.probe");
            }
        }
        finally { PerformanceMeasurement.ApplicationTrace = previous; }
    }

    [Fact]
    public void Main_status_has_no_intervening_profile_row()
    {
        var xaml = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var status = xaml.Descendants().Single(node => (string?)node.Attribute("Text") == "{Binding StatusText}");
        Assert.Equal("Grid", status.Parent!.Name.LocalName);
        Assert.Equal("1", (string?)status.Attribute("Grid.Row"));
        Assert.DoesNotContain("AacEffectiveProfile", xaml.ToString());
        Assert.Contains(xaml.Descendants(), node => node.Attributes().Any(a => a.Value == "MainExportProgress"));
    }

    private static (string Ffmpeg, string Ffprobe) Tools()
    {
        string Find(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder.Trim(), tool + ".exe")).First(File.Exists);
        return (Find("ffmpeg"), Find("ffprobe"));
    }
}
