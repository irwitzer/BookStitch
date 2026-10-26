using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportWorkflowServiceTests
{
    [Fact]
    public async Task Paused_merge_discard_restores_review_then_reload_allows_merge_without_repreparation()
    {
        using var folder = new TemporaryFolder(); var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Audio.MergeAsync = (_, _, part, token) => { File.WriteAllText(part, "partial"); throw new OperationCanceledException(token); };
        var paused = await fixture.Service.RunAsync(fixture.Request,
            new ExportWorkflowCallbacks { MergeAutomaticallyAfterConversion = () => true, IsPauseRequested = () => true }, default);
        Assert.Equal(ExportWorkflowResultStatus.Canceled, paused.Status);
        var baseline = new ProjectExtensionRollbackService().GetActive(fixture.ProjectFolder)!;
        Assert.NotNull(baseline);
        var aac = fixture.LoadManifest().Tracks.ToDictionary(t => t.ConvertedPath, t => File.ReadAllBytes(t.ConvertedPath));
        // Window-X/Back leaves the same owner and paused boundary intact; explicit discard owns rollback.
        Assert.Same(baseline, new ProjectExtensionRollbackService().GetActive(fixture.ProjectFolder));
        new ProjectExtensionRollbackService().Rollback(baseline);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, fixture.LoadManifest().State.Status);
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(fixture.ProjectFolder)));
        Assert.False(new ProjectExtensionRollbackService().Recover(fixture.ProjectFolder));
        var load = new ProjectResumePlanService().BuildFromProjectFolder(fixture.ProjectFolder)!;
        Assert.False(load.IsMergePaused);
        fixture.RecreateService(); fixture.Audio.MergeAsync = null;
        var prepared = fixture.Audio.PrepareTrackCalls;
        var result = await fixture.Service.RunAsync(fixture.Request with { ManualMergeRequested = true }, fixture.CreateCallbacks(autoMerge: () => false), default);
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(prepared, fixture.Audio.PrepareTrackCalls);
        Assert.All(aac, pair => Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key)));
        Assert.True(File.Exists(fixture.Plan.FinalOutputPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Merge_interruption_cleans_parts_preserves_preparation_and_persists_only_pause(bool pause)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Audio.MergeAsync = (_, _, part, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(part)!);
            File.WriteAllText(part, "incomplete");
            throw new OperationCanceledException(token);
        };
        var result = await fixture.Service.RunAsync(fixture.Request, new ExportWorkflowCallbacks
        { MergeAutomaticallyAfterConversion = () => true, IsPauseRequested = () => pause }, default);
        Assert.Equal(ExportWorkflowResultStatus.Canceled, result.Status);
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
        var manifest = fixture.LoadManifest();
        Assert.Equal(pause, manifest.Resume.MergePaused);
        Assert.All(manifest.Tracks, track => Assert.True(File.Exists(track.ConvertedPath)));
        Assert.All(fixture.Tracks, track => Assert.True(File.Exists(track.FilePath)));
        var preparations = fixture.Audio.PrepareTrackCalls;
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.ProjectFolder)!;
        Assert.Equal(pause, plan.IsMergePaused);
        fixture.Audio.MergeAsync = null;
        var resumed = await fixture.Service.RunAsync(fixture.Request with { ManualMergeRequested = true },
            fixture.CreateCallbacks(autoMerge: () => false), default);
        Assert.Equal(ExportWorkflowResultStatus.Completed, resumed.Status);
        Assert.Equal(preparations, fixture.Audio.PrepareTrackCalls);
        Assert.False(fixture.LoadManifest().Resume.MergePaused);
    }

    [Theory]
    [InlineData(true, PerformanceOutcome.Paused)]
    [InlineData(false, PerformanceOutcome.Cancelled)]
    public async Task Diagnostic_phase_classifies_pause_without_changing_cancelled_export_result(bool pause, PerformanceOutcome expected)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Audio.PrepareTrackAsync = (_, _, _, token) => throw new OperationCanceledException(token);
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        using var operation = trace.StartOperation("export.run");
        var context = new ExportPerformanceContext(name => trace.StartPhase(operation.OperationId, name),
            (phase, purpose) => new(trace, operation.OperationId, phase, purpose), () => pause);
        var result = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None, context);
        Assert.Equal(ExportWorkflowResultStatus.Canceled, result.Status);
        operation.Cancel(pause);
        var recorded = Assert.Single(trace.CompleteSession(PerformanceOutcome.Success)!.Operations);
        Assert.Equal(expected, recorded.Outcome);
        Assert.Equal(expected, recorded.Phases.Single(phase => phase.Name == "export.convert").Outcome);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task Review_waits_for_timing_children_and_failed_timing_retries_without_audio_preparation()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<ProjectPipelineState>();
        fixture.Audio.AnalyzeTimingAsync = async (_, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            throw new InvalidDataException("probe failed");
        };
        var run = fixture.Service.RunAsync(fixture.Request, new ExportWorkflowCallbacks
        {
            MergeAutomaticallyAfterConversion = () => false,
            SetPipelineState = state => states.Add(state)
        }, CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.DoesNotContain(ProjectPipelineState.ReviewBeforeMerge, states);
            Assert.False(run.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, (await run).Status);
        var preparedCount = fixture.Audio.PrepareTrackCalls;
        Assert.All(fixture.LoadManifest().Tracks, entry =>
        {
            Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
            Assert.NotEqual(Guid.Empty, entry.PreparationId);
            Assert.Null(entry.AacTiming);
        });
        var failed = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Failed, failed.Status);
        Assert.Equal(preparedCount, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(0, fixture.Audio.MergeCalls);
        fixture.Audio.AnalyzeTimingAsync = null;
        var resumed = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, resumed.Status);
        Assert.Equal(preparedCount, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auto_merge_off_requires_real_export_review_even_when_UI_already_indicates_review(bool cached)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        if (cached) fixture.SeedReusableManifest();
        var callbacks = new ExportWorkflowCallbacks
        {
            MergeAutomaticallyAfterConversion = () => false
        };
        var first = await fixture.Service.RunAsync(fixture.Request, callbacks, CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, first.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
        fixture.RecreateService();
        var next = await fixture.Service.RunAsync(fixture.Request, callbacks, CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, next.Status);
        var freshStart = await fixture.Service.RunAsync(fixture.Request, new ExportWorkflowCallbacks
        {
            MergeAutomaticallyAfterConversion = () => false
        }, CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, freshStart.Status);
        var manifest = fixture.LoadManifest();
        File.Delete(manifest.Tracks[0].ConvertedPath);
        var recovery = await fixture.Service.RunAsync(fixture.Request, callbacks, CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, recovery.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task Cache_changed_after_reconciliation_cannot_be_legitimized_by_new_timing_analysis()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.SeedReusableManifest();
        fixture.Audio.AnalyzeTiming = (path, cached) =>
        {
            var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes);
            return new ConvertedAacTiming(1, bytes.Length, File.GetLastWriteTimeUtc(path).Ticks,
                TimeSpan.TicksPerSecond, 1, TimeSpan.TicksPerSecond);
        };
        var result = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Failed, result.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
    }

    [Fact]
    public async Task Repeated_source_entries_prepare_separately_and_merge_in_order()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Tracks[1].FilePath = fixture.Tracks[0].FilePath;
        fixture.Tracks[1].FileName = "Repeated source.m4a";
        fixture.Tracks[1].DurationTicks = fixture.Tracks[0].DurationTicks;
        fixture.Tracks[0].SourceId = Guid.NewGuid();
        fixture.Tracks[1].SourceId = fixture.Tracks[0].SourceId;
        var result = await fixture.Service.RunAsync(fixture.Request with { ChapterSettings = new ChapterSettings { Mode = ChapterSettingsService.ModeAuto } }, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.True(result.Status == ExportWorkflowResultStatus.Completed, result.Error?.ToString());
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(2, fixture.LoadManifest().Tracks.Count);
        var files = fixture.Audio.LastConcat.Split('\n').Where(line => line.StartsWith("file ")).ToArray();
        Assert.Equal(2, files.Length); Assert.NotEqual(files[0], files[1]);
    }

    [Fact]
    public async Task Processing_action_change_replaces_cache_entry_and_reaches_merge()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.SeedReusableManifest();
        fixture.Tracks[0].ProcessingAction = ProcessingActionIds.Convert;
        var result = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(1, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.Equal(2, fixture.LoadManifest().Tracks.Count);
        Assert.Contains(fixture.LoadManifest().Tracks, entry => entry.Action == ProcessingActionIds.Convert);
    }

    [Fact]
    public async Task Committed_output_cleanup_failure_keeps_success_and_completed_manifest()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var cleanupCalls = 0;
        fixture.Output = new FinalOutputStorageService(_ =>
        { cleanupCalls++; throw new IOException("Cleanup denied"); });
        fixture.RecreateService();
        var result = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(1, cleanupCalls);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(ProjectManifestStatuses.Completed, fixture.LoadManifest().State.Status);
    }

    [Fact]
    public async Task Post_commit_notification_failure_does_not_reclassify_export_as_failed()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var result = await fixture.Service.RunAsync(fixture.Request,
            fixture.CreateCallbacks(setPipelineState: state =>
            { if (state == ProjectPipelineState.Completed) throw new IOException("Notification failed"); }), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(ProjectManifestStatuses.Completed, fixture.LoadManifest().State.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_AutoAndManualMergeUseConvertedTimelineAcrossReloadAndMetadataExport(bool manual)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: manual);
        fixture.Tracks[1].EmbeddedChapters =
        [new TrackEmbeddedChapterInfo { Title = "Embedded", StartTicks = TimeSpan.TicksPerSecond,
            EndTicks = 3 * TimeSpan.TicksPerSecond }];
        fixture.Audio.AnalyzeTiming = (path, cached) => cached ?? new ConvertedAacTiming(
            1, 1, 1, 5 * TimeSpan.TicksPerSecond, 1, TimeSpan.TicksPerSecond);
        var first = await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        if (manual)
        {
            Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, first.Status);
            fixture.RecreateService();
            first = await fixture.Service.RunAsync(fixture.Request with { ManualMergeRequested = true },
                fixture.CreateCallbacks(), CancellationToken.None);
        }
        Assert.Equal(ExportWorkflowResultStatus.Completed, first.Status);
        Assert.Contains("START=6000", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
        Assert.Contains("duration 5.000000", fixture.Audio.LastConcat, StringComparison.Ordinal);
        var expected = fixture.Audio.LastChapterMetadata;
        Assert.All(fixture.LoadManifest().Tracks, track => Assert.NotNull(track.AacTiming));
        fixture.RecreateService();
        var next = await fixture.Service.RunAsync(fixture.Request with
            { ManualMergeRequested = manual, FinalTags = new FinalAudioTagData { Title = "Changed metadata" } },
            fixture.CreateCallbacks(resolveFinalOutputConflict: (_, _) => FinalOutputConflictAction.Overwrite), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, next.Status);
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(expected, fixture.Audio.LastChapterMetadata);
        Assert.Equal(2, fixture.Audio.TimingCalls);
    }

    [Fact]
    public async Task RunAsync_CompletesHappyPathAndPersistsCompletedManifest()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var pipelineStates = new List<ProjectPipelineState>();
        var phaseNames = new List<string>();

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(setPipelineState: pipelineStates.Add),
            CancellationToken.None,
            CreatePhaseRecorder(phaseNames));

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(ProjectManifestStatuses.Completed, manifest.State.Status);
        Assert.Equal("ExportCompleted", manifest.State.LastSuccessfulStep);
        Assert.False(manifest.Resume.CanResume);
        Assert.True(File.Exists(fixture.Plan.FinalOutputPath));
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
        Assert.False(Directory.EnumerateFiles(fixture.Plan.ProjectWorkFolder, "*.part", SearchOption.AllDirectories).Any());
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.Equal(1, fixture.Tags.WriteCalls);
        Assert.Contains(ProjectPipelineState.Converting, pipelineStates);
        Assert.Contains(ProjectPipelineState.Merging, pipelineStates);
        Assert.Contains(ProjectPipelineState.Completed, pipelineStates);
        Assert.Contains("export.prepare", phaseNames);
        Assert.Contains("export.convert", phaseNames);
        Assert.Contains("export.timing", phaseNames);
        Assert.Contains("export.merge", phaseNames);
        Assert.Contains("export.inspect", phaseNames);
        Assert.Contains("export.tag", phaseNames);
        Assert.Contains("export.validate", phaseNames);
        Assert.Contains("export.output", phaseNames);
        Assert.Contains("export.cleanup", phaseNames);
        Assert.Contains("export.finalize", phaseNames);
    }

    [Fact]
    public async Task RunAsync_RebuildsChapterNumberWidthImmediatelyBeforeMerge()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var tracks = Enumerable.Range(1, 9)
            .Select(index => ExportWorkflowFixture.CreateTrack(
                fixture.SourceFolder,
                index,
                $"Track {index:000}.m4a",
                TimeSpan.FromSeconds(1)))
            .ToList();
        foreach (var track in tracks)
            File.WriteAllText(track.FilePath, "source " + track.Index);

        fixture.Audio.PrepareTrackAsync = (track, sourcePath, convertedPath, _) =>
        {
            if (track.Index == 9)
            {
                track.EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Final split A",
                        StartTicks = 0,
                        EndTicks = TimeSpan.FromMilliseconds(500).Ticks
                    },
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Final split B",
                        StartTicks = TimeSpan.FromMilliseconds(500).Ticks,
                        EndTicks = TimeSpan.FromSeconds(1).Ticks
                    }
                ];
            }

            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.Copy(sourcePath, convertedPath, overwrite: true);
            return Task.CompletedTask;
        };

        var totalTicks = tracks.Sum(track => track.DurationTicks ?? 0);
        var plan = fixture.Plan with
        {
            TrackSnapshot = tracks,
            TotalDuration = TimeSpan.FromTicks(totalTicks),
            TotalTicks = totalTicks
        };
        var request = fixture.Request with
        {
            Plan = plan,
            ChapterSettings = new ChapterSettings
            {
                Mode = "Auto",
                NumberingStyle = "ZeroPadded"
            },
            LegacyUseLeadingZerosInChapterSuggestions = false
        };

        var result = await fixture.Service.RunAsync(
            request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(9, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.Contains("title=01", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("title=001", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ChapterSettingsService.ModeAuto, "title=1 Book", true)]
    [InlineData(ChapterSettingsService.ModeCustom, "title=Book", true)]
    [InlineData(ChapterSettingsService.ModeNone, "[CHAPTER]", false)]
    [InlineData(ChapterSettingsService.ModeChapterMaster, "title=1 Book", true)]
    [InlineData(ChapterSettingsService.ModeOriginal, "[CHAPTER]", false)]
    public async Task RunAsync_ExportUsesExactlyTheSelectedChapterMode(
        string mode,
        string marker,
        bool expected)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var settings = new ChapterSettings
        {
            Mode = mode,
            NumberingStyle = "Plain",
            Slot1 = "BookTitle",
            Slot2 = "Empty",
            Slot3 = "Empty"
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request with { ChapterSettings = settings },
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        if (expected)
            Assert.Contains(marker, fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
        else
            Assert.DoesNotContain(marker, fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OriginalModeExportsOriginalEmbeddedTimeline()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Tracks[0].EmbeddedChapters =
        [
            new TrackEmbeddedChapterInfo
            {
                Title = "  Original Export  ",
                StartTicks = TimeSpan.FromSeconds(1).Ticks,
                EndTicks = TimeSpan.FromSeconds(3).Ticks
            }
        ];
        var settings = new ChapterSettings
        {
            Mode = ChapterSettingsService.ModeOriginal,
            NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString()
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request with { ChapterSettings = settings },
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Contains("START=1000", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
        Assert.Contains("title=  Original Export  ", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
        Assert.DoesNotContain("title=001", fixture.Audio.LastChapterMetadata, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RunAsync_WhenPauseBeforeMerge_StopsAfterPreparation()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: true);
        var manualReviewChanged = false;

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(
                autoMerge: () => false,
                notifyManualReviewStateChanged: () => manualReviewChanged = true),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.Equal("ConversionCompleted", manifest.State.LastSuccessfulStep);
        Assert.False(manifest.Resume.CanResume);
        Assert.True(manualReviewChanged);
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(0, fixture.Audio.MergeCalls);
        Assert.Equal(0, fixture.Tags.WriteCalls);
        Assert.Equal("1 Book", fixture.Tracks[0].ChapterTitle);
        Assert.Equal("2 Book", fixture.Tracks[1].ChapterTitle);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
    }

    [Fact]
    public async Task RunAsync_EmitsLocalizedStatusCallbacks()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            using var folder = new TemporaryFolder();
            var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: true);
            var statuses = new List<string>();
            var progress = new List<string>();

            var result = await fixture.Service.RunAsync(
                fixture.Request,
                fixture.CreateCallbacks(
                    autoMerge: () => false,
                    setStatusText: statuses.Add,
                    setProgressText: progress.Add),
                CancellationToken.None);

            Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
            Assert.Contains(statuses, value => value.Contains("Exportación", StringComparison.Ordinal));
            Assert.Contains(statuses, value => value.Contains("Conversión completada", StringComparison.Ordinal));
            Assert.Contains(progress, value => value.Contains("pistas preparadas", StringComparison.Ordinal));
            Assert.DoesNotContain(statuses, value => value.Contains("Konvertierung", StringComparison.Ordinal));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task RunAsync_WhenLiveAutoMergeIsEnabledAtBoundary_Merges()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: true);
        var autoMerge = true;

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => autoMerge),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(ProjectManifestStatuses.Completed, manifest.State.Status);
        Assert.Equal(1, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_WhenLiveAutoMergeIsDisabledAtBoundary_PausesForReview()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var autoMerge = false;

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => autoMerge),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_WhenAutoMergeChangesFromOffToOnDuringConversion_UsesLiveBoundaryValue()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: true);
        var autoMerge = false;
        fixture.Audio.PrepareTrackAsync = async (track, sourcePath, convertedPath, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            await File.WriteAllTextAsync(convertedPath, "prepared " + track.Index, token);
            autoMerge = true;
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => autoMerge),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(1, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_WhenAutoMergeChangesFromOnToOffDuringConversion_UsesLiveBoundaryValue()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var autoMerge = true;
        fixture.Audio.PrepareTrackAsync = async (track, sourcePath, convertedPath, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            await File.WriteAllTextAsync(convertedPath, "prepared " + track.Index, token);
            autoMerge = false;
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => autoMerge),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_ExplicitManualMerge_UsesPreparedTracksWithAutoMergeOff()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder, pauseBeforeMerge: true);
        var first = await fixture.Service.RunAsync(fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => false), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, first.Status);
        var result = await fixture.Service.RunAsync(fixture.Request with { ManualMergeRequested = true },
            fixture.CreateCallbacks(autoMerge: () => false), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_WhenPreparationIsCanceled_CleansPartFilesAndPersistsCanceledResumeState()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Audio.PrepareTrackAsync = (track, sourcePath, convertedPath, token) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.WriteAllText(convertedPath + ".part", "incomplete");
            throw new OperationCanceledException(token);
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(),
            new CancellationToken(canceled: true));

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Canceled, result.Status);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.NotNull(manifest.State.CancelRequestedUtc);
        Assert.True(manifest.Resume.CanResume);
        Assert.Equal(0, fixture.Audio.MergeCalls);
        Assert.False(Directory.EnumerateFiles(fixture.Plan.ProjectWorkFolder, "*.part", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task RunAsync_WhenMergeFails_ReturnsFailedAndDoesNotCompleteManifest()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Audio.MergeAsync = (_, _, finalPartPath, _) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPartPath)!);
            File.WriteAllText(finalPartPath, "partial final");
            throw new InvalidOperationException("merge failed");
        };

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Failed, result.Status);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.Contains("merge failed", manifest.State.LastErrorSummary, StringComparison.Ordinal);
        Assert.True(manifest.Resume.CanResume);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
        Assert.Equal(0, fixture.Tags.WriteCalls);
    }

    [Fact]
    public async Task RunAsync_WhenTaggingFails_ReturnsFailedAndRemovesFinalPart()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Tags.WriteTags = (_, _) => throw new InvalidOperationException("tag failed");

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Failed, result.Status);
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.Contains("tag failed", manifest.State.LastErrorSummary, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
    }

    [Fact]
    public async Task RunAsync_FreezesFinalizationSnapshotAfterMergeAndUsesFinalDuration()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var duration = TimeSpan.FromHours(27) + TimeSpan.FromMinutes(3);
        var validator = new FakeFinalizedOutputValidationService { Duration = duration };
        fixture.Validator = validator;
        fixture.RecreateService();
        var chapterSettings = new ChapterSettings
        {
            Mode = "Custom",
            NumberingStyle = "Plain",
            Slot1 = "Number",
            Separator1 = "Space",
            Slot2 = "CleanFileName",
            Separator2 = "Space",
            Slot3 = "Empty"
        };
        var request = fixture.Request with
        {
            ChapterSettings = chapterSettings,
            CreateFinalTags = (finalDuration, finalChapterCount) =>
            {
                chapterSettings.Slot2 = "BookTitle";
                return new FinalAudioTagData
                {
                    Title = "Frozen",
                    Authors = ["Author"],
                    Duration = finalDuration,
                    ChapterCount = finalChapterCount,
                    CompatibilityDescriptionEnabled = true,
                    OutputPresentationCulture = "en-US"
                };
            }
        };

        var result = await fixture.Service.RunAsync(
            request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(duration, fixture.Tags.LastTagData?.Duration);
        Assert.Equal(duration, validator.ValidatedSnapshot?.OutputTags.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), validator.ValidatedSnapshot?.ExpectedDuration);
        Assert.Equal(2, validator.ValidatedSnapshot?.ExpectedChapters?.Count);
        Assert.Equal(2, fixture.Tags.LastTagData?.ChapterCount);
        Assert.Equal(2, validator.ValidatedSnapshot?.OutputTags.ChapterCount);
        Assert.Equal("CleanFileName", validator.ValidatedSnapshot?.ChapterSettingsSnapshot?.Slot2);
    }

    [Fact]
    public async Task RunAsync_WhenFinalValidationFails_ReturnsFailedAndDoesNotCommitOutput()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.Validator = new FakeFinalizedOutputValidationService
        {
            ValidateCore = () => throw new InvalidOperationException("validation failed")
        };
        fixture.RecreateService();

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Failed, result.Status);
        Assert.Contains("validation failed", result.Error?.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(fixture.Plan.FinalOutputPath));
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
    }

    [Fact]
    public async Task RunAsync_WhenOutputConflictIsCanceled_KeepsExistingOutputAndDiscardsFinalPart()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        Directory.CreateDirectory(fixture.Plan.FinalOutputFolder);
        File.WriteAllText(fixture.Plan.FinalOutputPath, "existing");

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(resolveFinalOutputConflict: (_, _) => FinalOutputConflictAction.Cancel),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.FinalOutputDiscarded, result.Status);
        Assert.Equal("existing", File.ReadAllText(fixture.Plan.FinalOutputPath));
        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.False(File.Exists(fixture.Plan.FinalPartPath));
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(fixture.ProjectFolder)));
        Assert.Null(new ProjectExtensionRollbackService().GetActive(fixture.ProjectFolder));
        Assert.All(manifest.Tracks, track => Assert.Equal(File.ReadAllBytes(track.SourcePath), File.ReadAllBytes(track.ConvertedPath)));
    }

    [Theory]
    [InlineData(FinalOutputConflictAction.Rename)]
    [InlineData(FinalOutputConflictAction.Overwrite)]
    public async Task RunAsync_WhenOutputConflictIsResolved_CompletesWithExpectedOutput(
        FinalOutputConflictAction action)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        Directory.CreateDirectory(fixture.Plan.FinalOutputFolder);
        File.WriteAllText(fixture.Plan.FinalOutputPath, "existing");
        var expectedPath = action == FinalOutputConflictAction.Rename
            ? OutputFileConflictService.CreateRenamedOutputPath(fixture.Plan.FinalOutputPath, fixture.Preset)
            : fixture.Plan.FinalOutputPath;

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(resolveFinalOutputConflict: (_, _) => action),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(expectedPath, result.OutputPath);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal("merged final", File.ReadAllText(expectedPath));
        Assert.Equal(ProjectManifestStatuses.Completed, manifest.State.Status);
        Assert.Equal(Path.GetFileName(expectedPath), manifest.Export.OutputFileName);
    }

    [Fact]
    public async Task RunAsync_WhenOutputMoveFailsAndDesktopFallbackIsAccepted_CompletesWithFallbackPath()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var fallbackPath = Path.Combine(folder.Path, "desktop", fixture.Plan.FinalOutputFileName);
        fixture.Output = new FakeFinalOutputStorageService
        {
            CreateDesktopOutputPathResult = fallbackPath,
            MoveToOutputCore = (source, destination, overwrite, call) =>
            {
                if (call == 1)
                    throw new IOException("destination unavailable");

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination, overwrite);
            }
        };
        fixture.RecreateService();

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(resolveFinalOutputFailure: (_, _, _) => FinalOutputFailureAction.SaveToDesktop),
            CancellationToken.None);

        var manifest = fixture.LoadManifest();
        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(fallbackPath, result.OutputPath);
        Assert.True(File.Exists(fallbackPath));
        Assert.Equal(ProjectManifestStatuses.Completed, manifest.State.Status);
        Assert.Equal(Path.GetDirectoryName(fallbackPath), manifest.Export.OutputFolder);
    }

    [Fact]
    public async Task RunAsync_ReusesPreparedTracksWithoutCallingPreparationAgain()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.SeedReusableManifest();
        fixture.Audio.PrepareTrackAsync = (_, _, _, _) => throw new InvalidOperationException("prepare should not run");

        var result = await fixture.Service.RunAsync(
            fixture.Request,
            fixture.CreateCallbacks(),
            CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.Completed, result.Status);
        Assert.Equal(0, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.Equal(ConvertedTrackPreparationPlanStatus.FullyReusable, result.ConversionResumeState?.Status);
        Assert.True(File.Exists(fixture.Plan.FinalOutputPath));
    }

    [Theory]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.FolderProject)]
    public async Task RunAsync_PreviouslyCompletedCachedExport_StillReviewsWhenAutoMergeIsOff(string projectType)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var request = fixture.Request with { Plan = fixture.Plan with { ProjectType = projectType } };
        fixture.ConfigureProjectType(projectType);
        var first = await fixture.Service.RunAsync(
            request, fixture.CreateCallbacks(autoMerge: () => true), CancellationToken.None);
        Assert.Null(first.Error);
        Assert.Equal(ExportWorkflowResultStatus.Completed, first.Status);

        var priorManifest = fixture.LoadManifest();
        var manifestService = new WorkManifestService();
        // Completed projects written before this patch may lack a review marker.
        priorManifest.State.ManualMergeReviewCompletedPresets.Clear();
        manifestService.Save(fixture.Plan.ManifestPath, priorManifest);

        var states = new List<ProjectPipelineState>();
        var reviewNotifications = 0;
        var second = await fixture.Service.RunAsync(
            request, fixture.CreateCallbacks(autoMerge: () => false,
                setPipelineState: states.Add, notifyManualReviewStateChanged: () => reviewNotifications++,
                resolveFinalOutputConflict: (_, _) => FinalOutputConflictAction.Overwrite), CancellationToken.None);

        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, second.Status);
        Assert.Equal(2, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.DoesNotContain(ProjectPipelineState.Converting, states);
        Assert.Contains(ProjectPipelineState.ReviewBeforeMerge, states);
        Assert.DoesNotContain(ProjectPipelineState.Merging, states);
        Assert.Equal(1, reviewNotifications);
    }

    [Fact]
    public async Task RunAsync_CompletedExportWithMissingPreparedTrack_ReconvertsAndReviewsAgain()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        await fixture.Service.RunAsync(fixture.Request, fixture.CreateCallbacks(), CancellationToken.None);
        File.Delete(fixture.LoadManifest().Tracks[0].ConvertedPath);
        var states = new List<ProjectPipelineState>();
        var result = await fixture.Service.RunAsync(fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => false, setPipelineState: states.Add), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(3, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(1, fixture.Audio.MergeCalls);
        Assert.Contains(ProjectPipelineState.Converting, states);
        Assert.Contains(ProjectPipelineState.ReviewBeforeMerge, states);
    }

    public static IEnumerable<object[]> HistoricalStates()
    {
        foreach (var projectType in new[] { ProjectManifestTypes.FolderProject,
                     ProjectManifestTypes.Mp3DiscProject, ProjectManifestTypes.AudioCdProject })
        foreach (var status in new[] { ProjectManifestStatuses.Preparing,
                     ProjectPipelineStateNames.AcquiringSources, ProjectManifestStatuses.Converting,
                     ProjectManifestStatuses.ReviewBeforeMerge, ProjectManifestStatuses.Merging,
                     ProjectManifestStatuses.Completed, "Canceled", "Failed" })
        foreach (var autoMerge in new[] { false, true })
            yield return [projectType, status, autoMerge];
    }

    [Theory]
    [MemberData(nameof(HistoricalStates))]
    public async Task RunAsync_AllProjectHistories_UseOnlyLiveSwitchAtReview(
        string projectType, string status, bool autoMerge)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.ConfigureProjectType(projectType);
        fixture.SeedReusableManifest();
        var manifest = fixture.LoadManifest();
        manifest.State.Status = status;
        manifest.State.ManualMergeReviewCompletedPresets = [fixture.Preset.DisplayName];
        new WorkManifestService().Save(fixture.Plan.ManifestPath, manifest);
        fixture.RecreateService(); // Represents reopening a project; no transient approval survives.
        var states = new List<ProjectPipelineState>();
        var reads = 0;
        var result = await fixture.Service.RunAsync(
            fixture.Request with { Plan = fixture.Plan with { ProjectType = projectType } },
            fixture.CreateCallbacks(setPipelineState: states.Add, autoMerge: () =>
            {
                reads++;
                Assert.Equal(ProjectPipelineState.ReviewBeforeMerge, states.Last());
                Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, fixture.LoadManifest().State.Status);
                return autoMerge;
            }), CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(autoMerge ? ExportWorkflowResultStatus.Completed : ExportWorkflowResultStatus.PausedBeforeMerge,
            result.Status);
        Assert.Equal(1, reads);
        Assert.Equal(0, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(autoMerge ? 1 : 0, fixture.Audio.MergeCalls);
        Assert.Equal(autoMerge, File.Exists(fixture.Plan.FinalOutputPath));
        Assert.Equal(autoMerge, states.Contains(ProjectPipelineState.Merging));
    }

    [Theory]
    [InlineData(ProjectManifestTypes.AudioCdProject, false)]
    [InlineData(ProjectManifestTypes.AudioCdProject, true)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, false)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, true)]
    [InlineData(ProjectManifestTypes.FolderProject, false)]
    [InlineData(ProjectManifestTypes.FolderProject, true)]
    public async Task RunAsync_AdditionalSourceAfterReview_DecidesFromLiveSwitchAgain(string projectType, bool autoMerge)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.ConfigureProjectType(projectType);
        var request = fixture.Request with { Plan = fixture.Plan with { ProjectType = projectType } };
        var first = await fixture.Service.RunAsync(request,
            fixture.CreateCallbacks(autoMerge: () => false), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, first.Status);
        var added = ExportWorkflowFixture.CreateTrack(fixture.SourceFolder, 3, "Added.m4a", TimeSpan.FromSeconds(2));
        added.SourceId = Guid.NewGuid();
        added.DiscNumber = 1;
        added.TrackNumber = 3;
        File.WriteAllText(added.FilePath, "added source");
        fixture.Tracks.Add(added);
        fixture.ConfigureProjectType(projectType);
        var extendedTracks = fixture.Tracks.ToList();
        request = request with { Plan = request.Plan with
            { TrackSnapshot = extendedTracks, TotalTicks = extendedTracks.Sum(track => track.DurationTicks.GetValueOrDefault()) } };
        var result = await fixture.Service.RunAsync(request,
            fixture.CreateCallbacks(autoMerge: () => autoMerge), CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Equal(autoMerge ? ExportWorkflowResultStatus.Completed : ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(3, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(autoMerge ? 1 : 0, fixture.Audio.MergeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_SwitchChangedOnEnteringReview_ReadsNewValueOnce(bool autoMergeAtReview)
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var autoMerge = !autoMergeAtReview;
        var reads = 0;
        var result = await fixture.Service.RunAsync(fixture.Request,
            fixture.CreateCallbacks(setPipelineState: state =>
            {
                if (state == ProjectPipelineState.ReviewBeforeMerge) autoMerge = autoMergeAtReview;
            }, autoMerge: () => { reads++; return autoMerge; }), CancellationToken.None);
        Assert.Equal(autoMergeAtReview ? ExportWorkflowResultStatus.Completed : ExportWorkflowResultStatus.PausedBeforeMerge,
            result.Status);
        Assert.Equal(1, reads);
        Assert.Equal(autoMergeAtReview ? 1 : 0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_ManualCommandWithMissingPreparedTrack_RequiresReviewAgain()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        fixture.SeedReusableManifest();
        File.Delete(fixture.LoadManifest().Tracks[0].ConvertedPath);
        var result = await fixture.Service.RunAsync(fixture.Request with { ManualMergeRequested = true },
            fixture.CreateCallbacks(autoMerge: () => false), CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.PausedBeforeMerge, result.Status);
        Assert.Equal(1, fixture.Audio.PrepareTrackCalls);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    [Fact]
    public async Task RunAsync_UnreadableLiveSwitch_FailsWithoutMerging()
    {
        using var folder = new TemporaryFolder();
        var fixture = ExportWorkflowFixture.Create(folder);
        var result = await fixture.Service.RunAsync(fixture.Request,
            fixture.CreateCallbacks(autoMerge: () => throw new InvalidOperationException("Unavailable switch")),
            CancellationToken.None);
        Assert.Equal(ExportWorkflowResultStatus.Failed, result.Status);
        Assert.Equal(0, fixture.Audio.MergeCalls);
    }

    private static ExportPerformanceContext CreatePhaseRecorder(List<string> phaseNames) =>
        new(
            phaseName =>
            {
                phaseNames.Add(phaseName);
                return default;
            },
            (_, _) => null);

    private sealed class ExportWorkflowFixture
    {
        private readonly WorkManifestService _manifestService = new();
        private readonly bool _autoMerge;
        private readonly ProjectSnapshotService _snapshotService;
        private readonly ConvertedTrackPreparationPlanService _planService;
        private readonly ConvertedTrackPreparationWorkflowService _preparationWorkflowService;

        private ExportWorkflowFixture(TemporaryFolder folder, bool pauseBeforeMerge)
        {
            _autoMerge = !pauseBeforeMerge;
            Root = folder.Path;
            SourceFolder = Path.Combine(Root, "source");
            ProjectFolder = Path.Combine(Root, "project");
            ProjectFolderLayout.EnsureProjectFolders(ProjectFolder);
            Directory.CreateDirectory(SourceFolder);

            Preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
            Tracks =
            [
                CreateTrack(SourceFolder, 1, "Track 01.m4a", TimeSpan.FromSeconds(4)),
                CreateTrack(SourceFolder, 2, "Track 02.m4a", TimeSpan.FromSeconds(6))
            ];
            foreach (var track in Tracks)
                File.WriteAllText(track.FilePath, "source " + track.Index);

            var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(ProjectFolder, Preset.GetFolderName());
            var mergeFolder = ProjectFolderLayout.GetMergeFolder(ProjectFolder);
            var outputFolder = Path.Combine(Root, "output");
            var outputFileName = "Book.m4b";
            var finalOutputPath = Path.Combine(outputFolder, outputFileName);
            // Exercise the production path contract rather than an independently constructed test plan.
            Plan = new ExportPlanService().Create(new ExportPlanRequest(Tracks, SourceFolder, Root,
                finalOutputPath, Preset.DisplayName, 2, ProjectWorkFolderOverride: ProjectFolder));
            Snapshot = new ProjectSnapshotUiState(
                Preset.DisplayName,
                "2",
                ".m4b",
                outputFolder,
                "{Titel}",
                "Book",
                "Author",
                "Album",
                "Narrator",
                "Audiobook",
                "",
                "",
                outputFileName);
            Request = new ExportWorkflowRequest(
                Plan,
                SourceFolder,
                "ffmpeg",
                Snapshot,
                new FinalAudioTagData { Title = "Book", Album = "Album", Author = "Author" },
                ManualMergeRequested: false);

            Audio = new FakeAudioExportProcessingService();
            Tags = new FakeFinalTagService();
            Output = new FinalOutputStorageService();
            _snapshotService = new ProjectSnapshotService(
                new Mp3DiscProjectService(),
                new AudioDiscProjectService(),
                _manifestService);
            _planService = new ConvertedTrackPreparationPlanService(
                new ConvertedTrackReconciliationService(_manifestService));
            _preparationWorkflowService = new ConvertedTrackPreparationWorkflowService(
                _manifestService,
                Audio);
            RecreateService();
        }

        public string Root { get; }
        public string SourceFolder { get; }
        public string ProjectFolder { get; }
        public ExportPreset Preset { get; }
        public List<TrackInfo> Tracks { get; }
        public ExportPlan Plan { get; }
        public ProjectSnapshotUiState Snapshot { get; }
        public ExportWorkflowRequest Request { get; }
        public FakeAudioExportProcessingService Audio { get; }
        public FakeFinalTagService Tags { get; }
        public IFinalOutputStorageService Output { get; set; }
        public IFinalizedOutputValidationService? Validator { get; set; }
        public ExportWorkflowService Service { get; private set; } = null!;

        public static ExportWorkflowFixture Create(TemporaryFolder folder, bool pauseBeforeMerge = false) =>
            new(folder, pauseBeforeMerge);

        public void RecreateService()
        {
            Service = new ExportWorkflowService(
                _manifestService,
                _snapshotService,
                Audio,
                new ConvertedFileCleanupService(),
                _planService,
                _preparationWorkflowService,
                new ExportChapterService(),
                Tags,
                Output,
                Validator);
        }

        public ExportWorkflowCallbacks CreateCallbacks(
            Action<ProjectPipelineState>? setPipelineState = null,
            Func<string, string, FinalOutputConflictAction>? resolveFinalOutputConflict = null,
            Func<string, string, Exception, FinalOutputFailureAction>? resolveFinalOutputFailure = null,
            Action? notifyManualReviewStateChanged = null,
            Action<string>? setStatusText = null,
            Action<string>? setProgressText = null,
            Func<bool>? autoMerge = null) =>
            new()
            {
                SetPipelineState = setPipelineState,
                MergeAutomaticallyAfterConversion = autoMerge ?? (() => _autoMerge),
                ResolveFinalOutputConflict = resolveFinalOutputConflict,
                ResolveFinalOutputFailure = resolveFinalOutputFailure,
                NotifyManualReviewStateChanged = notifyManualReviewStateChanged,
                SetStatusText = setStatusText,
                SetProgressText = setProgressText
            };

        public ExportWorkManifest LoadManifest() =>
            _manifestService.LoadOrCreate(
                Plan.ManifestPath,
                Plan.ProjectType,
                Plan.ProjectWorkFolder,
                SourceFolder,
                Preset.DisplayName);

        public void ConfigureProjectType(string projectType)
        {
            foreach (var track in Tracks)
            {
                if (track.SourceId == Guid.Empty) track.SourceId = Guid.NewGuid();
                track.DiscNumber = 1;
                track.TrackNumber = track.Index;
            }
            if (projectType == ProjectManifestTypes.AudioCdProject)
                File.WriteAllText(ProjectFolderLayout.GetAudioDiscManifestPath(ProjectFolder),
                    System.Text.Json.JsonSerializer.Serialize(new AudioDiscProjectManifest
                    {
                        ProjectFolder = ProjectFolder, TotalDiscs = 1,
                        Discs = [new() { DiscNumber = 1, TrackCount = Tracks.Count, DiscIdentity = "test-audio-disc",
                            Tracks = Tracks.Select(track => new AudioDiscProjectManifestTrack
                            { SourceId = track.SourceId, DiscNumber = 1, TrackNumber = track.Index,
                                FileName = track.FileName, RelativePath = track.FilePath }).ToList() }]
                    }));
            if (projectType == ProjectManifestTypes.Mp3DiscProject)
                File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(ProjectFolder),
                    System.Text.Json.JsonSerializer.Serialize(new Mp3DiscProjectManifest
                    {
                        ProjectFolder = ProjectFolder, TotalDiscs = 1,
                        ImportedDiscs = [new() { DiscNumber = 1, FileCount = Tracks.Count,
                            SourceFiles = Tracks.Select(track => new Mp3DiscProjectManifestSourceFile
                            { SourceId = track.SourceId, SourceFileName = track.FileName,
                                RelativePath = track.FileName }).ToList() }]
                    }));
        }

        public void SeedReusableManifest()
        {
            var manifest = _manifestService.LoadOrCreate(
                Plan.ManifestPath,
                Plan.ProjectType,
                Plan.ProjectWorkFolder,
                SourceFolder,
                Preset.DisplayName);

            foreach (var track in Tracks)
            {
                var sourcePath = TrackPathService.GetTrackPath(SourceFolder, track);
                var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(Plan.ConvertedFolder, sourcePath, track);
                Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
                File.WriteAllText(convertedPath, "ready " + track.Index);
                _manifestService.UpdateTrack(
                    manifest,
                    track.Index - 1,
                    track,
                    sourcePath,
                    convertedPath,
                    Preset);
            }

            _manifestService.Save(Plan.ManifestPath, manifest);
        }

        public static TrackInfo CreateTrack(string sourceFolder, int index, string fileName, TimeSpan duration) =>
            new()
            {
                Index = index,
                FilePath = Path.Combine(sourceFolder, fileName),
                FileName = fileName,
                Extension = ".m4a",
                ProcessingAction = "Übernehmen",
                DurationTicks = duration.Ticks,
                ChapterTitle = $"Chapter {index}",
                Codec = "AAC"
            };
    }

    private sealed class FakeAudioExportProcessingService : IAudioExportProcessingService
    {
        private readonly Dictionary<string, long> _durations = new();
        public Func<string, ConvertedAacTiming?, ConvertedAacTiming>? AnalyzeTiming { get; set; }
        public Func<string, CancellationToken, Task<ConvertedAacTiming>>? AnalyzeTimingAsync { get; set; }
        private int _timingCalls;
        public int TimingCalls => _timingCalls;
        public string LastConcat { get; private set; } = "";
        public async Task<ConvertedAacTiming> AnalyzeConvertedTimingAsync(string path, string ffprobePath,
            ConvertedAacTiming? cached, CancellationToken token)
        {
            Interlocked.Increment(ref _timingCalls);
            if (AnalyzeTimingAsync is not null) return await AnalyzeTimingAsync(path, token);
            var info = new FileInfo(path);
            var timing = AnalyzeTiming?.Invoke(path, cached) ??
                new ConvertedAacTiming(1, 1, 1,
                    _durations.GetValueOrDefault(path, TimeSpan.TicksPerSecond), 1, TimeSpan.TicksPerSecond);
            return timing with { Length = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks };
        }
        public int PrepareTrackCalls { get; private set; }
        public int MergeCalls { get; private set; }
        public string LastChapterMetadata { get; private set; } = "";
        public Func<TrackInfo, string, string, CancellationToken, Task>? PrepareTrackAsync { get; set; }
        public Func<string, string, string, CancellationToken, Task>? MergeAsync { get; set; }

        public async Task PrepareTrackForExportAsync(
            TrackInfo track,
            string sourcePath,
            string convertedPath,
            ExportPreset preset,
            string ffmpegPath,
            CancellationToken token,
            Action<TimeSpan> onProgress,
            PerformanceExternalProcessContext? performanceContext = null,
        string? ffprobePath = null)
        {
            PrepareTrackCalls++;
            _durations[convertedPath] = track.DurationTicks ?? TimeSpan.TicksPerSecond;
            if (PrepareTrackAsync is not null)
            {
                await PrepareTrackAsync(track, sourcePath, convertedPath, token);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.Copy(sourcePath, convertedPath, overwrite: true);
            onProgress(TimeSpan.FromTicks(track.DurationTicks ?? TimeSpan.TicksPerSecond));
        }

        public async Task MergeConvertedTracksAsync(
            string concatListPath,
            string chapterMetadataPath,
            string finalPartPath,
            long totalTicks,
            string ffmpegPath,
            CancellationToken token,
            Action<TimeSpan> onProgress,
            PerformanceExternalProcessContext? performanceContext = null)
        {
            MergeCalls++;
            LastConcat = await File.ReadAllTextAsync(concatListPath, token);
            LastChapterMetadata = File.Exists(chapterMetadataPath)
                ? await File.ReadAllTextAsync(chapterMetadataPath, token)
                : "";
            if (MergeAsync is not null)
            {
                await MergeAsync(concatListPath, chapterMetadataPath, finalPartPath, token);
                return;
            }

            Assert.True(File.Exists(concatListPath));
            Assert.True(File.Exists(chapterMetadataPath));
            Directory.CreateDirectory(Path.GetDirectoryName(finalPartPath)!);
            await File.WriteAllTextAsync(finalPartPath, "merged final", token);
            onProgress(TimeSpan.FromTicks(totalTicks));
        }
    }

    private sealed class FakeFinalTagService : IFinalTagService
    {
        public int WriteCalls { get; private set; }
        public FinalAudioTagData? LastTagData { get; private set; }
        public Action<string, FinalAudioTagData>? WriteTags { get; set; }

        public void WriteFinalTags(string audioPath, FinalAudioTagData tagData)
        {
            WriteCalls++;
            LastTagData = tagData;
            WriteTags?.Invoke(audioPath, tagData);
        }
    }

    private sealed class FakeFinalizedOutputValidationService : IFinalizedOutputValidationService
    {
        public TimeSpan? Duration { get; init; }
        public FinalizationSnapshot? ValidatedSnapshot { get; private set; }
        public Action? ValidateCore { get; init; }

        public Task<FinalizedOutputInspection> InspectAsync(
            string finalPartPath,
            string ffprobePath,
            PerformanceExternalProcessContext? performanceContext,
            CancellationToken token) =>
            Task.FromResult(new FinalizedOutputInspection(
                Duration,
                TagReadFailed: false,
                new OriginalTagMetadata(),
                new AudioProbeInfo
                {
                    FilePath = finalPartPath,
                    Success = true,
                    CodecType = "audio",
                    Channels = 2,
                    SampleRateHz = 44100,
                    Duration = Duration ?? TimeSpan.FromSeconds(1)
                }));

        public void Validate(FinalizationSnapshot snapshot, FinalizedOutputInspection inspection)
        {
            ValidatedSnapshot = snapshot;
            ValidateCore?.Invoke();
        }
    }

    private sealed class FakeFinalOutputStorageService : IFinalOutputStorageService
    {
        private readonly FinalOutputStorageService _inner = new();
        private int _moveCalls;

        public string CreateDesktopOutputPathResult { get; init; } = "";
        public Action<string, string, bool, int>? MoveToOutputCore { get; init; }

        public void MoveToOutput(string sourcePath, string destinationPath, bool overwrite)
        {
            _moveCalls++;
            if (MoveToOutputCore is not null)
            {
                MoveToOutputCore(sourcePath, destinationPath, overwrite, _moveCalls);
                return;
            }

            _inner.MoveToOutput(sourcePath, destinationPath, overwrite);
        }

        public string CreateDesktopOutputPath(string requestedOutputPath) =>
            string.IsNullOrWhiteSpace(CreateDesktopOutputPathResult)
                ? _inner.CreateDesktopOutputPath(requestedOutputPath)
                : CreateDesktopOutputPathResult;

        public string CreateAvailableOutputPath(string preferredPath) =>
            _inner.CreateAvailableOutputPath(preferredPath);
    }
}
