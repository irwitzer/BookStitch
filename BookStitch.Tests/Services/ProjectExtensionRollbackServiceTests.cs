using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectExtensionRollbackServiceTests
{
    [Theory]
    [InlineData(ProjectManifestTypes.FolderProject)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void Additional_sources_preserve_all_presets_and_rollback_restores_the_complete_baseline(string type)
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var original = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(temp.Path), "source.mp3");
        File.WriteAllText(original, "protected");
        var work = new WorkManifestService();
        var manifest = new ExportWorkManifest { ProjectType = type, ProjectWorkFolder = temp.Path };
        foreach (var preset in new[] { "AAC Mono 64 kbps", "AAC Stereo 128 kbps" })
        {
            var path = Path.Combine(ProjectFolderLayout.GetConvertedPresetFolder(temp.Path, ExportPreset.Parse(preset).GetFolderName()), "001.m4a");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "aac");
            manifest.Tracks.Add(new() { SourceId = Guid.NewGuid(), SourcePath = original, Preset = preset,
                ConvertedPath = path, PreparationId = Guid.NewGuid(), PreparationVersion = 1,
                Status = ProjectManifestTrackStatuses.Converted });
            manifest.State.ManualMergeReviewCompletedPresets.Add(preset);
        }
        manifest.State.Status = ProjectManifestStatuses.Completed;
        manifest.State.LastSuccessfulStep = "ExportCompleted";
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(temp.Path);
        work.Save(manifestPath, manifest);
        var metadata = ProjectFolderLayout.GetProjectMetadataPath(temp.Path);
        File.WriteAllText(metadata, "{\"Title\":\"original\"}");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.CaptureForAdditionalSources(temp.Path);
        Assert.Equal(2, Directory.EnumerateFiles(ProjectFolderLayout.GetConvertedFolder(temp.Path), "*", SearchOption.AllDirectories).Count());
        var before = snapshot.JsonFiles[manifestPath];
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
        var added = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(temp.Path), "new.mp3");
        File.WriteAllText(added, "added");
        File.WriteAllText(metadata, "{}");
        service.Rollback(snapshot);
        Assert.True(File.Exists(original));
        Assert.False(File.Exists(added));
        Assert.Contains("original", File.ReadAllText(metadata));
        var restored = work.ReadOnly(manifestPath);
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
        Assert.Equal(2, restored.State.ManualMergeReviewCompletedPresets.Count);
        Assert.Equal("ExportCompleted", restored.State.LastSuccessfulStep);
        Assert.All(restored.Tracks, track => {
            Assert.True(File.Exists(track.ConvertedPath));
            Assert.NotEqual(Guid.Empty, track.PreparationId);
            Assert.Equal(ProjectManifestTrackStatuses.Converted, track.Status);
        });
    }

    [Theory]
    [InlineData("AdditionalSources")]
    [InlineData("Reconversion")]
    public void Recovery_after_process_end_restores_json_cache_and_sources_idempotently(string kind)
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var manifest = Path.Combine(temp.Path, "project-settings", "project.json");
        File.WriteAllText(manifest, "{\"phase\":\"Completed\"}");
        var old = Path.Combine(ProjectFolderLayout.GetConvertedFolder(temp.Path), "old.m4a");
        File.WriteAllText(old, "old aac");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path) with { Kind = kind };
        // A fresh process sees this durable record without an in-memory owner.
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(temp.Path),
            new ProjectRollbackJournal { Snapshot = snapshot }, null);
        service.PreserveConvertedFile(snapshot, old);
        File.WriteAllText(old, "replacement aac");
        File.WriteAllText(manifest, "{}");
        var newOriginal = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(temp.Path), "new.mp3");
        File.WriteAllText(newOriginal, "new source");
        Assert.True(service.Recover(temp.Path));
        Assert.Equal("old aac", File.ReadAllText(old));
        Assert.Equal("{\"phase\":\"Completed\"}", File.ReadAllText(manifest));
        Assert.False(File.Exists(newOriginal));
        Assert.False(service.Recover(temp.Path));
    }

    [Fact]
    public void Merge_recovery_removes_verified_external_part_and_restores_review_and_aac()
    {
        using var temp = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var store = new WorkManifestService();
        var manifest = new ExportWorkManifest { ProjectWorkFolder = temp.Path,
            State = new() { Status = ProjectManifestStatuses.ReviewBeforeMerge },
            Export = new() { OutputFolder = output.Path, OutputFileName = "book.m4b" } };
        var path = ProjectFolderLayout.GetExportManifestPath(temp.Path);
        store.Save(path, manifest);
        var part = ConvertedTrackPathService.GetPartFilePath(Path.Combine(output.Path, "book.m4b"));
        var aac = Path.Combine(ProjectFolderLayout.GetConvertedFolder(temp.Path), "old.m4a");
        File.WriteAllText(aac, "old aac");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path) with { Kind = "Merge", FinalPartPath = part };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(temp.Path),
            new ProjectRollbackJournal { Snapshot = snapshot }, null);
        File.WriteAllText(part, "incomplete merge");
        manifest.State.Status = ProjectManifestStatuses.Merging;
        manifest.Resume.MergePaused = true;
        store.Save(path, manifest);
        Assert.True(service.Recover(temp.Path));
        var restored = store.ReadOnly(path);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, restored.State.Status);
        Assert.False(restored.Resume.MergePaused);
        Assert.False(File.Exists(part));
        Assert.Equal("old aac", File.ReadAllText(aac));
    }

    [Fact]
    public void Startup_finishes_committed_cleanup_without_rolling_back_accepted_new_sources()
    {
        using var parent = new TemporaryDirectory();
        var root = Path.Combine(parent.Path, "project");
        Directory.CreateDirectory(root);
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(root) with { Kind = "AdditionalSources" };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(root),
            new ProjectRollbackJournal { Snapshot = snapshot, Stage = "Committed" }, null);
        var source = Path.Combine(root, "new.mp3");
        File.WriteAllText(source, "accepted source");
        Assert.Empty(service.RecoverInterruptedAttempts([parent.Path]));
        Assert.Equal("accepted source", File.ReadAllText(source));
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(root)));
        Assert.Empty(service.RecoverInterruptedAttempts([parent.Path]));
    }

    [Fact]
    public async Task Actual_aac_replacement_during_extension_is_copy_on_write_and_discard_restores_bytes()
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var source = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(temp.Path), "track.m4a");
        var target = Path.Combine(ProjectFolderLayout.GetConvertedFolder(temp.Path), "track.m4a");
        File.WriteAllText(source, "source aac");
        File.WriteAllText(target, "previous aac");
        var rollback = new ProjectExtensionRollbackService();
        var baseline = rollback.CaptureForAdditionalSources(temp.Path);
        await new AacExportProcessingService().PrepareTrackForExportAsync(
            new TrackInfo { SampleRateHz = 44100, ProcessingAction = ProcessingActionIds.Reuse }, source, target,
            ExportPreset.Parse("AAC Stereo 128 kbps"), "", default, _ => { });
        Assert.Equal("source aac", File.ReadAllText(target));
        rollback.Rollback(baseline);
        Assert.Equal("previous aac", File.ReadAllText(target));
        Assert.Equal("source aac", File.ReadAllText(source));
    }

    [Fact]
    public void An_old_snapshot_cannot_commit_or_rollback_a_new_attempt()
    {
        using var temp = new TemporaryDirectory();
        var service = new ProjectExtensionRollbackService();
        var old = service.CaptureForAdditionalSources(temp.Path);
        service.Commit(old);
        var current = service.CaptureForAdditionalSources(temp.Path);
        Assert.Throws<IOException>(() => service.Commit(old));
        Assert.Throws<IOException>(() => service.Rollback(old));
        service.Rollback(current);
    }

    [Fact]
    public void Live_transaction_cannot_be_recovered_or_overwritten_and_commit_keeps_new_sources()
    {
        using var temp = new TemporaryDirectory();
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.CaptureForAdditionalSources(temp.Path);
        var marker = File.ReadAllBytes(ProjectExtensionRollbackService.JournalPath(temp.Path));
        Assert.False(service.Recover(temp.Path));
        Assert.Throws<IOException>(() => service.CaptureForAdditionalSources(temp.Path));
        Assert.Equal(marker, File.ReadAllBytes(ProjectExtensionRollbackService.JournalPath(temp.Path)));
        var source = Path.Combine(temp.Path, "new.mp3");
        File.WriteAllText(source, "new source");
        service.Commit(snapshot);
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(temp.Path)));
        Assert.Equal("new source", File.ReadAllText(source));
    }

    [Fact]
    public void Partial_rollback_is_repeated_after_json_restore_without_losing_the_old_aac()
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var old = Path.Combine(ProjectFolderLayout.GetConvertedFolder(temp.Path), "old.m4a");
        File.WriteAllText(old, "old aac");
        var manifest = Path.Combine(temp.Path, "project-settings", "state.json");
        File.WriteAllText(manifest, "{}");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path) with { Kind = "Reconversion" };
        service.PreserveConvertedFile(snapshot, old);
        File.WriteAllText(old, "new aac");
        File.WriteAllBytes(manifest, snapshot.JsonFiles[manifest]);
        Assert.True(service.Recover(temp.Path));
        Assert.Equal("old aac", File.ReadAllText(old));
        Assert.False(service.Recover(temp.Path));
    }

    [Fact]
    public void Recovery_rejects_a_foreign_project_identity_without_deleting_files()
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var primary = ProjectFolderLayout.GetMp3DiscManifestPath(temp.Path);
        File.WriteAllText(primary, "{\"ProjectId\":\"" + Guid.NewGuid() + "\"}");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path) with { Kind = "AdditionalSources" };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(temp.Path),
            new ProjectRollbackJournal { Snapshot = snapshot }, null);
        var replacement = "{\"ProjectId\":\"" + Guid.NewGuid() + "\"}";
        File.WriteAllText(primary, replacement);
        Assert.Throws<InvalidDataException>(() => service.Recover(temp.Path));
        Assert.Equal(replacement, File.ReadAllText(primary));
        Assert.True(File.Exists(ProjectExtensionRollbackService.JournalPath(temp.Path)));
    }

    [Fact]
    public void Reconversion_discard_restores_the_previous_selected_preset_in_all_manifests()
    {
        using var temp = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(temp.Path);
        var manifest = ProjectFolderLayout.GetExportManifestPath(temp.Path);
        File.WriteAllText(manifest, "{\"SelectedPreset\":\"AAC Mono 64 kbps\",\"Export\":{\"SelectedPreset\":\"AAC Mono 64 kbps\"}}");
        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(temp.Path, "AAC Stereo 128 kbps", "AAC Mono 64 kbps");
        service.RollbackPresetReconversion(context);
        var restored = new WorkManifestService().ReadOnly(manifest);
        Assert.Equal("AAC Stereo 128 kbps", restored.SelectedPreset);
        Assert.Equal("AAC Stereo 128 kbps", restored.Export.SelectedPreset);
    }

    [Fact]
    public void Explicit_cleanup_never_deletes_an_original_cache_file_from_the_snapshot()
    {
        using var temp = new TemporaryDirectory();
        var old = Path.Combine(temp.Path, "old.m4a");
        File.WriteAllText(old, "aac");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);
        service.Rollback(snapshot, [old]);
        Assert.Equal("aac", File.ReadAllText(old));
    }

    [Fact]
    public void Rollback_RemovesOnlyFilesAndDirectoriesCreatedAfterCapture()
    {
        using var temp = new TemporaryDirectory();
        var existingFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "originals", "CD 01")).FullName;
        var existingFile = Path.Combine(existingFolder, "001.mp3");
        File.WriteAllText(existingFile, "existing");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);

        var newFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "originals", "CD 02")).FullName;
        var newFile = Path.Combine(newFolder, "001.mp3");
        File.WriteAllText(newFile, "new");

        var result = service.Rollback(snapshot);

        Assert.True(File.Exists(existingFile));
        Assert.False(File.Exists(newFile));
        Assert.False(Directory.Exists(newFolder));
        Assert.Equal(1, result.DeletedFiles);
    }

    [Fact]
    public void Rollback_RestoresChangedJsonManifest()
    {
        using var temp = new TemporaryDirectory();
        var manifest = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(manifest, "{\"totalDiscs\":1}");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);
        File.WriteAllText(manifest, "{\"totalDiscs\":2}");

        service.Rollback(snapshot);

        Assert.Equal("{\"totalDiscs\":1}", File.ReadAllText(manifest));
    }

    [Fact]
    public void Rollback_RemovesExplicitGeneratedConvertedFileAndPartFile()
    {
        using var temp = new TemporaryDirectory();
        var convertedFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "converted", "aac_stereo_128k")).FullName;
        var existingFile = Path.Combine(convertedFolder, "001_existing.m4a");
        File.WriteAllText(existingFile, "existing");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);

        var generatedFile = Path.Combine(convertedFolder, "144_added.m4a");
        var generatedPartFile = generatedFile + ".part";
        File.WriteAllText(generatedFile, "new");
        File.WriteAllText(generatedPartFile, "partial");

        service.Rollback(snapshot, new[] { generatedFile });

        Assert.True(File.Exists(existingFile));
        Assert.False(File.Exists(generatedFile));
        Assert.False(File.Exists(generatedPartFile));
    }


    [Fact]
    public void RollbackPresetReconversion_RemovesNewTargetAndRestoresManifest()
    {
        using var temp = new TemporaryDirectory();
        var manifest = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(manifest, "{\"preset\":\"AAC Stereo 192 kbps\"}");

        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(
            temp.Path,
            "AAC Stereo 192 kbps",
            "AAC Mono 64 kbps");

        Assert.False(context.TargetPresetFolderExisted);

        Directory.CreateDirectory(context.TargetPresetFolder);
        var completedTrack = Path.Combine(context.TargetPresetFolder, "001.m4a");
        var partTrack = Path.Combine(context.TargetPresetFolder, "002.m4a.part");
        File.WriteAllText(completedTrack, "converted");
        File.WriteAllText(partTrack, "partial");
        File.WriteAllText(manifest, "{\"preset\":\"AAC Mono 64 kbps\"}");

        var result = service.RollbackPresetReconversion(context);

        Assert.False(Directory.Exists(context.TargetPresetFolder));
        Assert.False(File.Exists(completedTrack));
        Assert.False(File.Exists(partTrack));
        Assert.Equal("{\"preset\":\"AAC Stereo 192 kbps\"}", File.ReadAllText(manifest));
        Assert.Equal(1, result.RestoredJsonFiles);
    }


    [Fact]
    public void RollbackPresetReconversion_PreservesExistingPartialSamePresetBaseline()
    {
        using var temp = new TemporaryDirectory();
        var manifest = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(manifest, "{\"preset\":\"AAC Stereo 96 kbps\"}");

        var presetFolder = ProjectFolderLayout.GetConvertedPresetFolder(
            temp.Path,
            ExportPreset.Parse("AAC Stereo 96 kbps").GetFolderName());
        Directory.CreateDirectory(presetFolder);
        var existingTrack = Path.Combine(presetFolder, "001_existing.m4a");
        File.WriteAllText(existingTrack, "existing partial baseline");

        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(
            temp.Path,
            "AAC Stereo 96 kbps",
            "AAC Stereo 96 kbps");

        Assert.True(context.TargetPresetFolderExisted);

        var newlyPreparedTrack = Path.Combine(presetFolder, "002_new.m4a");
        File.WriteAllText(newlyPreparedTrack, "newly prepared");
        File.WriteAllText(newlyPreparedTrack + ".part", "partial");
        File.WriteAllText(manifest, "{\"preset\":\"AAC Stereo 96 kbps\",\"progress\":2}");

        service.RollbackPresetReconversion(context);

        Assert.True(Directory.Exists(presetFolder));
        Assert.True(File.Exists(existingTrack));
        Assert.Equal("existing partial baseline", File.ReadAllText(existingTrack));
        Assert.False(File.Exists(newlyPreparedTrack));
        Assert.False(File.Exists(newlyPreparedTrack + ".part"));
        Assert.Equal("{\"preset\":\"AAC Stereo 96 kbps\"}", File.ReadAllText(manifest));
    }

    [Fact]
    public void RollbackPresetReconversion_PreservesPreExistingPartialTargetWhenSwitchingPresets()
    {
        using var temp = new TemporaryDirectory();
        var targetFolder = ProjectFolderLayout.GetConvertedPresetFolder(
            temp.Path,
            ExportPreset.Parse("AAC Stereo 96 kbps").GetFolderName());
        Directory.CreateDirectory(targetFolder);
        var existingTargetTrack = Path.Combine(targetFolder, "001_existing.m4a");
        File.WriteAllText(existingTargetTrack, "partial target before run");

        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(
            temp.Path,
            "AAC Stereo 192 kbps",
            "AAC Stereo 96 kbps");

        Assert.True(context.TargetPresetFolderExisted);

        var newlyPreparedTrack = Path.Combine(targetFolder, "002_new.m4a");
        File.WriteAllText(newlyPreparedTrack, "new during run");

        service.RollbackPresetReconversion(context);

        Assert.True(Directory.Exists(targetFolder));
        Assert.True(File.Exists(existingTargetTrack));
        Assert.Equal("partial target before run", File.ReadAllText(existingTargetTrack));
        Assert.False(File.Exists(newlyPreparedTrack));
    }

    [Fact]
    public void RollbackPresetReconversion_RemovesAllNewArtifacts()
    {
        using var temp = new TemporaryDirectory();
        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(
            temp.Path,
            "AAC Stereo 192 kbps",
            "AAC Mono 64 kbps");

        Directory.CreateDirectory(context.TargetPresetFolder);
        var completedTrack = Path.Combine(context.TargetPresetFolder, "001.m4a");
        var copyingTrack = Path.Combine(context.TargetPresetFolder, "002.m4a.copying");
        File.WriteAllText(completedTrack, "complete");
        File.WriteAllText(copyingTrack, "incomplete");

        service.RollbackPresetReconversion(context);

        Assert.False(File.Exists(completedTrack));
        Assert.False(File.Exists(copyingTrack));
    }

    [Fact]
    public void RollbackPresetReconversion_RejectsTargetPresetFolderOutsideConvertedRoot()
    {
        using var temp = new TemporaryDirectory();
        var service = new ProjectExtensionRollbackService();
        var context = service.CapturePresetReconversion(
            temp.Path,
            "AAC Stereo 192 kbps",
            "AAC Mono 64 kbps");
        var unsafeContext = context with { TargetPresetFolder = Path.Combine(temp.Path, "originals") };

        Assert.Throws<InvalidOperationException>(() => service.RollbackPresetReconversion(unsafeContext));
    }

    [Fact]
    public void Rollback_RemovesFileThatAppearsShortlyAfterCleanupStarts()
    {
        using var temp = new TemporaryDirectory();
        var convertedFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "converted", "aac_stereo_128k")).FullName;

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);
        var lateFile = Path.Combine(convertedFolder, "144_added.m4a.part");

        using var writerCompleted = new ManualResetEventSlim(false);
        var writer = new Thread(() =>
        {
            Thread.Sleep(10);
            File.WriteAllText(lateFile, "late");
            writerCompleted.Set();
        });
        writer.Start();

        service.Rollback(snapshot);
        writer.Join();

        Assert.True(writerCompleted.IsSet);
        Assert.False(File.Exists(lateFile));
    }

    [Theory]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData(ProjectPipelineState.Completed)]
    public void Resumed_reconversion_rollback_retains_previous_phase_originals_and_prepared_preset(ProjectPipelineState previousPhase)
    {
        using var temp = new TemporaryDirectory();
        var original = Path.Combine(temp.Path, "originals", "track.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!); File.WriteAllText(original, "original");
        var timestamp = File.GetLastWriteTimeUtc(original);
        var manifest = Path.Combine(temp.Path, "project.json");
        var before = "{\"preset\":\"AAC Stereo 192 kbps\",\"phase\":\"" + previousPhase + "\"}";
        File.WriteAllText(manifest, before);
        var preparedA = ProjectFolderLayout.GetConvertedPresetFolder(temp.Path, ExportPreset.Parse("AAC Stereo 192 kbps").GetFolderName());
        Directory.CreateDirectory(preparedA); File.WriteAllText(Path.Combine(preparedA, "track.m4a"), "prepared A");
        var rollback = new ProjectExtensionRollbackService();
        var context = rollback.CapturePresetReconversion(temp.Path, "AAC Stereo 192 kbps", "AAC Mono 64 kbps") with { PreviousPhase = previousPhase };
        Directory.CreateDirectory(context.TargetPresetFolder);
        File.WriteAllText(Path.Combine(context.TargetPresetFolder, "track.m4a"), "prepared B");
        File.WriteAllText(Path.Combine(context.TargetPresetFolder, "unfinished.m4a.part"), "partial B");
        File.WriteAllText(manifest, "{\"preset\":\"AAC Mono 64 kbps\",\"phase\":\"Converting\"}");
        Assert.Equal(ProjectCloseIntent.RollbackRun, ApplicationShutdownService.ResolveProjectCloseIntent(false, false, true));
        rollback.RollbackPresetReconversion(context);
        Assert.Equal(previousPhase, context.PreviousPhase); Assert.Equal(before, File.ReadAllText(manifest));
        Assert.False(Directory.Exists(context.TargetPresetFolder));
        Assert.Equal("prepared A", File.ReadAllText(Path.Combine(preparedA, "track.m4a")));
        Assert.Equal("original", File.ReadAllText(original)); Assert.Equal(timestamp, File.GetLastWriteTimeUtc(original));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Merge_journal_recovers_current_project_part_and_legacy_output_part_idempotently(bool legacy)
    {
        using var project = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(project.Path);
        var final = Path.Combine(output.Path, "book.m4b");
        File.WriteAllBytes(final, [1, 2, 3, 4]);
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(project.Path);
        var manifest = new ExportWorkManifest { ProjectWorkFolder = project.Path,
            State = new() { Status = ProjectManifestStatuses.ReviewBeforeMerge },
            Export = new() { OutputFolder = output.Path, OutputFileName = "book.m4b" } };
        var store = new WorkManifestService(); store.Save(manifestPath, manifest);
        var baselineJson = File.ReadAllBytes(manifestPath);
        var aac = Path.Combine(ProjectFolderLayout.GetConvertedFolder(project.Path), "prepared.m4a");
        File.WriteAllBytes(aac, [5, 6, 7, 8]);
        var part = legacy ? ConvertedTrackPathService.GetPartFilePath(final)
            : ConvertedTrackPathService.GetMergePartFilePath(project.Path, final);
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(project.Path) with { Kind = "Merge", FinalPartPath = part };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(project.Path),
            new ProjectRollbackJournal { Snapshot = snapshot }, null);
        File.WriteAllText(part, "interrupted merge");
        File.WriteAllText(Path.Combine(ProjectFolderLayout.GetMergeFolder(project.Path), "concat-list.txt"), "attempt");
        manifest.State.Status = ProjectManifestStatuses.Merging; manifest.Resume.MergePaused = true;
        store.Save(manifestPath, manifest);
        Assert.True(service.Recover(project.Path));
        Assert.Equal(baselineJson, File.ReadAllBytes(manifestPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(final));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, File.ReadAllBytes(aac));
        Assert.False(File.Exists(part));
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(project.Path)));
        Assert.Null(service.GetActive(project.Path));
        Assert.False(service.Recover(project.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Startup_reports_unverified_merge_without_deletion_or_false_commit_and_recovers_other_projects(bool foreignArtifact)
    {
        using var parent = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        var root = Path.Combine(parent.Path, "blocked"); ProjectFolderLayout.EnsureProjectFolders(root);
        var final = Path.Combine(output.Path, "book.m4b"); File.WriteAllText(final, "existing final");
        var manifest = ProjectFolderLayout.GetExportManifestPath(root);
        new WorkManifestService().Save(manifest, new ExportWorkManifest { ProjectWorkFolder = root,
            Export = new() { OutputFolder = output.Path, OutputFileName = "book.m4b" } });
        var service = new ProjectExtensionRollbackService();
        var part = foreignArtifact ? ConvertedTrackPathService.GetMergePartFilePath(root, final)
            : Path.Combine(output.Path, "foreign.part.m4b");
        var snapshot = service.Capture(root) with { Kind = "Merge", FinalPartPath = part };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(root), new ProjectRollbackJournal { Snapshot = snapshot }, null);
        File.WriteAllText(part, "preserve");
        if (foreignArtifact) File.WriteAllText(Path.Combine(ProjectFolderLayout.GetOriginalsFolder(root), "foreign.mp3"), "preserve");
        File.WriteAllText(manifest, "{\"after\":true}");
        var pending = File.ReadAllBytes(ProjectExtensionRollbackService.JournalPath(root));
        var other = Path.Combine(parent.Path, "healthy"); ProjectFolderLayout.EnsureProjectFolders(other);
        var otherSnapshot = service.Capture(other) with { Kind = "AdditionalSources" };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(other), new ProjectRollbackJournal { Snapshot = otherSnapshot }, null);
        Assert.Single(service.RecoverInterruptedAttempts([parent.Path]));
        Assert.Equal(pending, File.ReadAllBytes(ProjectExtensionRollbackService.JournalPath(root)));
        Assert.Equal("{\"after\":true}", File.ReadAllText(manifest));
        Assert.Equal("preserve", File.ReadAllText(part)); Assert.Equal("existing final", File.ReadAllText(final));
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(other)));
        Assert.Single(service.RecoverInterruptedAttempts([parent.Path]));
    }

    [Fact]
    public void Startup_reports_null_snapshot_without_a_global_exception()
    {
        using var parent = new TemporaryDirectory();
        var root = Path.Combine(parent.Path, "broken"); ProjectFolderLayout.EnsureProjectFolders(root);
        var path = ProjectExtensionRollbackService.JournalPath(root);
        File.WriteAllText(path, "{\"Version\":1,\"Snapshot\":null}");
        Assert.Single(new ProjectExtensionRollbackService().RecoverInterruptedAttempts([parent.Path]));
        Assert.True(File.Exists(path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
    }
}
