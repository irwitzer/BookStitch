using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackStateUpdateQueueServiceTests
{
    [Fact]
    public void Probed_mp3_bitrate_is_not_replaced_by_container_size_including_cover_art()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "covered.mp3");
            File.WriteAllBytes(path, new byte[1000000]);
            var track = new TrackInfo { FilePath = path, Codec = "MP3", BitrateKbps = 128,
                SampleRateHz = 44100, DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                AudioValidationPassed = true };
            TrackStateUpdateQueueService.ApplyUpdates(new([track], root, "AAC Mono 64 kbps", false));
            Assert.Equal(128, track.BitrateKbps);
            Assert.Equal(44100, track.SampleRateHz);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true, 128)]
    [InlineData(false, 128)]
    public void Source_bitrate_survives_container_size_refresh_before_and_after_validation(bool verified, int expected)
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "covered.m4a");
            File.WriteAllBytes(path, new byte[1000000]);
            var track = new TrackInfo { FilePath = path, Extension = ".m4a", Codec = "AAC",
                Channels = 2, SampleRateHz = 48000, TargetSampleRateHz = 48000,
                DurationTicks = TimeSpan.FromSeconds(10).Ticks, BitrateKbps = 128,
                AudioValidationPassed = verified, AacOptimizationEligible = verified };
            var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
            var before = FolderSampleRateService.DecisionKey([track], preset, 44100);
            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext([track], root, preset.DisplayName, false));
            Assert.Equal(expected, track.BitrateKbps);
            if (verified)
            {
                Assert.Equal(before, FolderSampleRateService.DecisionKey([track], preset, 44100));
                Assert.True(FolderSampleRateService.Decide([track], preset, 44100).FullPassThrough);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Active_ticks_and_request_bursts_share_the_five_second_safety_interval()
    {
        var clock = new RefreshClock();
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var context = new TrackStateUpdateContext([], "", "AAC Mono 64 kbps", true);
        using var queue = new TrackStateUpdateQueueService(() => context, () => { }, trace: trace, timeProvider: clock);
        Tick(queue);
        for (var index = 0; index < 6; index++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(750));
            for (var request = 0; request < 20; request++) queue.RequestRefresh();
            Tick(queue);
        }
        Assert.Equal(1, RefreshCount(trace, "track-state.refresh.active"));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Tick(queue);
        Assert.Equal(2, RefreshCount(trace, "track-state.refresh.active"));
        clock.Advance(TimeSpan.FromSeconds(5));
        Tick(queue); // Safety backstop also runs without a pending request.
        Assert.Equal(3, RefreshCount(trace, "track-state.refresh.active"));
        context = context with { IsWorkflowActive = false };
        Tick(queue); // Final refresh ignores the active interval.
        Tick(queue);
        Assert.Equal(1, RefreshCount(trace, "track-state.refresh.requested-or-final"));
        queue.RequestRefresh();
        Tick(queue);
        Assert.Equal(2, RefreshCount(trace, "track-state.refresh.requested-or-final"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_requests_and_active_to_idle_final_refresh_survive_suspend_or_merge(bool merge)
    {
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var context = new TrackStateUpdateContext([], "", "AAC Mono 64 kbps", true);
        using var queue = new TrackStateUpdateQueueService(() => context, () => { }, trace: trace, timeProvider: new RefreshClock());
        Tick(queue);
        context = context with { IsRefreshSuspended = !merge,
            PipelineState = merge ? ProjectPipelineState.Merging : ProjectPipelineState.Converting };
        queue.RequestRefresh();
        Tick(queue);
        context = context with { IsWorkflowActive = false };
        Tick(queue);
        context = context with { IsRefreshSuspended = false, PipelineState = ProjectPipelineState.ReviewBeforeMerge };
        Tick(queue);
        Tick(queue);
        Assert.Equal(1, RefreshCount(trace, "track-state.refresh.active"));
        Assert.Equal(1, RefreshCount(trace, "track-state.refresh.requested-or-final"));
    }

    [Fact]
    public void Large_active_workload_scans_less_than_a_quarter_of_previous_tick_polling()
    {
        var clock = new RefreshClock();
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var tracks = Enumerable.Range(0, 1308).Select(index => new TrackInfo { Index = index }).ToArray();
        using var queue = new TrackStateUpdateQueueService(
            () => new(tracks, "", "AAC Mono 64 kbps", true), () => { }, trace: trace, timeProvider: clock);
        for (var tick = 0; tick < 125; tick++)
        {
            queue.RequestRefresh();
            Tick(queue);
            clock.Advance(TimeSpan.FromMilliseconds(750));
        }
        Assert.Equal(18, RefreshCount(trace, "track-state.refresh.active"));
        var refresh = trace.GetCurrentSnapshot()!.Operations.Single(item => item.Name == "track-state.refresh.active");
        Assert.Equal(18 * 1308, refresh.Metrics.Single(item => item.Name == "TracksChecked").Value);
        Assert.Equal(Enumerable.Range(0, 1308), tracks.Select(track => track.Index));
    }

    private static double RefreshCount(PerformanceTraceService trace, string name) =>
        trace.GetCurrentSnapshot()!.Operations.Single(item => item.Name == name).Metrics.Single(item => item.Name == "Count").Value;

    private static void Tick(TrackStateUpdateQueueService queue) =>
        typeof(TrackStateUpdateQueueService).GetMethod("Timer_Tick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(queue, [null, EventArgs.Empty]);

    private sealed class RefreshClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Refresh_diagnostics_observe_prepared_reads_without_changing_track_state(bool enabled)
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "source.mp3");
            var converted = Path.Combine(root, "converted.m4a");
            File.WriteAllText(source, "source");
            File.WriteAllText(converted, "converted");
            const string preset = "AAC Mono 64 kbps";
            WriteManifest(root, source, converted, preset, "Converted", "export-project.json");
            var before = File.ReadAllBytes(Path.Combine(root, "export-project.json"));
            var track = new TrackInfo { FilePath = source, FileName = "source.mp3", Index = 8, ProcessingAction = ProcessingActionIds.Convert };
            var trace = new PerformanceTraceService(enabled);
            trace.TryStartSession(new());
            Assert.True(TrackStateUpdateQueueService.ApplyUpdates(new([track], root, preset, false), trace));
            Assert.Equal(8, track.Index);
            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal(converted, track.PreparedConvertedPath);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "export-project.json")));
            var report = trace.CompleteSession(PerformanceOutcome.Success);
            if (!enabled) { Assert.Null(report); return; }
            var refresh = report!.Operations.Single(item => item.Name == "track-state.refresh.requested-or-final");
            var counters = refresh.Metrics.ToDictionary(item => item.Name, item => item.Value);
            Assert.Equal(2, counters["ManifestReads"]);
            Assert.Equal(1, counters["PreparedStateReads"]);
            Assert.Equal(2, counters["SourceFileChecks"]);
            Assert.Equal(1, counters["ConvertedFileChecks"]);
            Assert.Equal(1, counters["PreparedFileChecks"]);
            Assert.Equal(1, counters["ChangedCycles"]);
            Assert.Contains(report.Operations, item => item.Name == "manifest.read");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Diagnostics_count_executed_refreshes_and_ignore_idle_and_suspended_ticks()
    {
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var track = new TrackInfo { FilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3"), Index = 7 };
        var context = new TrackStateUpdateContext([track], "", "AAC Mono 64 kbps", false);
        using var queue = new TrackStateUpdateQueueService(() => context, () => { }, trace: trace);
        var tick = typeof(TrackStateUpdateQueueService).GetMethod("Timer_Tick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        for (var index = 0; index < 10; index++) tick.Invoke(queue, [null, EventArgs.Empty]);
        Assert.Empty(trace.GetCurrentSnapshot()!.Operations);
        queue.RequestRefresh();
        context = context with { IsRefreshSuspended = true };
        tick.Invoke(queue, [null, EventArgs.Empty]);
        Assert.Empty(trace.GetCurrentSnapshot()!.Operations);
        context = context with { IsRefreshSuspended = false };
        tick.Invoke(queue, [null, EventArgs.Empty]);
        context = context with { IsWorkflowActive = true };
        tick.Invoke(queue, [null, EventArgs.Empty]);
        var aggregates = trace.CompleteSession(PerformanceOutcome.Success)!.Operations;
        Assert.Equal(2, aggregates.Count);
        var requested = aggregates.Single(item => item.Name == "track-state.refresh.requested-or-final");
        var active = aggregates.Single(item => item.Name == "track-state.refresh.active");
        Assert.Contains(requested.Metrics, item => item.Name == "SourceFileChecks" && item.Value == 1);
        Assert.Contains(active.Metrics, item => item.Name == "SourceFileChecks" && item.Value == 2);
        Assert.All(aggregates, item => Assert.Contains(item.Metrics, metric => metric.Name == "TracksChecked" && metric.Value == 1));
        Assert.Equal(7, track.Index);
        Assert.Equal(context.Tracks[0].FilePath, track.FilePath);
    }

    [Fact]
    public void Merge_ticks_defer_requested_scans_until_workflow_leaves_merge()
    {
        var track = new TrackInfo { SizeMb = 123, SourceSizeAvailable = true };
        var phase = ProjectPipelineState.Merging;
        var notifications = 0;
        using var queue = new TrackStateUpdateQueueService(
            () => new TrackStateUpdateContext([track], "", "AAC Mono 64 kbps", true, PipelineState: phase),
            () => notifications++);
        var tick = typeof(TrackStateUpdateQueueService).GetMethod("Timer_Tick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        queue.RequestRefresh();
        for (var i = 0; i < 4; i++) tick.Invoke(queue, [null, EventArgs.Empty]);
        Assert.Equal(123, track.SizeMb);
        Assert.True(track.SourceSizeAvailable);
        Assert.Equal(0, notifications);
        phase = ProjectPipelineState.ReviewBeforeMerge;
        tick.Invoke(queue, [null, EventArgs.Empty]);
        Assert.Equal(0, track.SizeMb);
        Assert.False(track.SourceSizeAvailable);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Constructor_RejectsNullContextProvider()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TrackStateUpdateQueueService(
                null!,
                () => { }));
    }

    [Fact]
    public void Constructor_RejectsNullTracksChangedCallback()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TrackStateUpdateQueueService(
                () => new TrackStateUpdateContext(
                    Array.Empty<TrackInfo>(),
                    string.Empty,
                    string.Empty,
                    false),
                null!));
    }

    [Fact]
    public void ApplyUpdates_RefreshesSourceAndConvertedSizesForSelectedPreset()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[2 * 1024 * 1024]);
            File.WriteAllBytes(convertedPath, new byte[512 * 1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            var changed = TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.True(changed);
            Assert.Equal(2d, track.SizeMb);
            Assert.True(track.SourceSizeAvailable);
            Assert.Equal("2.00", track.DisplaySizeMb);
            Assert.True(track.ConvertedSizeAvailable);
            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal("Wiederverwenden", track.DisplayProcessingAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_IgnoresConvertedFileFromDifferentPreset()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Stereo 128 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.False(track.ConvertedSizeAvailable);
            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal("Bereit", track.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_UsesPartFileSizeWhileConversionIsRunning()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[256 * 1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.True(track.ConvertedSizeAvailable);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_LeavesMissingSizesBlank()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.flac"),
                ProcessingAction = "FLAC rippen",
                Warning = "Noch nicht gerippt"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.False(track.SourceSizeAvailable);
            Assert.Equal("", track.DisplaySizeMb);
            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("Noch nicht gerippt", track.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ShowsAndWarnsAboutExistingZeroByteFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, Array.Empty<byte>());
            File.WriteAllBytes(convertedPath, Array.Empty<byte>());
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "FLAC rippen"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.True(track.SourceSizeAvailable);
            Assert.Equal("0.00", track.DisplaySizeMb);
            Assert.True(track.ConvertedSizeAvailable);
            Assert.Contains("Quelldatei ist leer", track.Warning);
            Assert.Contains("AAC-Datei ist leer", track.Warning);
            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal(ProcessingActionIds.RipFlac, ProcessingActionIds.Normalize(track.ProcessingAction));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void ApplyUpdates_RefreshesCompletedAudioDiscSourceMetadata()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            File.WriteAllBytes(sourcePath, new byte[1024 * 1024]);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                DiscNumber = 1,
                Duration = "00:10",
                ProcessingAction = "FLAC rippen"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.Equal(".flac", track.Extension);
            Assert.Equal("FLAC", track.Codec);
            Assert.Equal(2, track.Channels);
            Assert.Equal("Stereo", track.ChannelLayout);
            Assert.Equal(839, track.BitrateKbps);
            Assert.Equal(ProcessingActionIds.Convert, track.ProcessingAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_DoesNotOverwriteKnownOpusCodecForOggContainer()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "opus-in-ogg.ogg");
            File.WriteAllBytes(sourcePath, new byte[1024]);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                Codec = "OPUS",
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.Equal(".ogg", track.Extension);
            Assert.Equal("OPUS", track.Codec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ClearsRemovedPartFileAndStalePreparedState()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[128 * 1024]);
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren",
                PreparedConvertedPath = convertedPath,
                PreparedConvertedPreset = "AAC Mono 64 kbps"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));
            Assert.True(track.ConvertedSizeAvailable);

            File.Delete(convertedPath + ".part");
            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("", track.PreparedConvertedPath);
            Assert.Equal("", track.PreparedConvertedPreset);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void ApplyUpdates_IgnoresLingeringPartFileWhenWorkflowIsIdle()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[256 * 1024]);
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren",
                ConvertedSizeAvailable = true
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.False(track.ConvertedSizeAvailable);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ClearsMeasuredMetadataWhenLocalSourceIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.mp3"),
                ProcessingAction = "Konvertieren",
                BitrateKbps = 192,
                SampleRateHz = 44100,
                Channels = 2,
                ChannelLayout = "Stereo",
                Codec = "MP3"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.Null(track.BitrateKbps);
            Assert.Null(track.SampleRateHz);
            Assert.Null(track.Channels);
            Assert.Equal("", track.ChannelLayout);
            Assert.Equal("MP3", track.Codec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_PreservesExpectedAudioDiscChannelMetadataBeforeRip()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.flac"),
                DiscNumber = 1,
                ProcessingAction = "FLAC rippen",
                BitrateKbps = 1411,
                Channels = 2,
                ChannelLayout = "Stereo",
                Codec = "FLAC"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.Null(track.BitrateKbps);
            Assert.Equal(2, track.Channels);
            Assert.Equal("Stereo", track.ChannelLayout);
            Assert.Equal("FLAC", track.Codec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_UsesExportManifestForMp3DiscProject()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            var convertedPath = Path.Combine(root, "converted", "aac", "track.m4a");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[2048]);

            File.WriteAllText(
                Path.Combine(root, "project.json"),
                JsonSerializer.Serialize(new Mp3DiscProjectManifest
                {
                    ProjectFolder = root,
                    TotalDiscs = 1
                }));

            WriteManifest(root, sourcePath, convertedPath, "AAC Stereo 160 kbps", "Converted", "export-project.json");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Stereo 160 kbps", false));

            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal(convertedPath, track.PreparedConvertedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_DoesNotTreatUntrackedFileAsPrepared()
    {
        var root = CreateTempDirectory();
        try
        {
            const string presetName = "AAC Stereo 160 kbps";
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = "track.mp3",
                ProcessingAction = "Konvertieren"
            };

            var preset = ExportPreset.Parse(presetName);
            var convertedFolder = Path.Combine(root, "converted", preset.GetFolderName());
            Directory.CreateDirectory(convertedFolder);
            var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
            File.WriteAllBytes(convertedPath, new byte[2048]);

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, presetName, false));

            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal("Bereit", track.Status);
            Assert.Equal("Konvertieren", track.DisplayProcessingAction);
            Assert.Empty(track.PreparedConvertedPath);
            Assert.Empty(track.PreparedConvertedPreset);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_DoesNotPromoteUncommittedFinalFileToPreparedState()
    {
        var root = CreateTempDirectory();
        try
        {
            const string presetName = "AAC Stereo 160 kbps";
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            var convertedPath = Path.Combine(root, "converted", "track.m4a");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[2048]);
            WriteManifest(root, sourcePath, convertedPath, presetName, "Converting", "export-project.json");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = "track.mp3",
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, presetName, false));

            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal("Bereit", track.Status);
            Assert.Empty(track.PreparedConvertedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteManifest(
        string root,
        string sourcePath,
        string convertedPath,
        string preset,
        string status,
        string fileName = "project.json")
    {
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    SourceSizeBytes = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0,
                    SourceLastWriteUtcTicks = File.Exists(sourcePath) ? File.GetLastWriteTimeUtc(sourcePath).Ticks : 0,
                    ConvertedSizeBytes = File.Exists(convertedPath) ? new FileInfo(convertedPath).Length : 0,
                    ConvertedLastWriteUtcTicks = File.Exists(convertedPath) ? File.GetLastWriteTimeUtc(convertedPath).Ticks : 0,
                    PreparationVersion = 1, PreparationId = Guid.NewGuid(),
                    SourcePath = sourcePath,
                    Preset = preset,
                    Status = status,
                    ConvertedPath = convertedPath
                }
            ]
        };

        File.WriteAllText(
            Path.Combine(root, fileName),
            JsonSerializer.Serialize(manifest));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
