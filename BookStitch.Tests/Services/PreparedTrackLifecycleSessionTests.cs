using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PreparedTrackLifecycleSessionTests
{
    [Theory]
    [InlineData(ProjectManifestTypes.FolderProject)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    public async Task Concurrent_start_piggybacks_on_completion_but_is_not_reusable_after_reload(string projectType)
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        fixture.Manifest.ProjectType = projectType;
        var unfinished = Path.Combine(folder.Path, "unfinished.m4a");
        var other = new TrackInfo { SourceId = Guid.NewGuid(), Index = 1, FilePath = fixture.Source,
            FileName = "other.mp3", ProcessingAction = ProcessingActionIds.Convert };
        await using var session = fixture.CreateSession();
        session.MarkStarted(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None);
        session.MarkStarted(1, other, fixture.Source, unfinished, fixture.Preset, CancellationToken.None);
        File.WriteAllText(unfinished, "partial audio");
        using var pause = new CancellationTokenSource();
        pause.Cancel();
        var completed = session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, pause.Token);
        var disk = fixture.Store.ReadOnly(fixture.ManifestPath);
        var saved = disk.Tracks.Single(entry => entry.SourceId == fixture.Track.SourceId);
        Assert.Equal(completed.PreparationId, saved.PreparationId);
        Assert.True(WorkManifestService.IsReusableConvertedManifestEntry(saved, fixture.Preset.DisplayName));
        var pending = disk.Tracks.Single(entry => entry.SourceId == other.SourceId);
        Assert.Equal(ProjectManifestTrackStatuses.Converting, pending.Status);
        Assert.False(WorkManifestService.IsReusableConvertedManifestEntry(pending, fixture.Preset.DisplayName));
        var resumed = new ConvertedTrackReconciliationService(fixture.Store).ReconcileTrack(disk, projectType,
            1, other, fixture.Source, unfinished, fixture.Preset);
        Assert.False(resumed.CanReuse);
    }

    [Fact]
    public async Task Starts_remain_in_memory_and_completion_is_the_only_per_track_commit()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var fs = new CountingFileSystem();
        fixture.Store = new WorkManifestService(new AtomicJsonStore(fs));
        fixture.Store.Save(fixture.ManifestPath, fixture.Manifest);
        var initialCommits = fs.Commits;
        using var canceled = new CancellationTokenSource();
        await using var session = fixture.CreateSession();
        var ids = new List<Guid>();
        for (var index = 0; index < 12; index++)
        {
            var target = Path.Combine(folder.Path, $"converted-{index}.m4a");
            session.MarkStarted(index, fixture.Track, fixture.Source, target, fixture.Preset, CancellationToken.None);
            Assert.Equal(initialCommits + index, fs.Commits);
            Assert.Equal(ProjectManifestTrackStatuses.Converting, fixture.Manifest.Tracks.Last().Status);
            Assert.False(session.TryMarkStarted(index, fixture.Track, fixture.Source, target, fixture.Preset, CancellationToken.None));
            // A crash at this boundary leaves only already completed tracks on disk.
            var disk = fixture.Store.ReadOnly(fixture.ManifestPath);
            Assert.Equal(index, disk.Tracks.Count);
            File.WriteAllText(target, "finalized audio");
            canceled.Cancel(); // Completion remains durable even when Pause wins the boundary race.
            var entry = session.Complete(index, fixture.Track, fixture.Source, target, fixture.Preset, canceled.Token);
            ids.Add(entry.PreparationId);
            Assert.Equal(initialCommits + index + 1, fs.Commits);
        }
        var unfinished = Path.Combine(folder.Path, "unfinished.m4a");
        session.MarkStarted(12, fixture.Track, fixture.Source, unfinished, fixture.Preset, CancellationToken.None);
        File.WriteAllText(unfinished, "uncommitted audio");
        var reloaded = fixture.Store.ReadOnly(fixture.ManifestPath);
        Assert.Equal(ids, reloaded.Tracks.Select(entry => entry.PreparationId));
        Assert.All(reloaded.Tracks, entry => Assert.True(WorkManifestService.IsReusableConvertedManifestEntry(entry, fixture.Preset.DisplayName)));
        Assert.DoesNotContain(reloaded.Tracks, entry => entry.ConvertedPath == unfinished);
        Assert.False(session.CanReuse(fixture.Source, unfinished, fixture.Preset));
        var resumed = new ConvertedTrackReconciliationService(fixture.Store).ReconcileTrack(reloaded,
            ProjectManifestTypes.FolderProject, 12, fixture.Track, fixture.Source, unfinished, fixture.Preset);
        Assert.False(resumed.CanReuse); // A finalized-looking file without its completion checkpoint must be prepared again.
        await session.DrainAsync();
        Assert.Equal(initialCommits + 12, fs.Commits);
    }

    [Fact]
    public async Task Pause_after_audio_finalization_finishes_the_critical_checkpoint_without_timing()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probeCalls = 0;
        fixture.Audio.AnalyzeTiming = (path, _) => { probeCalls++; return Task.FromResult(Timing(path)); };
        await using var session = fixture.CreateSession();
        var entry = session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, cancellation.Token);
        await session.DrainAsync();
        var saved = Assert.Single(fixture.Store.ReadOnly(fixture.ManifestPath).Tracks);
        Assert.Equal(entry.PreparationId, saved.PreparationId);
        Assert.True(WorkManifestService.IsReusableConvertedManifestEntry(saved, fixture.Preset.DisplayName));
        Assert.Null(saved.AacTiming);
        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public async Task Thousand_timing_results_use_one_flush_and_disposal_does_not_save_again()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var fs = new CountingFileSystem();
        fixture.Store = new WorkManifestService(new AtomicJsonStore(fs));
        for (var index = 0; index < 1001; index++)
        {
            var converted = Path.Combine(folder.Path, $"prepared-{index}.m4a");
            File.WriteAllText(converted, "prepared");
            fixture.Store.UpdateTrack(fixture.Manifest, index, fixture.Track, fixture.Source, converted, fixture.Preset);
        }
        fixture.Store.Save(fixture.ManifestPath, fixture.Manifest);
        var before = fs.Commits;
        fixture.Audio.AnalyzeTiming = (path, _) => Task.FromResult(Timing(path));
        var session = fixture.CreateSession(40);
        session.QueuePreset(fixture.Preset, CancellationToken.None);
        await session.DrainAsync();
        Assert.Equal(before + 1, fs.Commits);
        Assert.All(fixture.Store.ReadOnly(fixture.ManifestPath).Tracks, entry => Assert.NotNull(entry.AacTiming));
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.Equal(before + 1, fs.Commits);
    }

    [Fact]
    public async Task Timing_persistence_failure_preserves_checkpoint_and_can_resume_without_conversion()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var fs = new CountingFileSystem();
        fixture.Store = new WorkManifestService(new AtomicJsonStore(fs));
        fixture.Audio.AnalyzeTiming = (path, _) => Task.FromResult(Timing(path));
        var session = fixture.CreateSession();
        var id = session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None).PreparationId;
        var bytes = File.ReadAllBytes(fixture.ManifestPath);
        fs.Fail = true;
        await session.DrainAsync();
        await session.DisposeAsync();
        Assert.Equal(bytes, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Null(Assert.Single(fixture.Manifest.Tracks).AacTiming);
        fs.Fail = false;
        fixture.Manifest = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        Assert.Equal(id, Assert.Single(fixture.Manifest.Tracks).PreparationId);
        Assert.True(session.CanReuse(fixture.Source, fixture.Converted, fixture.Preset));
        await using var resumed = fixture.CreateSession();
        resumed.QueuePreset(fixture.Preset, CancellationToken.None);
        await resumed.DrainAsync();
        Assert.Equal(id, Assert.Single(fixture.Store.ReadOnly(fixture.ManifestPath).Tracks).PreparationId);
        Assert.NotNull(Assert.Single(fixture.Manifest.Tracks).AacTiming);
    }

    [Fact]
    public async Task Observed_child_fault_is_not_rethrown_by_later_drain_or_dispose()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var session = fixture.CreateSession();
        // Simulate an unexpected infrastructure fault, beyond normal handled probe failures.
        var tasks = (List<Task>)typeof(PreparedTrackLifecycleSession).GetField("_tasks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        tasks.Add(Task.FromException(new IOException("child failure")));
        await Assert.ThrowsAsync<IOException>(() => session.DrainAsync());
        await session.DrainAsync();
        await session.DisposeAsync();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Failed_critical_checkpoint_stops_preparation_and_preserves_last_saved_json()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var fs = new CountingFileSystem();
        fixture.Store = new WorkManifestService(new AtomicJsonStore(fs));
        fixture.Store.Save(fixture.ManifestPath, fixture.Manifest);
        var before = File.ReadAllBytes(fixture.ManifestPath);
        fs.Fail = true;
        var probeCalls = 0;
        fixture.Audio.AnalyzeTiming = (path, _) => { probeCalls++; return Task.FromResult(Timing(path)); };
        var session = fixture.CreateSession();
        var error = Assert.Throws<PreparedTrackCheckpointException>(() => session.Complete(0, fixture.Track,
            fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None));
        Assert.Contains(LocalizationText.Get("Persistence.Manifest.CheckpointFailed"), error.Message);
        Assert.Equal(before, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Equal(0, probeCalls);
        await session.DisposeAsync();
    }

    private sealed class CountingFileSystem : IAtomicJsonStoreFileSystem
    {
        public int Commits;
        public bool Fail;
        public Stream CreateNewWriteThroughFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        public bool FileExists(string path) => File.Exists(path);
        public void Move(string source, string target) => Commit(source, target);
        public void Replace(string source, string target) => Commit(source, target);
        private void Commit(string source, string target)
        {
            if (Fail) throw new IOException("permanent persistence failure");
            File.Move(source, target, overwrite: true);
            Commits++;
        }
        public void DeleteIfExists(string path) => File.Delete(path);
    }

    [Fact]
    public async Task Disposal_still_settles_children_when_a_cancellation_callback_throws()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var session = fixture.CreateSession();
        var stop = (CancellationTokenSource)typeof(PreparedTrackLifecycleSession).GetField("_stop",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        using var callback = stop.Token.Register(() => throw new IOException("callback failure"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = (List<Task>)typeof(PreparedTrackLifecycleSession).GetField("_tasks",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
        tasks.Add(release.Task);
        var disposal = session.DisposeAsync().AsTask();
        await Task.Delay(30);
        Assert.False(disposal.IsCompleted);
        release.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Late_timing_from_an_independent_old_owner_cannot_overwrite_a_replacement_checkpoint()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        fixture.Manifest = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audio.AnalyzeTiming = async (path, _) => { started.SetResult(); await release.Task; return Timing(path); };
        await using var session = fixture.CreateSession();
        session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        var replacement = fixture.Store.UpdateTrack(newer, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        fixture.Store.Save(fixture.ManifestPath, newer);
        var bytes = File.ReadAllBytes(fixture.ManifestPath);
        release.SetResult();
        await session.DrainAsync();
        Assert.Equal(bytes, File.ReadAllBytes(fixture.ManifestPath));
        Assert.Equal(replacement.PreparationId, Assert.Single(fixture.Store.ReadOnly(fixture.ManifestPath).Tracks).PreparationId);
        Assert.Null(Assert.Single(fixture.Manifest.Tracks).AacTiming);
    }

    [Fact]
    public async Task Converted_checkpoint_is_durable_before_timing_and_replaced_identity_rejects_late_result()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid analyzedId = Guid.Empty;
        fixture.Audio.AnalyzeTiming = async (path, token) =>
        {
            var saved = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
            var entry = Assert.Single(saved.Tracks);
            Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
            Assert.NotEqual(Guid.Empty, entry.PreparationId);
            Assert.Null(entry.AacTiming);
            analyzedId = entry.PreparationId;
            started.TrySetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return Timing(path);
        };
        await using var session = fixture.CreateSession();
        session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Store.UpdateTrack(fixture.Manifest, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        var replacement = Assert.Single(fixture.Manifest.Tracks);
        Assert.NotEqual(analyzedId, replacement.PreparationId);
        finish.SetResult();
        await session.DrainAsync();
        Assert.Null(replacement.AacTiming);
    }

    [Fact]
    public async Task Timing_failure_preserves_reuse_and_resume_enqueues_only_missing_timing()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        fixture.Audio.AnalyzeTiming = (_, _) => throw new InvalidDataException("probe failed");
        Guid id;
        await using (var session = fixture.CreateSession())
        {
            id = session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, CancellationToken.None).PreparationId;
            await session.DrainAsync();
            Assert.Null(Assert.Single(fixture.Manifest.Tracks).AacTiming);
        }
        fixture.Manifest = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        var plan = new ConvertedTrackPreparationPlanService(new ConvertedTrackReconciliationService(fixture.Store))
            .Build(fixture.Manifest, ProjectManifestTypes.FolderProject, [fixture.Track], folder.Path,
                Path.GetDirectoryName(fixture.Converted)!, fixture.Preset);
        Assert.Empty(plan.PendingItems);
        var calls = 0;
        fixture.Audio.AnalyzeTiming = (path, _) => { calls++; return Task.FromResult(Timing(path)); };
        await using (var resumed = fixture.CreateSession())
        {
            resumed.QueuePreset(fixture.Preset, CancellationToken.None);
            await resumed.DrainAsync();
        }
        Assert.Equal(1, calls);
        Assert.Equal(id, Assert.Single(fixture.Manifest.Tracks).PreparationId);
        Assert.NotNull(Assert.Single(fixture.Manifest.Tracks).AacTiming);
        await using (var resumedAgain = fixture.CreateSession())
        {
            resumedAgain.QueuePreset(fixture.Preset, CancellationToken.None);
            await resumedAgain.DrainAsync();
        }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancel_and_drain_wait_for_child_exit_and_suppress_even_a_late_successful_result()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audio.AnalyzeTiming = async (path, _) =>
        {
            started.SetResult();
            await finish.Task;
            return Timing(path); // Simulate a worker returning while cancellation is arriving.
        };
        await using var session = fixture.CreateSession();
        session.Complete(0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var drain = session.DrainAsync();
        Assert.False(drain.IsCompleted);
        finish.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        var saved = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        Assert.Null(Assert.Single(saved.Tracks).AacTiming);
        Assert.True(WorkManifestService.IsReusableConvertedManifestEntry(saved.Tracks[0], fixture.Preset.DisplayName));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(40, 8)]
    public async Task Timing_parallelism_scales_moderately_and_is_bounded_independently(int conversionSlots, int expectedSlots)
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var peak = 0; var calls = 0;
        fixture.Audio.AnalyzeTiming = async (path, token) =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensionsMax(ref peak, current);
            if (Interlocked.Increment(ref calls) == expectedSlots) started.SetResult();
            try { await finish.Task.WaitAsync(token); return Timing(path); }
            finally { Interlocked.Decrement(ref active); }
        };
        await using var session = fixture.CreateSession(conversionSlots);
        for (var index = 0; index < 12; index++)
        {
            var target = Path.Combine(folder.Path, $"converted-{index}.m4a");
            File.WriteAllText(target, "prepared");
            session.Complete(index, fixture.Track, fixture.Source, target, fixture.Preset, CancellationToken.None);
        }
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedSlots, peak);
        finish.SetResult();
        await session.DrainAsync();
        Assert.Equal(12, calls);
        Assert.InRange(peak, 1, expectedSlots);
    }

    [Fact]
    public void Presets_keep_independent_ids_reuse_preserves_them_and_only_replacement_invalidates_timing()
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var mono = fixture.Store.UpdateTrack(fixture.Manifest, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        mono.AacTiming = Timing(fixture.Converted);
        var stereoPath = Path.Combine(folder.Path, "stereo.m4a"); File.WriteAllText(stereoPath, "stereo");
        var stereo = fixture.Store.UpdateTrack(fixture.Manifest, 0, fixture.Track, fixture.Source, stereoPath, ExportPreset.Parse("AAC Stereo 256 kbps"));
        Assert.NotEqual(mono.PreparationId, stereo.PreparationId);
        new ConvertedTrackReconciliationService(fixture.Store).ReconcileTrack(fixture.Manifest, ProjectManifestTypes.FolderProject,
            0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        Assert.Contains(fixture.Manifest.Tracks, entry => entry.PreparationId == mono.PreparationId && entry.AacTiming is not null);
        var replaced = fixture.Store.UpdateTrack(fixture.Manifest, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        Assert.NotEqual(mono.PreparationId, replaced.PreparationId);
        Assert.Null(replaced.AacTiming);
        Assert.Contains(fixture.Manifest.Tracks, entry => entry.PreparationId == stereo.PreparationId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Legacy_migration_preserves_audio_and_only_retains_structurally_matching_timing(bool matchingTiming)
    {
        using var folder = new TemporaryFolder();
        var fixture = new Fixture(folder.Path);
        var legacy = fixture.Store.UpdateTrack(fixture.Manifest, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset);
        legacy.PreparationId = Guid.Empty; legacy.PreparationVersion = 0;
        legacy.AacTiming = Timing(fixture.Converted) with { Length = matchingTiming ? new FileInfo(fixture.Converted).Length : 999 };
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ManifestPath)!);
        var json = JsonSerializer.Serialize(fixture.Manifest).Replace("\"PreparationId\":\"00000000-0000-0000-0000-000000000000\"",
            "\"ConvertedSha256\":\"legacy-content-hash\"");
        File.WriteAllText(fixture.ManifestPath, json);
        var bytes = File.ReadAllBytes(fixture.Converted);
        var migrated = fixture.Store.LoadOrCreate(fixture.ManifestPath, folder.Path, folder.Path, fixture.Preset.DisplayName);
        var entry = Assert.Single(migrated.Tracks);
        Assert.NotEqual(Guid.Empty, entry.PreparationId);
        Assert.True(fixture.Store.CanReuseConvertedTrack(migrated, 0, fixture.Track, fixture.Source, fixture.Converted, fixture.Preset));
        Assert.Equal(matchingTiming, entry.AacTiming is not null);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Converted));
        Assert.DoesNotContain("Sha256", File.ReadAllText(fixture.ManifestPath), StringComparison.OrdinalIgnoreCase);
    }

    private static ConvertedAacTiming Timing(string path)
    {
        var file = new FileInfo(path);
        return new(1, file.Length, file.LastWriteTimeUtc.Ticks, 44100, 1, 44100);
    }

    private static void InterlockedExtensionsMax(ref int target, int value)
    {
        int current;
        do { current = Volatile.Read(ref target); if (current >= value) return; }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    private sealed class Fixture
    {
        public Fixture(string folder)
        {
            ManifestPath = ProjectFolderLayout.GetExportManifestPath(folder);
            Source = Path.Combine(folder, "source.mp3");
            Track = new TrackInfo { SourceId = Guid.NewGuid(), FileName = "source.mp3", FilePath = Source,
                ProcessingAction = ProcessingActionIds.Convert };
            var cache = ProjectFolderLayout.GetConvertedPresetFolder(folder, Preset.GetFolderName());
            Directory.CreateDirectory(cache);
            Converted = ConvertedTrackPathService.GetConvertedTrackPath(cache, Source, Track);
            File.WriteAllText(Source, "source");
            File.SetLastWriteTimeUtc(Source, DateTime.UtcNow.AddMinutes(-2));
            File.WriteAllText(Converted, "prepared");
        }
        public WorkManifestService Store { get; set; } = new();
        public RecordingAudioExportProcessingService Audio { get; } = new();
        public ExportWorkManifest Manifest { get; set; } = new();
        public ExportPreset Preset { get; } = ExportPreset.Parse("AAC Mono 64 kbps");
        public string ManifestPath { get; }
        public string Source { get; }
        public string Converted { get; }
        public TrackInfo Track { get; }
        public PreparedTrackLifecycleSession CreateSession(int parallel = 4) =>
            new(Store, Audio, Manifest, ManifestPath, new object(), "probe", parallel);
    }
}
