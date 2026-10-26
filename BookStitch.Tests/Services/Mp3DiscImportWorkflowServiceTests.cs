using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscImportWorkflowServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_copy_announces_the_source_once_and_scan_cancellation_announces_nothing(bool cancelAfterScan)
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "001.mp3", "one");
        CreateNonEmptyFile(source.Path, "002.mp3", "two");
        var notifications = new List<string>();
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path };
        using var cancel = new CancellationTokenSource();
        var result = await CreateService().ImportDiscAsync(source.Path, project.Path, manifest, 1, 1,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, null, _ =>
            {
                Assert.Empty(notifications);
                if (cancelAfterScan) cancel.Cancel();
            }, cancel.Token, sourceReadStarted: notifications.Add);
        Assert.Equal(cancelAfterScan, result.WasCanceled);
        if (cancelAfterScan) Assert.Empty(notifications);
        else Assert.Equal(source.Path, Assert.Single(notifications));
    }

    [Theory]
    [InlineData(true, PerformanceOutcome.Paused)]
    [InlineData(false, PerformanceOutcome.Cancelled)]
    public async Task Cancel_after_scan_preserves_workload_and_does_not_commit_a_partial_disc(bool pause, PerformanceOutcome expected)
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "001.mp3", "one");
        CreateNonEmptyFile(source.Path, "002.mp3", "two");
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path };
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        using var operation = trace.StartOperation("disc.mp3.import");
        using var phase = trace.StartPhase(operation.OperationId, "file.copy");
        using var cancellation = new CancellationTokenSource();
        var scanned = false;
        try
        {
            var result = await CreateService().ImportDiscAsync(source.Path, project.Path, manifest, 1, 2,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), null, null,
                precheck =>
                {
                    scanned = true;
                    trace.UpdateOperationWorkload(operation.OperationId, precheck.CreatePerformanceWorkload(precheck.TotalFiles));
                    cancellation.Cancel();
                }, cancellation.Token);
            Assert.True(result.WasCanceled);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Assert.True(scanned);
        phase.Cancel(pause);
        operation.Cancel(pause);
        var recorded = Assert.Single(trace.CompleteSession(PerformanceOutcome.Success)!.Operations);
        Assert.Equal(expected, recorded.Outcome);
        Assert.Equal(expected, Assert.Single(recorded.Phases).Outcome);
        Assert.Equal(1, recorded.Workload.DiscCount);
        Assert.Equal(2, recorded.Workload.FileCount);
        Assert.Equal(2, recorded.Workload.TrackCount);
        Assert.Equal(6, recorded.Workload.InputBytes);
        Assert.DoesNotContain(manifest.ImportedDiscs, disc => disc.Status == Mp3DiscImportStatus.Completed);
    }

    [Fact]
    public void CountCompletedCopiedFiles_SumsOnlyCompletedDiscs()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed, CopiedFiles = 12 },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = "Canceled", CopiedFiles = 7 },
                new Mp3DiscProjectManifestDisc { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed, CopiedFiles = 8 }
            ]
        };

        var service = CreateService();

        Assert.Equal(20, service.CountCompletedCopiedFiles(manifest));
    }

    [Fact]
    public void BuildCompletedDiscSignatureSet_ReturnsOnlyCompletedNonEmptySignatures()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed, Signature = "ABC" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = "Canceled", Signature = "DEF" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed, Signature = "" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 4, Status = Mp3DiscImportStatus.Completed, Signature = "abc" }
            ]
        };

        var service = CreateService();
        var signatures = service.BuildCompletedDiscSignatureSet(manifest);

        Assert.Single(signatures);
        Assert.Contains("ABC", signatures);
        Assert.Contains("abc", signatures);
        Assert.DoesNotContain("DEF", signatures);
    }

    [Fact]
    public void AnalyzeDiscForImport_DetectsAlreadyImportedSignature()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = folder.Path;
        CreateNonEmptyFile(sourceFolder, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(sourceFolder, "Track 02.mp3", "audio 2");
        var service = CreateService();
        var first = service.AnalyzeDiscForImport(sourceFolder, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first.Signature.ToLowerInvariant() };

        var second = service.AnalyzeDiscForImport(sourceFolder, importedSignatures);

        Assert.True(first.Analysis.IsSupportedDataDisc);
        Assert.Equal(2, first.TotalFiles);
        Assert.False(first.IsAlreadyImported);
        Assert.True(second.IsAlreadyImported);
        Assert.Equal(first.Signature, second.Signature);
    }

    [Fact]
    public void AnalyzeDiscForImport_UsesCatalogDataDiscFormatSet()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = folder.Path;
        CreateNonEmptyFile(sourceFolder, "001.mp3", "audio");
        CreateNonEmptyFile(sourceFolder, "002.m4a", "audio");
        CreateNonEmptyFile(sourceFolder, "003.m4b", "audio");
        CreateNonEmptyFile(sourceFolder, "004.aac", "audio");
        CreateNonEmptyFile(sourceFolder, "005.wav", "audio");
        CreateNonEmptyFile(sourceFolder, "006.flac", "audio");
        CreateNonEmptyFile(sourceFolder, "007.wma", "audio");
        var service = CreateService();

        var result = service.AnalyzeDiscForImport(sourceFolder, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(
            ["001.mp3", "002.m4a", "003.m4b", "004.aac", "005.wav", "006.flac", "007.wma"],
            result.Analysis.SupportedAudioFiles.Select(path => Path.GetFileName(path) ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task CopyDiscAsync_CopiesFilesAndReportsProgress()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(source.Path, Path.Combine("Sub", "Track 02.m4a"), "audio 2");
        var service = CreateService();
        var progressItems = new List<DiscCopyProgress>();
        var copiedItems = new List<DiscCopiedFile>();

        var result = await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 2,
            totalDiscs: 4,
            new CollectingProgress<DiscCopyProgress>(progressItems.Add),
            new CollectingProgress<DiscCopiedFile>(copiedItems.Add),
            CancellationToken.None);

        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.CopiedFiles);
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 2), "Track 01.mp3")));
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 2), "Sub", "Track 02.m4a")));
        Assert.Equal(2, progressItems.Count);
        Assert.Equal(2, copiedItems.Count);
        Assert.All(copiedItems, item => Assert.Equal(2, item.DiscNumber));
    }


    [Fact]
    public async Task ImportDiscAsync_CopiesDiscMarksCompletedAndAddsSignature()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(source.Path, "Track 02.mp3", "audio 2");
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var service = CreateService();
        var readyToCopyCalled = false;
        Guid[] plannedSourceIds = [];

        var result = await service.ImportDiscAsync(
            source.Path,
            project.Path,
            manifest,
            discNumber: 1,
            totalDiscs: 2,
            importedSignatures,
            progress: null,
            copiedFileProgress: null,
            onReadyToCopy: precheck =>
            {
                readyToCopyCalled = true;
                Assert.Equal(2, precheck.TotalFiles);
                plannedSourceIds = precheck.PlannedSources.Select(source => source.SourceId).OrderBy(id => id).ToArray();
            },
            CancellationToken.None);

        Assert.True(readyToCopyCalled);
        Assert.False(result.IsAlreadyImported);
        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.CopiedFiles);
        Assert.Single(importedSignatures);
        Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(Mp3DiscImportStatus.Completed, manifest.ImportedDiscs[0].Status);
        Assert.Equal(2, manifest.ImportedDiscs[0].CopiedFiles);
        Assert.Equal(
            plannedSourceIds,
            manifest.ImportedDiscs[0].SourceFiles.Select(source => source.SourceId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task ImportDiscAsync_WhenSignatureAlreadyImported_DoesNotCopyOrMarkCompleted()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var service = CreateService();
        var signature = service.AnalyzeDiscForImport(
            source.Path,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Signature;
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { signature };
        var readyToCopyCalled = false;
        var sourceReadCalled = false;

        var result = await service.ImportDiscAsync(
            source.Path,
            project.Path,
            manifest,
            discNumber: 1,
            totalDiscs: 2,
            importedSignatures,
            progress: null,
            copiedFileProgress: null,
            onReadyToCopy: _ => readyToCopyCalled = true,
            CancellationToken.None, sourceReadStarted: _ => sourceReadCalled = true);

        Assert.True(result.IsAlreadyImported);
        Assert.Null(result.ImportResult);
        Assert.False(readyToCopyCalled);
        Assert.False(sourceReadCalled);
        Assert.Empty(manifest.ImportedDiscs);
        Assert.False(Directory.Exists(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1)));
    }

    [Fact]
    public async Task ImportDiscAsync_DuplicateOverrideImportsWithoutChangingKnownSignatureHistory()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, ImportedDiscs = [] };
        var service = CreateService();
        var signature = service.AnalyzeDiscForImport(source.Path, new HashSet<string>()).Signature;
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { signature };

        var result = await service.ImportDiscAsync(
            source.Path, project.Path, manifest, discNumber: 2, totalDiscs: 2,
            importedSignatures, progress: null, copiedFileProgress: null,
            onReadyToCopy: null, CancellationToken.None, acceptDuplicate: true);

        Assert.False(result.IsAlreadyImported);
        Assert.Equal(1, result.CopiedFiles);
        Assert.Equal(signature, Assert.Single(importedSignatures));
        Assert.Equal(2, Assert.Single(manifest.ImportedDiscs).DiscNumber);
    }

    [Fact]
    public async Task Duplicate_disc_override_allocates_distinct_source_ids_and_reuses_them_on_retry()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(source.Path, "Track 02.mp3", "audio 2");
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, TotalDiscs = 2 };
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var service = CreateService();
        var readNotifications = 0;
        async Task<Mp3DiscSingleDiscImportResult> Import(int disc) => await service.ImportDiscAsync(
            source.Path, project.Path, manifest, disc, 2, signatures,
            null, null, null, CancellationToken.None, acceptDuplicate: disc == 2, sourceReadStarted: _ => readNotifications++);
        var first = await Import(1);
        var duplicate = await Import(2);
        var firstIds = first.Precheck.PlannedSources.Select(item => item.SourceId).ToArray();
        var duplicateIds = duplicate.Precheck.PlannedSources.Select(item => item.SourceId).ToArray();
        Assert.Equal(4, firstIds.Concat(duplicateIds).Distinct().Count());
        Assert.Equal(duplicateIds, manifest.ImportedDiscs.Single(disc => disc.DiscNumber == 2)
            .SourceFiles.Select(item => item.SourceId));
        Assert.Equal(2, readNotifications);
        var retry = await Import(2);
        Assert.Equal(2, readNotifications);
        Assert.Equal(duplicateIds, retry.Precheck.PlannedSources.Select(item => item.SourceId));
        Assert.Single(signatures);
        Assert.Equal(2, manifest.ImportedDiscs.Count);
    }

    [Fact]
    public void MarkDiscCompleted_UpdatesManifestThroughProjectService()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var importResult = new DiscImportResult(
            source.Path,
            ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1),
            project.Path,
            DiscNumber: 1,
            TotalDiscs: 3,
            CopiedFiles: 5,
            ImportedFiles:
            [
                new SourceFileCopyIdentity(
                    Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", "001.mp3"),
                    "001.mp3",
                    123,
                    456)
            ]);
        var service = CreateService();

        service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "SIGNATURE",
            source.Path,
            importResult,
            fileCount: 5);

        var disc = Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(1, disc.DiscNumber);
        Assert.Equal(Mp3DiscImportStatus.Completed, disc.Status);
        Assert.Equal("SIGNATURE", disc.Signature);
        Assert.Equal(importResult.ImportedFolder, disc.LocalFolder);
        Assert.Equal(5, disc.FileCount);
        Assert.Equal(5, disc.CopiedFiles);
        var sourceFile = Assert.Single(disc.SourceFiles);
        Assert.Equal(Path.Combine("CD 01", "001.mp3"), sourceFile.RelativePath);
        Assert.Equal("001.mp3", sourceFile.SourceFileName);
        Assert.Equal(123, sourceFile.SourceSizeBytes);
        Assert.Equal(456, sourceFile.SourceLastWriteUtcTicks);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task Resume_preview_keeps_user_edits_and_planned_ids_including_repeated_media(int completed, bool repeated)
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "001.mp3", "first");
        CreateNonEmptyFile(source.Path, "002.mp3", "second");
        var service = CreateService();
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, TotalDiscs = 2 };
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (completed == 1)
        {
            await service.ImportDiscAsync(source.Path, project.Path, manifest, 1, 2,
                signatures, null, null, null, default);
            if (!repeated) File.AppendAllText(Path.Combine(source.Path, "001.mp3"), "new medium");
        }
        var number = completed + 1;
        var scan = service.AnalyzeDiscForImport(source.Path, signatures);
        var prepared = new Mp3DiscPendingChapterDisc(number, scan.Signature,
            service.ReuseExistingSourceIds(project.Path, number, scan.PlannedSources, manifest));
        var reconciliation = new Mp3DiscTrackReconciliationService();
        var current = manifest.ImportedDiscs.SelectMany(disc => disc.SourceFiles.Select(item =>
            new TrackInfo { SourceId = item.SourceId, DiscNumber = disc.DiscNumber, FileName = item.SourceFileName,
                FilePath = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(project.Path), item.RelativePath) })).ToList();
        var visible = current.ToList();
        var preview = reconciliation.CreatePreviewTracksFromKnownFiles(source.Path, scan.Analysis.SupportedAudioFiles);
        reconciliation.NormalizeProjectDiscNumber(preview, number);
        reconciliation.ApplyPlannedSourceIds(preview, source.Path, number, prepared.Sources);
        reconciliation.AppendMissingPreviewTracks(visible, preview, source.Path, number, clearExistingTracks: false);
        reconciliation.AppendMissingPreviewTracks(visible, preview, source.Path, number, clearExistingTracks: false);
        Assert.Equal((completed + 1) * 2, visible.Count);
        foreach (var track in current) Assert.Same(track, visible.Single(item => item.SourceId == track.SourceId));
        visible.Reverse();
        visible[0].IsExcluded = true;
        visible[0].ExcludedChapterTitle = "Excluded user chapter";
        visible[1].ChapterTitle = "User chapter";
        new TrackListStateService().Save(project.Path, visible);
        Assert.Equal(completed, manifest.ImportedDiscs.Count);
        var result = await service.ImportDiscAsync(source.Path, project.Path, manifest, number, 2,
            signatures, null, null, precheck =>
            {
                Assert.Equal(prepared.Sources.Select(item => item.SourceId), precheck.PlannedSources.Select(item => item.SourceId));
                reconciliation.ApplyPlannedSourceIds(visible, source.Path, number, precheck.PlannedSources);
            }, default, acceptDuplicate: repeated, preparedDisc: prepared);
        Assert.False(result.WasCanceled);
        var restored = new TrackListStateService().Apply(project.Path, manifest.ImportedDiscs.SelectMany(disc => disc.SourceFiles.Select(item =>
            new TrackInfo { SourceId = item.SourceId, FilePath = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(project.Path), item.RelativePath),
                FileName = item.SourceFileName })));
        Assert.Equal(visible.Select(track => track.SourceId), restored.Select(track => track.SourceId));
        Assert.True(restored[0].IsExcluded);
        Assert.Equal("Excluded user chapter", restored[0].ExcludedChapterTitle);
        Assert.Equal("User chapter", restored[1].ChapterTitle);
        Assert.Equal(completed + 1, manifest.ImportedDiscs.Count);
    }

    [Fact]
    public async Task Changed_mp3_medium_does_not_reuse_the_preview_source_ids()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "001.mp3", "first");
        var service = CreateService();
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, TotalDiscs = 1 };
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scan = service.AnalyzeDiscForImport(source.Path, signatures);
        var preview = new Mp3DiscPendingChapterDisc(1, scan.Signature, scan.PlannedSources);
        File.AppendAllText(Path.Combine(source.Path, "001.mp3"), "changed");
        var result = await service.ImportDiscAsync(source.Path, project.Path, manifest, 1, 1,
            signatures, null, null, precheck => Assert.NotEqual(preview.Sources[0].SourceId, precheck.PlannedSources[0].SourceId),
            default, preparedDisc: preview);
        Assert.NotEqual(preview.Signature, result.Precheck.Signature);
    }

    [Theory]
    [InlineData("duplicate", DiscPollingDisplayState.Duplicate)]
    [InlineData("audio", DiscPollingDisplayState.Unsupported)]
    [InlineData("empty", DiscPollingDisplayState.Waiting)]
    [InlineData("new", DiscPollingDisplayState.Ready)]
    public async Task Extension_entry_polling_preserves_two_completed_discs_then_imports_and_rechecks_next_disc(
        string medium, DiscPollingDisplayState expectedState)
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        var sourceRoot = Path.Combine(source.Path, "drive");
        var projectService = new Mp3DiscProjectService();
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, TotalDiscs = 2,
            PipelineState = ProjectPipelineStateNames.ReviewBeforeMerge };
        var workflow = CreateService();
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var disc = 1; disc <= 2; disc++)
        {
            foreach (var file in Directory.Exists(sourceRoot) ? Directory.GetFiles(sourceRoot) : []) File.Delete(file);
            CreateNonEmptyFile(sourceRoot, "disc-" + disc + ".mp3", "source " + disc);
            var result = await workflow.ImportDiscAsync(sourceRoot, project.Path, manifest, disc, 2,
                signatures, null, null, null, default);
            Assert.Equal(1, result.CopiedFiles);
        }
        projectService.Save(manifest);
        var rollback = new ProjectExtensionRollbackService();
        var snapshot = rollback.Capture(project.Path);
        var manifestPath = Mp3DiscProjectService.GetManifestPath(project.Path);
        var originalManifest = File.ReadAllBytes(manifestPath);
        projectService.IncreaseTotalDiscsForAdditionalImport(manifest, 4);
        projectService.Save(manifest);
        if (medium != "duplicate")
        {
            foreach (var file in Directory.GetFiles(sourceRoot)) File.Delete(file);
            if (medium == "empty") Directory.Delete(sourceRoot);
            if (medium == "audio") CreateNonEmptyFile(sourceRoot, "Track01.cda", "audio CD shortcut");
            if (medium == "new") CreateNonEmptyFile(sourceRoot, "disc-3.mp3", "new source");
        }
        var ejected = new List<string?>();
        var polling = new Mp3DiscPollingService(new Mp3DiscImportService(), new DiscDriveService(),
            path => { ejected.Add(path); return true; });
        var persistedBefore = File.ReadAllBytes(manifestPath);
        var inMemoryBefore = System.Text.Json.JsonSerializer.Serialize(manifest);
        var check = await polling.CheckDiscSourceForNextImportAsync(sourceRoot, 3, 4, signatures);
        Assert.Equal(expectedState, check.DisplayState);
        Assert.Equal(medium == "new", check.CanImport);
        Assert.Equal(medium is "duplicate" or "audio" ? new[] { sourceRoot } : [], ejected);
        Assert.Equal(persistedBefore, File.ReadAllBytes(manifestPath));
        Assert.Equal(inMemoryBefore, System.Text.Json.JsonSerializer.Serialize(manifest));
        Assert.False(manifest.IsStoppedForResume);
        Assert.Equal(2, manifest.ImportedDiscs.Count);
        if (!check.CanImport)
        {
            foreach (var file in Directory.Exists(sourceRoot) ? Directory.GetFiles(sourceRoot) : []) File.Delete(file);
            CreateNonEmptyFile(sourceRoot, "disc-3.mp3", "new source");
            Assert.True((await polling.CheckDiscSourceForNextImportAsync(sourceRoot, 3, 4, signatures)).CanImport);
        }
        var third = await workflow.ImportDiscAsync(sourceRoot, project.Path, manifest, 3, 4,
            signatures, null, null, null, default);
        Assert.Equal(1, third.CopiedFiles);
        Assert.Equal(3, manifest.ImportedDiscs.Count);
        Assert.Equal(DiscPollingDisplayState.Duplicate,
            (await polling.CheckDiscSourceForNextImportAsync(sourceRoot, 4, 4, signatures)).DisplayState);
        Assert.True((await polling.CheckDiscSourceForNextImportAsync(sourceRoot, 4, 4, signatures, acceptDuplicate: true)).CanImport);
        var fourth = await workflow.ImportDiscAsync(sourceRoot, project.Path, manifest, 4, 4,
            signatures, null, null, null, default, acceptDuplicate: true);
        Assert.Equal(1, fourth.CopiedFiles);
        Assert.Equal(new[] { 1, 2, 3, 4 }, manifest.ImportedDiscs.Select(disc => disc.DiscNumber));
        Assert.False((await polling.CheckDiscSourceForNextImportAsync(sourceRoot, 5, 5, signatures)).CanImport);
        rollback.Rollback(snapshot);
        Assert.Equal(originalManifest, File.ReadAllBytes(manifestPath));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 3)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 4)));
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1), "disc-1.mp3")));
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 2), "disc-2.mp3")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paused_mp3_attempt_reuses_completed_copies_source_ids_and_its_own_duplicate_permission(bool duplicate)
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        var workflow = CreateService();
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = project.Path, TotalDiscs = 5 };
        for (var disc = 1; disc <= 2; disc++)
        {
            for (var index = 1; index <= 4; index++) CreateNonEmptyFile(source.Path, index + ".mp3", "original " + disc + new string('x', disc));
            await workflow.ImportDiscAsync(source.Path, project.Path, manifest, disc, 5, signatures, null, null, null, default);
        }
        if (!duplicate)
            for (var index = 1; index <= 4; index++) CreateNonEmptyFile(source.Path, index + ".mp3", "new disc " + index);
        var owner = new DiscResumePreviewState();
        using var cancel = new CancellationTokenSource();
        var copied = new List<DiscCopiedFile>();
        var conversionRelease = new TaskCompletionSource();
        var conversionTasks = new List<Task>();
        var part = await workflow.ImportDiscAsync(source.Path, project.Path, manifest, 3, 5, signatures,
            null, new CollectingProgress<DiscCopiedFile>(file =>
            {
                copied.Add(file);
                conversionTasks.Add(Task.Run(async () => await conversionRelease.Task));
                if (copied.Count == 2) cancel.Cancel();
            }), precheck =>
            {
                owner.Set(new(3, precheck.Signature, precheck.PlannedSources), duplicate);
                owner.MarkProcessingStarted();
            }, cancel.Token, acceptDuplicate: duplicate);
        Assert.True(part.WasCanceled);
        Assert.Equal(2, part.CopiedFiles);
        Assert.Equal(2, copied.Count);
        Assert.All(conversionTasks, task => Assert.False(task.IsCompleted));
        Assert.DoesNotContain(manifest.ImportedDiscs, disc => disc.DiscNumber == 3);
        var originals = copied.Select(file => (TargetFile: file.ImportedFile, Bytes: File.ReadAllBytes(file.ImportedFile),
            Modified: File.GetLastWriteTimeUtc(file.ImportedFile))).ToArray();
        var ids = owner.Mp3Disc!.Sources.Select(item => item.SourceId).ToArray();
        conversionRelease.SetResult();
        await Task.WhenAll(conversionTasks);
        var check = workflow.AnalyzeDiscForImport(source.Path, signatures);
        Assert.True(owner.CanResumeMp3Import(3, check.Signature));
        Assert.Equal(duplicate, check.IsAlreadyImported);
        var resumedCopies = new List<DiscCopiedFile>();
        var resumed = await workflow.ImportDiscAsync(source.Path, project.Path, manifest, 3, 5, signatures,
            null, new CollectingProgress<DiscCopiedFile>(resumedCopies.Add), null, default,
            acceptDuplicate: owner.Mp3DuplicateAccepted, preparedDisc: owner.Mp3Disc);
        Assert.False(resumed.WasCanceled);
        Assert.Equal(4, resumed.CopiedFiles);
        Assert.Equal(2, resumedCopies.Count); // Already complete files do not restart live conversion.
        var completed = manifest.ImportedDiscs.Single(disc => disc.DiscNumber == 3);
        Assert.Equal(ids.Order(), completed.SourceFiles.Select(file => file.SourceId).Order());
        Assert.Empty(manifest.ImportedDiscs.Where(disc => disc.DiscNumber < 3).SelectMany(disc => disc.SourceFiles)
            .Select(file => file.SourceId).Intersect(ids));
        foreach (var original in originals)
        {
            Assert.Equal(original.Bytes, File.ReadAllBytes(original.TargetFile));
            Assert.Equal(original.Modified, File.GetLastWriteTimeUtc(original.TargetFile));
        }
        owner.Clear();
        Assert.False(owner.Mp3DuplicateAccepted);
        var next = await workflow.ImportDiscAsync(source.Path, project.Path, manifest, 4, 5, signatures, null, null, null, default);
        Assert.True(next.IsAlreadyImported);
        Assert.DoesNotContain(manifest.ImportedDiscs, disc => disc.DiscNumber == 4);
    }

    private static Mp3DiscImportWorkflowService CreateService()
    {
        return new Mp3DiscImportWorkflowService(
            new Mp3DiscImportService(),
            new Mp3DiscProjectService());
    }

    private static string CreateNonEmptyFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private sealed class CollectingProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
