using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class DiscProjectStopSaveServiceTests
{
    private const string Preset = "AAC Stereo 128 kbps";
    private readonly DiscProjectStopSaveService _service = new();

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources, true, false, false, true)]
    [InlineData(ProjectPipelineState.Converting, true, false, false, true)]
    [InlineData(ProjectPipelineState.AcquiringSources, true, true, false, false)]
    [InlineData(ProjectPipelineState.Converting, true, false, true, false)]
    [InlineData(ProjectPipelineState.Merging, true, false, false, false)]
    [InlineData(ProjectPipelineState.Completed, true, false, false, false)]
    [InlineData(ProjectPipelineState.Converting, false, false, false, false)]
    public void Save_permission_is_exclusive_to_initial_disc_import(ProjectPipelineState phase,
        bool initial, bool extension, bool reconversion, bool expected)
        => Assert.Equal(expected, DiscProjectStopSaveService.CanOfferImportSave(phase, initial, extension, reconversion));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_save_cannot_commit_a_pending_extension(bool audio)
    {
        using var fixture = new Fixture(audio);
        var rollback = new ProjectExtensionRollbackService();
        var snapshot = rollback.CaptureForAdditionalSources(fixture.Root);
        Assert.False(_service.CanSave(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(ProjectExtensionRollbackService.JournalPath(fixture.Root)));
        rollback.Rollback(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_save_is_not_available_for_a_paused_merge(bool audio)
    {
        using var fixture = new Fixture(audio);
        fixture.CompleteSecondDisc();
        var path = ProjectFolderLayout.GetExportManifestPath(fixture.Root);
        var manifest = Read<ExportWorkManifest>(path);
        manifest.Resume.MergePaused = true;
        manifest.State.Status = ProjectManifestStatuses.ReviewBeforeMerge;
        Write(path, manifest);
        var before = File.ReadAllBytes(path);
        Assert.False(_service.CanSave(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => _service.Save(fixture.Root));
        Assert.Equal(before, File.ReadAllBytes(path));
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.True(plan.IsMergePaused);
        Assert.False(plan.CanContinueDiscImport);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Mp3_initial_import_stop_save_retains_completed_discs_and_the_four_disc_resume_plan(int completed)
    {
        using var fixture = new Fixture(audio: false);
        fixture.CompleteSecondDisc();
        var path = ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
        var manifest = Read<Mp3DiscProjectManifest>(path);
        manifest.TotalDiscs = 4;
        manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
        if (completed == 3)
        {
            var third = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(fixture.Root, 3), "third.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(third)!);
            File.WriteAllBytes(third, [1, 2, 3]);
            manifest.ImportedDiscs.Add(new() { DiscNumber = 3, Signature = "third", FileCount = 1, CopiedFiles = 1,
                Status = Mp3DiscImportStatus.Completed, LocalFolder = Path.GetDirectoryName(third)!,
                SourceFiles = [new() { SourceId = Guid.NewGuid(), SourceFileName = "third.mp3", SourceSizeBytes = 3,
                    RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), third) }] });
        }
        Write(path, manifest);
        _service.Save(fixture.Root);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        path = ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
        Assert.Equal(completed, plan.ImportedDiscCount);
        Assert.Equal(completed + 1, plan.NextMissingDiscNumber);
        Assert.Equal(4, Read<Mp3DiscProjectManifest>(path).TotalDiscs);
        Assert.True(Read<Mp3DiscProjectManifest>(path).IsStoppedForResume);
        DiscProjectStopSaveService.ValidateResumeSources(plan);
        Assert.True(File.Exists(fixture.FirstSource));
        Assert.True(File.Exists(fixture.PartialSource));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Canonical_metadata_edited_in_resume_preview_survives_stop_save_and_resume(bool audio)
    {
        using var fixture = new Fixture(audio);
        var session = new ProjectMetadataSession();
        session.OpenExisting(fixture.Root);
        session.Mutate(metadata =>
        {
            metadata.SetValue(ProjectMetadataField.Title, "Preview-edited title", MetadataOrigin.User);
            metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Preview author"], MetadataOrigin.User);
        });
        Assert.True(session.Flush());
        var saved = File.ReadAllBytes(ProjectFolderLayout.GetProjectMetadataPath(fixture.Root));
        _service.Save(fixture.Root);
        Assert.Equal(saved, File.ReadAllBytes(ProjectFolderLayout.GetProjectMetadataPath(fixture.Root)));
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        var loaded = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.True(loaded.Success, loaded.ErrorMessage);
        Assert.Equal("Preview-edited title", loaded.CanonicalMetadata!.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(new[] { "Preview author" }, loaded.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(2, plan.NextMissingDiscNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Two_completed_discs_and_repeated_partial_third_disc_resume_at_third_disc(bool audio)
    {
        using var fixture = new Fixture(audio);
        fixture.CompleteSecondDisc();
        var third = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), "CD 03", audio ? "third.flac" : "third.mp3");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(third)!);
            File.WriteAllBytes(third, [1, 2, 3]);
            if (audio)
            {
                var path = ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root);
                var manifest = Read<AudioDiscProjectManifest>(path);
                foreach (var completed in manifest.Discs) completed.DiscIdentity = $"fixture-disc-{completed.DiscNumber}";
                manifest.Discs.Add(new() { DiscNumber = 3, DiscIdentity = "fixture-disc-3", TrackCount = 2, Status = AudioDiscStatus.Ripping,
                    Tracks = [new() { DiscNumber = 3, TrackNumber = 1, SourceId = Guid.NewGuid(),
                        RelativePath = Path.GetRelativePath(fixture.Root, third), Status = AudioDiscTrackStatus.Ripped }] });
                Write(path, manifest);
            }
            else
            {
                var path = ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
                var manifest = Read<Mp3DiscProjectManifest>(path);
                foreach (var completed in manifest.ImportedDiscs) completed.Signature = $"fixture-disc-{completed.DiscNumber}";
                manifest.ImportedDiscs.Add(new() { DiscNumber = 3, Signature = "fixture-disc-3", FileCount = 2, CopiedFiles = 1, Status = "Importing",
                    LocalFolder = Path.GetDirectoryName(third)!, SourceFiles = [new() { SourceId = Guid.NewGuid(),
                        RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), third) }] });
                Write(path, manifest);
            }
            _service.Save(fixture.Root);
            var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
            Assert.Equal(2, plan.ImportedDiscCount);
            Assert.Equal(3, plan.NextMissingDiscNumber);
            Assert.True(File.Exists(fixture.FirstSource));
            Assert.True(File.Exists(fixture.PartialSource));
            Assert.False(File.Exists(third));
            DiscProjectStopSaveService.ValidateResumeSources(plan);
            if (audio)
            {
                var resumed = Read<AudioDiscProjectManifest>(ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root));
                Assert.Equal(new[] { "fixture-disc-1", "fixture-disc-2" }, resumed.Discs.Select(disc => disc.DiscIdentity));
                var sameThird = new AudioDiscInfo("Z:\\", "Z", [new(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "third-track")],
                    TimeSpan.FromSeconds(1), "fixture-disc-3");
                Assert.Equal(3, new AudioDiscProjectService().AddDisc(resumed, sameThird, 3).DiscNumber);
            }
            else
            {
                var resumed = Read<Mp3DiscProjectManifest>(ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root));
                Assert.Equal(new[] { "fixture-disc-1", "fixture-disc-2" }, resumed.ImportedDiscs.Select(disc => disc.Signature));
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repeated_partial_resume_disc_and_uncommitted_preview_edits_are_discarded(bool audio)
    {
        using var fixture = new Fixture(audio);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.PartialSource)!);
                File.WriteAllBytes(fixture.PartialSource, [1, 2]);
                if (audio)
                {
                    var path = ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root);
                    var manifest = Read<AudioDiscProjectManifest>(path);
                    manifest.Discs.Add(new() { DiscNumber = 2, Status = AudioDiscStatus.Ripping,
                        Tracks = [new() { DiscNumber = 2, SourceId = fixture.PartialId, Status = AudioDiscTrackStatus.Pending,
                            RelativePath = Path.GetRelativePath(fixture.Root, fixture.PartialSource), FileName = Path.GetFileName(fixture.PartialSource) }] });
                    Write(path, manifest);
                }
                else
                {
                    var path = ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
                    var manifest = Read<Mp3DiscProjectManifest>(path);
                    manifest.ImportedDiscs.Add(new() { DiscNumber = 2, Status = "Importing", FileCount = 2, CopiedFiles = 1,
                        LocalFolder = Path.GetDirectoryName(fixture.PartialSource)!,
                        SourceFiles = [new() { SourceId = fixture.PartialId, SourceFileName = Path.GetFileName(fixture.PartialSource),
                            RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), fixture.PartialSource) }] });
                    Write(path, manifest);
                }
            }
            new TrackListStateService().Save(fixture.Root, [
                new() { SourceId = fixture.FirstId, FilePath = fixture.FirstSource, DiscNumber = 1, IsExcluded = true },
                new() { SourceId = fixture.PartialId, FilePath = fixture.PartialSource, DiscNumber = 2, ChapterTitle = "Partial edit" },
                new() { SourceId = Guid.NewGuid(), FilePath = Path.Combine(Path.GetDirectoryName(fixture.PartialSource)!, "preview.flac"),
                    DiscNumber = 2, IsExcluded = true, ExcludedChapterTitle = "Uncommitted preview" }]);
            _service.Save(fixture.Root);
            var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
            Assert.Equal(2, plan.NextMissingDiscNumber);
            Assert.Equal(fixture.FirstId, Assert.Single(plan.Tracks).SourceId);
            Assert.True(File.Exists(fixture.FirstSource));
            Assert.False(File.Exists(fixture.PartialSource));
            using var state = JsonDocument.Parse(File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(fixture.Root)));
            Assert.Single(state.RootElement.GetProperty("Tracks").EnumerateArray());
        }
    }

    [Fact]
    public void Stop_save_preserves_completed_mp3_source_with_part_marker()
    {
        using var fixture = new Fixture(false);
        var renamed = Path.Combine(Path.GetDirectoryName(fixture.FirstSource)!, "Mein.Hoerbuch.part.01.mp3");
        File.Move(fixture.FirstSource, renamed);
        var path = ProjectFolderLayout.GetWorkManifestPath(fixture.Root);
        var manifest = Read<Mp3DiscProjectManifest>(path);
        manifest.ImportedDiscs[0].SourceFiles[0].SourceFileName = Path.GetFileName(renamed);
        manifest.ImportedDiscs[0].SourceFiles[0].RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), renamed);
        Write(path, manifest);
        var exportPath = ProjectFolderLayout.GetExportManifestPath(fixture.Root);
        var export = Read<ExportWorkManifest>(exportPath);
        export.Tracks[0].SourcePath = renamed;
        export.Tracks[0].SourceFileName = Path.GetFileName(renamed);
        Write(exportPath, export);
        _service.Save(fixture.Root);
        Assert.True(File.Exists(renamed));
        Assert.Equal(Mp3DiscImportStatus.Completed, Assert.Single(Read<Mp3DiscProjectManifest>(path).ImportedDiscs).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_DropsPartialDiscAcrossFilesAndState_AndKeepsCompletedDisc(bool audio)
    {
        using var fixture = new Fixture(audio);
        var originalMetadata = File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(fixture.Root));
        _service.Save(fixture.Root);

        Assert.True(File.Exists(fixture.FirstSource));
        Assert.False(File.Exists(fixture.PartialSource));
        Assert.True(File.Exists(fixture.FirstConverted));
        Assert.False(File.Exists(fixture.PartialConverted));
        Assert.False(File.Exists(fixture.UnfinishedConversion));
        Assert.False(File.Exists(fixture.MergeFile));
        Assert.False(File.Exists(ProjectFolderLayout.GetSourceMetadataSnapshotPath(fixture.Root, fixture.PartialId)));
        Assert.True(File.Exists(ProjectFolderLayout.GetSourceMetadataSnapshotPath(fixture.Root, fixture.FirstId)));
        Assert.Equal(originalMetadata, File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(fixture.Root)));

        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root, "AAC Stereo 320 kbps")!;
        Assert.True(plan.IsStoppedDiscProject);
        Assert.Equal(Preset, plan.SelectedPreset);
        Assert.Equal(5, plan.TotalDiscs);
        Assert.Equal(2, plan.NextMissingDiscNumber);
        Assert.Single(plan.Tracks);
        Assert.Equal(fixture.FirstId, plan.Tracks[0].SourceId);
        DiscProjectStopSaveService.ValidateResumeSources(plan);
        var loaded = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.True(loaded.Success);
        Assert.True(Assert.Single(loaded.Tracks).IsExcluded);
        Assert.False(_service.HasPendingCommit(fixture.Root));

        var export = Read<ExportWorkManifest>(ProjectFolderLayout.GetExportManifestPath(fixture.Root));
        Assert.Single(export.Tracks);
        Assert.Equal(8, export.Tracks[0].TrackIndex);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, export.Tracks[0].Status);
        Assert.Equal(8, export.State.LastCompletedTrackIndex);
        Assert.Empty(export.History);
        Assert.Single(new SourceCopyIdentityStore().Load(fixture.Root));
        using var state = JsonDocument.Parse(File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(fixture.Root)));
        Assert.Single(state.RootElement.GetProperty("Tracks").EnumerateArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_AllDiscsComplete_ResumesProcessingWithoutRequestingAnotherDisc(bool audio)
    {
        using var fixture = new Fixture(audio);
        fixture.SetTotalDiscs(2);
        fixture.CompleteSecondDisc();
        _service.Save(fixture.Root);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.Null(plan.NextMissingDiscNumber);
        Assert.False(plan.CanContinueDiscImport);
        Assert.True(plan.CanResume);
        Assert.False(plan.HasSuccessfulExport);
        Assert.Equal(2, plan.ImportedDiscCount);
        var loaded = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.True(loaded.Success);
        Assert.Equal(ProjectPipelineState.Converting, loaded.PipelineState);
        Assert.False(loaded.IsWaitingForManualMergeReview);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_ConversionNotFinished_KeepsSourceAndResetsOutputState(bool audio)
    {
        using var fixture = new Fixture(audio);
        var path = ProjectFolderLayout.GetExportManifestPath(fixture.Root);
        var manifest = Read<ExportWorkManifest>(path);
        manifest.Tracks[0].Status = ProjectManifestTrackStatuses.Converting;
        manifest.Tracks[0].ConvertedPath = fixture.UnfinishedConversion;
        Write(path, manifest);
        File.Delete(fixture.FirstConverted);
        _service.Save(fixture.Root);
        var track = Assert.Single(Read<ExportWorkManifest>(path).Tracks);
        Assert.True(File.Exists(fixture.FirstSource));
        Assert.Equal(ProjectManifestTrackStatuses.Pending, track.Status);
        Assert.Equal("", track.ConvertedPath);
        Assert.Null(track.CompletedUtc);
        Assert.False(File.Exists(fixture.UnfinishedConversion));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_Repeatedly_IsIdempotentAndSelectable(bool audio)
    {
        using var fixture = new Fixture(audio);
        _service.Save(fixture.Root);
        _service.Save(fixture.Root);
        var item = Assert.Single(new ProjectIndexService().ScanSelectableProjects(fixture.Root));
        Assert.True(item.IsStoppedDiscProject);
        Assert.False(item.IsCompletedProject);
        Assert.True(item.CanResume);
        Assert.Equal(LocalizationText.Format("Disc.StopSave.Status", 1, 5), item.ListDisplayState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_InvalidDiscCount_IsRejectedWithoutDeletingFiles(bool audio)
    {
        using var fixture = new Fixture(audio);
        fixture.SetTotalDiscs(0);
        Assert.False(_service.CanSave(fixture.Root));
        Assert.Throws<InvalidOperationException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(fixture.PartialSource));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 5)]
    [InlineData(false, 5)]
    public void Save_DuringFirstDisc_PreservesProjectAndLoadsEmptyWorkspaceForDiscOne(bool audio, int totalDiscs)
    {
        using var fixture = new Fixture(audio);
        fixture.SetTotalDiscs(totalDiscs);
        fixture.RemoveCompleteDisc();
        var coverPath = Path.Combine(ProjectFolderLayout.GetSettingsFolder(fixture.Root), "cover.png");
        File.WriteAllBytes(coverPath, [1, 2, 3]);
        var canonicalMetadata = new ProjectMetadata();
        canonicalMetadata.SetValue(ProjectMetadataField.Title, "Saved first disc", MetadataOrigin.User);
        canonicalMetadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Author A", "Author B"], MetadataOrigin.User);
        canonicalMetadata.SetCover(coverPath, coverPath, MetadataOrigin.User);
        new ProjectMetadataStore().Save(fixture.Root, canonicalMetadata);
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(fixture.Root);
        var metadata = File.ReadAllText(metadataPath);

        Assert.True(_service.CanSave(fixture.Root));
        _service.Save(fixture.Root);
        _service.Save(fixture.Root);

        Assert.Equal(metadata, File.ReadAllText(metadataPath));
        Assert.True(File.Exists(coverPath));
        foreach (var path in new[] { fixture.FirstSource, fixture.PartialSource, fixture.FirstConverted,
                     fixture.PartialConverted, fixture.UnfinishedConversion, fixture.MergeFile })
            Assert.False(File.Exists(path));
        Assert.Empty(new SourceCopyIdentityStore().Load(fixture.Root));
        Assert.Empty(Read<ExportWorkManifest>(ProjectFolderLayout.GetExportManifestPath(fixture.Root)).Tracks);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.True(plan.CanResume);
        Assert.True(plan.IsStoppedDiscProject);
        Assert.Equal(totalDiscs, plan.TotalDiscs);
        Assert.Equal(Preset, plan.SelectedPreset);
        Assert.Equal("Saved first disc", plan.BookTitle);
        Assert.Contains("Author A", plan.Author);
        Assert.Equal(0, plan.ImportedDiscCount);
        Assert.Equal(1, plan.NextMissingDiscNumber);
        Assert.Empty(plan.Tracks);
        var loaded = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.True(loaded.Success, loaded.ErrorMessage);
        Assert.NotNull(loaded.CanonicalMetadata);
        Assert.Equal(new[] { "Author A", "Author B" },
            loaded.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Empty(loaded.Tracks);
        Assert.Equal(ProjectPipelineState.AcquiringSources, loaded.PipelineState);
        Assert.True(audio ? loaded.IsAudioDiscProjectAwaitingRip : loaded.NeedsMp3DiscImport);
        Assert.True(Assert.Single(new ProjectIndexService().ScanSelectableProjects(fixture.Root)).CanResume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_MissingRetainedSource_FailsBeforeAnyCommit(bool audio)
    {
        using var fixture = new Fixture(audio);
        File.Delete(fixture.FirstSource);
        Assert.Throws<IOException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(fixture.PartialSource));
        Assert.False(_service.HasPendingCommit(fixture.Root));
    }

    [Fact]
    public void Save_FailedDeletion_KeepsJournalAndRecoveryCompletesAfterFileIsReleased()
    {
        using var fixture = new Fixture(true);
        using (var locked = new FileStream(fixture.PartialSource, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => _service.Save(fixture.Root));
            Assert.True(_service.HasPendingCommit(fixture.Root));
            Assert.Throws<IOException>(() => _service.Recover(fixture.Root));
            Assert.True(File.Exists(fixture.FirstSource));
        }
        // Loading after a restart automatically finishes the persisted transaction.
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.Equal(2, plan.NextMissingDiscNumber);
        Assert.False(File.Exists(fixture.PartialSource));
        Assert.False(_service.HasPendingCommit(fixture.Root));
    }

    [Fact]
    public void Save_FailedJsonWrite_CanBeRetriedWithoutLosingSources()
    {
        using var fixture = new Fixture(true);
        using (var locked = new FileStream(ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => _service.Save(fixture.Root));
            Assert.True(_service.HasPendingCommit(fixture.Root));
            Assert.True(File.Exists(fixture.PartialSource));
        }
        _service.Save(fixture.Root);
        Assert.False(_service.HasPendingCommit(fixture.Root));
        Assert.True(File.Exists(fixture.FirstSource));
    }

    [Fact]
    public void Save_SourceOutsideProject_IsRejectedAndExternalFileIsPreserved()
    {
        using var fixture = new Fixture(true);
        using var external = new TemporaryFolder();
        var source = external.CreateFile("original.flac");
        File.WriteAllBytes(source, [1]);
        var path = ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root);
        var manifest = Read<AudioDiscProjectManifest>(path);
        manifest.Discs[0].Tracks[0].RelativePath = source;
        Write(path, manifest);
        Assert.Throws<IOException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(fixture.PartialSource));
    }

    [Fact]
    public void Resume_MissingSource_IsRejectedBeforeWorkspaceLoad()
    {
        using var fixture = new Fixture(true);
        _service.Save(fixture.Root);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        File.Delete(fixture.FirstSource);
        Assert.Throws<IOException>(() => DiscProjectStopSaveService.ValidateResumeSources(plan));
    }

    [Fact]
    public void Save_Mp3LiveConversionWithoutExportManifest_IsRetained()
    {
        using var fixture = new Fixture(false);
        File.Delete(ProjectFolderLayout.GetExportManifestPath(fixture.Root));
        var folder = ProjectFolderLayout.GetConvertedPresetFolder(fixture.Root, ExportPreset.Parse(Preset).GetFolderName());
        var output = ConvertedTrackPathService.GetConvertedTrackPath(folder, fixture.FirstSource,
            new TrackInfo { FileName = Path.GetFileName(fixture.FirstSource) });
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(output, [1, 2]);
        File.SetLastWriteTimeUtc(output, File.GetLastWriteTimeUtc(fixture.FirstSource).AddSeconds(1));
        _service.Save(fixture.Root);
        Assert.True(File.Exists(output));
        Assert.False(File.Exists(fixture.PartialConverted));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedPreset_IsProtectedBySnapshotServices_WhileOutputRemainsEditable(bool audio)
    {
        using var fixture = new Fixture(audio);
        _service.Save(fixture.Root);
        if (audio)
        {
            var path = ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root);
            var manifest = Read<AudioDiscProjectManifest>(path);
            var service = new AudioDiscProjectService();
            service.UpdateSnapshot(manifest, new ProjectSnapshotUiState(
                "AAC Stereo 320 kbps", "Auto", ".m4b", "new-output", "name", "new-title", "new-author", "new-album", "new-narrator", "new-genre", "", "", "new-file"));
            Assert.Equal(Preset, manifest.ExportPreset);
            Assert.Equal("new-output", manifest.OutputFolder);
        }
        else
        {
            var manifest = Read<Mp3DiscProjectManifest>(ProjectFolderLayout.GetWorkManifestPath(fixture.Root));
            new Mp3DiscProjectService().UpdateSettingsSnapshot(manifest, "AAC Stereo 320 kbps", "Auto", ".m4b", "new-output", "name");
            Assert.Equal(Preset, manifest.ExportPreset);
            Assert.Equal("new-output", manifest.OutputFolder);
        }
    }

    [Fact]
    public void Save_UnknownSchema_RejectsBeforeDeletingSources()
    {
        using var fixture = new Fixture(true);
        var path = ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root);
        var manifest = Read<AudioDiscProjectManifest>(path);
        manifest.SchemaVersion = 999;
        Write(path, manifest);
        Assert.False(_service.CanSave(fixture.Root));
        Assert.Throws<PersistentJsonSchemaException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(fixture.PartialSource));
    }

    [Fact]
    public void Resume_IncompleteMp3Sources_ReturnsFailureInsteadOfLoadingPartialDisc()
    {
        using var fixture = new Fixture(false);
        _service.Save(fixture.Root);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        File.Delete(fixture.FirstSource);
        var result = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resume_rebuilt_after_source_loss_allows_reacquisition_instead_of_blocking_stop_save_recovery(bool audio)
    {
        using var fixture = new Fixture(audio);
        _service.Save(fixture.Root);
        File.Delete(fixture.FirstSource);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.True(plan.CanContinueDiscImport);
        Assert.Equal(1, plan.NextMissingDiscNumber);
        var result = new ProjectResumeLoadWorkflowService(new AudioDiscProjectService(), new TrackListStateService()).Prepare(plan);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(ProjectPipelineState.AcquiringSources, result.PipelineState);
    }

    [Fact]
    public void Save_NullDiscCollection_IsRejectedWithoutCrashingAvailabilityOrDeletingFiles()
    {
        using var fixture = new Fixture(true);
        File.WriteAllText(ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Root),
            "{\"schemaVersion\":1,\"TotalDiscs\":5,\"Discs\":null}");
        Assert.False(_service.CanSave(fixture.Root));
        Assert.Throws<InvalidDataException>(() => _service.Save(fixture.Root));
        Assert.True(File.Exists(fixture.PartialSource));
    }

    [Fact]
    public void Recovery_ForgedExternalDeletePath_IsRejectedBeforeApplyingAnyWrite()
    {
        using var fixture = new Fixture(true);
        using var external = new TemporaryFolder();
        var source = external.CreateFile("untouched.mp3");
        var journal = Path.Combine(ProjectFolderLayout.GetSettingsFolder(fixture.Root), "disc-stop-save.pending.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            Writes = new[] { new { RelativePath = "project-settings/forged.json", Document = new { Corrupted = true } } },
            DeleteFiles = new[] { source }
        }));
        Assert.Throws<IOException>(() => _service.Recover(fixture.Root));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(ProjectFolderLayout.GetSettingsFolder(fixture.Root), "forged.json")));
    }

    [Fact]
    public void Save_ReadOnlyDiscCopies_DiscardsPartialDiscAndPreservesRetainedAttributes()
    {
        using var fixture = new Fixture(false);
        File.SetAttributes(fixture.FirstSource, File.GetAttributes(fixture.FirstSource) | FileAttributes.ReadOnly);
        File.SetAttributes(fixture.PartialSource, File.GetAttributes(fixture.PartialSource) | FileAttributes.ReadOnly);
        try
        {
            _service.Save(fixture.Root);
            Assert.False(File.Exists(fixture.PartialSource));
            Assert.True(File.Exists(fixture.FirstSource));
            Assert.True((File.GetAttributes(fixture.FirstSource) & FileAttributes.ReadOnly) != 0);
            Assert.False(_service.HasPendingCommit(fixture.Root));
        }
        finally
        {
            foreach (var file in new[] { fixture.FirstSource, fixture.PartialSource })
                if (File.Exists(file))
                    File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Started_new_cd_before_first_byte_uses_stop_save_and_resumes_disc_one(bool audio, bool directStart)
    {
        using var fixture = new Fixture(audio);
        fixture.RemoveCompleteDisc();
        foreach (var path in new[] { fixture.FirstSource, fixture.PartialSource, fixture.FirstConverted,
                     fixture.PartialConverted, fixture.UnfinishedConversion, fixture.MergeFile })
            File.Delete(path);
        var owner = new SessionProjectLifecycle();
        owner.Register(fixture.Root, audio ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.Mp3DiscProject, directStart);
        if (!directStart) owner.AcceptStart(fixture.Root);
        Assert.Equal(ProjectCloseIntent.Save, ApplicationShutdownService.ResolveProjectCloseIntent(
            owner.DeleteOnWindowClose(fixture.Root, true), false, false));
        _service.Save(fixture.Root);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.True(Directory.Exists(fixture.Root));
        Assert.Equal(1, plan.NextMissingDiscNumber);
        Assert.Equal(0, plan.ImportedDiscCount);
        Assert.Empty(plan.Tracks);
        Assert.False(_service.HasPendingCommit(fixture.Root));
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path))!;
    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryFolder _folder = new();
        private readonly bool _audio;
        public string Root { get; }
        public Guid FirstId { get; } = Guid.NewGuid();
        public Guid PartialId { get; } = Guid.NewGuid();
        public string FirstSource { get; }
        public string PartialSource { get; }
        public string FirstConverted { get; }
        public string PartialConverted { get; }
        public string UnfinishedConversion { get; }
        public string MergeFile { get; }
        public Fixture(bool audio, string? projectFolder = null)
        {
            Root = projectFolder ?? _folder.Path;
            _audio = audio;
            ProjectFolderLayout.EnsureProjectFolders(Root);
            FirstSource = FileAt("originals/CD 01/first." + (audio ? "flac" : "mp3"));
            PartialSource = FileAt("originals/CD 02/partial." + (audio ? "flac" : "mp3"));
            FirstConverted = FileAt("converted/aac_stereo_128k/first.m4a");
            PartialConverted = FileAt("converted/aac_stereo_128k/partial.m4a");
            UnfinishedConversion = FileAt("converted/aac_stereo_128k/uncompleted.part.m4a");
            MergeFile = FileAt("merge/result.part.m4b");
            if (audio)
                Write(ProjectFolderLayout.GetAudioDiscManifestPath(Root), new AudioDiscProjectManifest
                {
                    ProjectFolder = Root, TotalDiscs = 5, ExportPreset = Preset,
                    Discs = [AudioDisc(1, FirstSource, FirstId, true), AudioDisc(2, PartialSource, PartialId, false)]
                });
            else
                Write(ProjectFolderLayout.GetWorkManifestPath(Root), new Mp3DiscProjectManifest
                {
                    ProjectFolder = Root, TotalDiscs = 5, ExportPreset = Preset,
                    ImportedDiscs = [Mp3Disc(1, FirstSource, FirstId, true), Mp3Disc(2, PartialSource, PartialId, false)]
                });
            Write(ProjectFolderLayout.GetExportManifestPath(Root), new ExportWorkManifest
            {
                ProjectWorkFolder = Root, SelectedPreset = Preset,
                ProjectType = audio ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.Mp3DiscProject,
                Tracks = [ExportTrack(8, FirstSource, FirstConverted, FirstId), ExportTrack(9, PartialSource, PartialConverted, PartialId)],
                History = [new() { TrackIndex = 9 }, new() { DiscIndex = 2 }]
            });
            new TrackListStateService().Save(Root, [
                new TrackInfo { SourceId = PartialId, FilePath = PartialSource, Index = 9 },
                new TrackInfo { SourceId = FirstId, FilePath = FirstSource, Index = 8, IsExcluded = true }]);
            new SourceCopyIdentityStore().RecordCopiedFile(Root, FirstSource, FirstSource, FirstId);
            new SourceCopyIdentityStore().RecordCopiedFile(Root, PartialSource, PartialSource, PartialId);
            var snapshots = new SourceMetadataSnapshotStore();
            snapshots.Save(Root, new SourceMetadataSnapshot { SourceId = FirstId });
            snapshots.Save(Root, new SourceMetadataSnapshot { SourceId = PartialId });
            new ProjectMetadataStore().Save(Root, new ProjectMetadata());
        }
        public void SetTotalDiscs(int total) => Mutate(
            audio => audio.TotalDiscs = total, mp3 => mp3.TotalDiscs = total);
        public void RemoveCompleteDisc() => Mutate(
            audio => audio.Discs[0].Tracks[0].Status = AudioDiscTrackStatus.Pending,
            mp3 => mp3.ImportedDiscs[0].Status = "Importing");
        public void CompleteSecondDisc() => Mutate(
            audio => { audio.Discs[1].TrackCount = 1; audio.Discs[1].Status = AudioDiscStatus.Completed; audio.Discs[1].Tracks[0].Status = AudioDiscTrackStatus.Ripped; },
            mp3 => { mp3.ImportedDiscs[1].FileCount = 1; mp3.ImportedDiscs[1].CopiedFiles = 1; mp3.ImportedDiscs[1].Status = Mp3DiscImportStatus.Completed; });
        private void Mutate(Action<AudioDiscProjectManifest> audio, Action<Mp3DiscProjectManifest> mp3)
        {
            var path = _audio ? ProjectFolderLayout.GetAudioDiscManifestPath(Root) : ProjectFolderLayout.GetWorkManifestPath(Root);
            if (_audio) { var manifest = Read<AudioDiscProjectManifest>(path); audio(manifest); Write(path, manifest); }
            else { var manifest = Read<Mp3DiscProjectManifest>(path); mp3(manifest); Write(path, manifest); }
        }
        private string FileAt(string relative)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }
        private AudioDiscProjectManifestDisc AudioDisc(int number, string file, Guid id, bool complete) => new()
        {
            DiscNumber = number, Status = complete ? AudioDiscStatus.Completed : AudioDiscStatus.Ripping,
            TrackCount = complete ? 1 : 2,
            Tracks = [new() { DiscNumber = number, TrackNumber = 1, GlobalIndex = number + 7, SourceId = id,
                RelativePath = Path.GetRelativePath(Root, file), FileName = Path.GetFileName(file),
                Status = complete ? AudioDiscTrackStatus.Ripped : AudioDiscTrackStatus.Pending }]
        };
        private Mp3DiscProjectManifestDisc Mp3Disc(int number, string file, Guid id, bool complete) => new()
        {
            DiscNumber = number, Status = complete ? Mp3DiscImportStatus.Completed : "Importing",
            FileCount = complete ? 1 : 2, CopiedFiles = 1, LocalFolder = Path.GetDirectoryName(file)!,
            SourceFiles = [new() { SourceId = id, RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(Root), file),
                SourceFileName = Path.GetFileName(file), SourceSizeBytes = 3 }]
        };
        private static ExportWorkManifestTrack ExportTrack(int index, string source, string converted, Guid id) => new()
        {
            TrackIndex = index, SourceId = id, SourcePath = source, SourceFileName = Path.GetFileName(source),
            Status = ProjectManifestTrackStatuses.Converted, Preset = Preset, ConvertedPath = converted, ConvertedSizeBytes = 3
        };
        public void Dispose() => _folder.Dispose();
    }
}
