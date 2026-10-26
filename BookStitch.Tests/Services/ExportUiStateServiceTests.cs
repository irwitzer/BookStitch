using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportUiStateServiceTests
{
    [Theory]
    [InlineData("folder", "extension")]
    [InlineData("mp3", "extension")]
    [InlineData("audio", "extension")]
    [InlineData("folder", "reconversion")]
    [InlineData("mp3", "reconversion")]
    [InlineData("audio", "reconversion")]
    [InlineData("folder", "merge")]
    [InlineData("mp3", "merge")]
    [InlineData("audio", "merge")]
    public void Existing_project_attempts_offer_pause_then_continue_and_discard_for_every_source_type(string source, string attempt)
    {
        var phase = attempt == "merge" ? ProjectPipelineState.Merging :
            attempt == "extension" ? ProjectPipelineState.AcquiringSources : ProjectPipelineState.Converting;
        var input = Input(phase, isLocal: source == "folder", isMp3Disc: source == "mp3", isAudioDisc: source == "audio",
            isProjectExtensionRun: attempt == "extension", isPresetReconversionRun: attempt == "reconversion");
        var running = _service.Create(input with { IsBusy = true, IsExporting = true });
        Assert.Equal("Pause", running.ExportButtonText);
        Assert.Equal(Visibility.Collapsed, running.SecondaryButtonVisibility);
        var paused = _service.Create(input with { IsRunPaused = true });
        Assert.Equal("Weiter", paused.ExportButtonText);
        Assert.Equal("Verwerfen", paused.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, paused.SecondaryButtonVisibility);
        Assert.True(paused.CanCancelExport);
    }

    [Fact]
    public void Merge_actions_follow_ready_running_pausing_paused_and_completed_states()
    {
        var ready = _service.Create(Input(ProjectPipelineState.ReviewBeforeMerge));
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Merge"), ready.ExportButtonText);
        Assert.Equal(Visibility.Collapsed, ready.SecondaryButtonVisibility);
        var input = Input(ProjectPipelineState.Merging, isBusy: true) with { IsExporting = true };
        var running = _service.Create(input);
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Pause"), running.ExportButtonText);
        Assert.True(running.CanStartExport);
        Assert.Equal(Visibility.Collapsed, running.SecondaryButtonVisibility);
        var pausing = _service.Create(input with { PauseRequested = true });
        Assert.False(pausing.CanStartExport);
        Assert.Equal(LocalizationText.Get("Pipeline.Transition.Pausing"), pausing.ExportButtonText);
        Assert.Equal(Visibility.Collapsed, pausing.SecondaryButtonVisibility);
        var paused = _service.Create(input with { IsBusy = false, IsExporting = false, IsRunPaused = true });
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), paused.ExportButtonText);
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Discard"), paused.SecondaryButtonText);
        Assert.True(paused.CanCancelExport);
        Assert.Equal(Visibility.Visible, paused.SecondaryButtonVisibility);
        Assert.Equal(Visibility.Collapsed, _service.Create(Input(ProjectPipelineState.Completed)).SecondaryButtonVisibility);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Idle_disc_phase_offers_continuation_and_only_real_work_offers_pause(ProjectPipelineState phase)
    {
        var input = Input(phase, isMp3Disc: true, needsDiscImport: true);
        var idle = _service.Create(input);
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), idle.ExportButtonText);
        Assert.True(idle.CanStartExport);
        var active = _service.Create(input with { IsDiscImporting = true, IsBusy = true });
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Pause"), active.ExportButtonText);
        Assert.True(active.CanStartExport);
        var paused = _service.Create(input with { IsRunPaused = true });
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), paused.ExportButtonText);
        Assert.True(paused.CanStartExport);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validated_unstarted_resume_preview_opens_metadata_and_settings_only(bool audio)
    {
        var input = Input(ProjectPipelineState.AcquiringSources, isRunPaused: true,
            isAudioDisc: audio, isMp3Disc: !audio) with { HasPreparedResumeDiscPreview = true };
        var state = _service.Create(input);
        Assert.True(state.CanChangeBookMetadata);
        Assert.True(state.CanOpenSettings);
        Assert.True(state.CanEditTrackOrder);
        Assert.False(state.CanRefreshResumeProjects);
        Assert.False(state.CanChangeExportPreset);
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanAddProjectSources);
    }

    [Theory]
    [InlineData("not-prepared")]
    [InlineData("source-search")]
    [InlineData("source-analysis")]
    [InlineData("busy")]
    [InlineData("failed")]
    [InlineData("running")]
    [InlineData("importing")]
    [InlineData("exporting")]
    [InlineData("pause-request")]
    [InlineData("stop-transition")]
    [InlineData("extension")]
    [InlineData("reconversion")]
    [InlineData("started-preview")]
    [InlineData("no-tracks")]
    [InlineData("local")]
    [InlineData("converting")]
    [InlineData("merging")]
    public void Resume_preview_configuration_does_not_unlock_other_lifecycle_cases(string scenario)
    {
        var input = Input(ProjectPipelineState.AcquiringSources, isRunPaused: true, isMp3Disc: true)
            with { HasPreparedResumeDiscPreview = true };
        input = scenario switch
        {
            "not-prepared" => input with { HasPreparedResumeDiscPreview = false },
            "source-search" or "busy" => input with { IsBusy = true },
            "source-analysis" => input with { CanCancelTrackAnalysis = true },
            "failed" => input with { IsRunFailed = true },
            "running" => input with { IsRunPaused = false },
            "importing" => input with { IsDiscImporting = true },
            "exporting" => input with { IsExporting = true },
            "pause-request" => input with { PauseRequested = true },
            "stop-transition" => input with { UiTransition = new(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary) },
            "extension" => input with { IsProjectExtensionRun = true },
            "reconversion" => input with { IsPresetReconversionRun = true },
            "started-preview" => input with { ResumeDiscPreviewProcessingStarted = true },
            "no-tracks" => input with { TrackCount = 0 },
            "local" => input with { LoadedResumeProjectIsMp3Disc = false, LoadedResumeProjectIsLocal = true },
            "converting" => input with { CurrentState = ProjectPipelineState.Converting },
            "merging" => input with { CurrentState = ProjectPipelineState.Merging },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var baseline = _service.Create(input with { HasPreparedResumeDiscPreview = false });
        var state = _service.Create(input);
        Assert.False(state.CanChangeBookMetadata);
        Assert.False(state.CanOpenSettings);
        // Every unrelated permission follows the existing state contract.
        Assert.Equal(baseline with { CanEditTrackOrder = state.CanEditTrackOrder }, state);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData(ProjectPipelineState.Completed)]
    public void Preview_flags_do_not_change_normal_phase_permissions(ProjectPipelineState phase)
    {
        var normal = Input(phase, isMp3Disc: true, isProjectExtensionRun: true);
        Assert.Equal(_service.Create(normal), _service.Create(normal with { HasPreparedResumeDiscPreview = true }));
    }

    [Theory]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(false, true, false, true, false, true)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]
    [InlineData(false, false, false, true, false, false)]
    public void Only_validated_idle_disc_resume_preview_is_editable_while_acquisition_is_paused(
        bool mp3, bool audio, bool busy, bool preparedPreview, bool failed, bool expected)
    {
        var input = Input(ProjectPipelineState.AcquiringSources, isRunPaused: true,
            isMp3Disc: mp3, isAudioDisc: audio, isBusy: busy, isRunFailed: failed) with
            { HasPreparedResumeDiscPreview = preparedPreview };
        var state = _service.Create(input);
        Assert.Equal(expected, state.CanEditTrackOrder);
        Assert.False(state.CanChangeExportPreset);
        Assert.False(state.CanSelectFolder);
        Assert.False(_service.Create(input with { IsRunPaused = false }).CanEditTrackOrder);
        Assert.False(_service.Create(input with { CurrentState = ProjectPipelineState.Converting }).CanEditTrackOrder);
        Assert.False(_service.Create(input with { TrackCount = 0 }).CanEditTrackOrder);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing, false, false, false, false, false)]
    [InlineData(ProjectPipelineState.Converting, false, false, false, false, false)]
    [InlineData(ProjectPipelineState.AcquiringSources, false, true, false, false, true)]
    [InlineData(ProjectPipelineState.AcquiringSources, true, false, false, false, true)]
    [InlineData(ProjectPipelineState.Converting, true, false, false, false, true)]
    [InlineData(ProjectPipelineState.Converting, true, false, true, false, false)]
    [InlineData(ProjectPipelineState.Converting, true, false, false, true, false)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge, true, false, false, false, false)]
    [InlineData(ProjectPipelineState.Completed, false, false, false, false, false)]
    [InlineData(ProjectPipelineState.Merging, true, false, false, false, false)]
    public void Panel_collapse_requires_actual_preparation_work_not_loading_or_other_transitions(
        ProjectPipelineState phase, bool exporting, bool importing, bool paused, bool failed, bool expected)
    {
        Assert.Equal(expected, ExportUiStateService.IsPreparationProcessing(phase, exporting, importing, paused, failed));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Paused_initial_projects_have_no_destructive_secondary_action(bool local, bool mp3, bool audio)
    {
        var state = _service.Create(Input(ProjectPipelineState.AcquiringSources, isRunPaused: true,
            isLocal: local, isMp3Disc: mp3, isAudioDisc: audio));
        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.True(state.CanStartExport);
        Assert.False(state.CanCancelExport);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources, true, false)]
    [InlineData(ProjectPipelineState.Converting, false, true)]
    [InlineData(ProjectPipelineState.Converting, false, false)]
    public void Pending_pause_disables_actions_keeps_primary_visible_and_does_not_offer_continue(
        ProjectPipelineState phase, bool mp3, bool audio)
    {
        var input = Input(phase, isMp3Disc: mp3, isAudioDisc: audio) with { PauseRequested = true };
        var pending = _service.Create(input);
        Assert.Equal(LocalizationText.Get("Pipeline.Transition.Pausing"), pending.ExportButtonText);
        Assert.Equal(Visibility.Visible, pending.ExportButtonVisibility);
        Assert.False(pending.CanStartExport);
        Assert.False(pending.CanCancelExport);
        Assert.False(pending.CanEditTrackOrder);
        Assert.False(pending.CanChangeExportPreset);
        var paused = _service.Create(input with { PauseRequested = false, IsRunPaused = true });
        Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), paused.ExportButtonText);
        Assert.True(paused.CanStartExport);
        Assert.False(paused.CanCancelExport);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Converting)]
    [InlineData(ProjectPipelineState.Merging)]
    [InlineData(ProjectPipelineState.Preparing)]
    public void Stopping_keeps_secondary_visible_disabled_even_when_busy_or_phase_changes(ProjectPipelineState phase)
    {
        var input = Input(phase, isBusy: true) with
        {
            UiTransition = new(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary)
        };
        var stopping = _service.Create(input);
        Assert.Equal(LocalizationText.Get("Pipeline.Transition.Stopping"), stopping.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, stopping.SecondaryButtonVisibility);
        Assert.False(stopping.CanCancelExport);
        Assert.False(stopping.CanStartExport);
        Assert.False(stopping.CanOpenSettings);
        Assert.False(stopping.CanSelectFolder);
    }

    private readonly ExportUiStateService _service = new();

    private static ExportUiStateInput Input(
        ProjectPipelineState state,
        bool isBusy = false,
        bool isRunPaused = false,
        bool isRunFailed = false,
        bool isProjectExtensionRun = false,
        bool needsReconversion = false,
        bool hasPreparedTracks = false,
        bool needsDiscImport = false,
        bool isMp3Disc = false,
        bool isAudioDisc = false,
        bool isLocal = false,
        int trackCount = 10,
        bool hasOutputFolder = true,
        double progress = 0,
        bool hasSelectedProject = false,
        bool canCancelTrackAnalysis = false,
        bool isPresetReconversionRun = false) => new(
            CurrentState: state,
            IsBusy: isBusy,
            IsRunPaused: isRunPaused,
            IsRunFailed: isRunFailed,
            IsProjectExtensionRun: isProjectExtensionRun,
            ManualMergeReviewNeedsReconversion: needsReconversion,
            ManualMergeReviewHasPreparedTracks: hasPreparedTracks,
            FfmpegAvailable: true,
            LoadedResumeProjectNeedsDiscImport: needsDiscImport,
            LoadedResumeProjectIsMp3Disc: isMp3Disc,
            LoadedResumeProjectIsAudioDisc: isAudioDisc,
            LoadedResumeProjectIsLocal: isLocal,
            TrackCount: trackCount,
            HasOutputFolder: hasOutputFolder,
            ExportProgressPercent: progress,
            HasSelectedResumeProject: hasSelectedProject,
            CanCancelTrackAnalysis: canCancelTrackAnalysis,
            IsPresetReconversionRun: isPresetReconversionRun);

    [Theory]
    [InlineData(ProjectPipelineState.Preparing, false, false, true)]
    [InlineData(ProjectPipelineState.Preparing, true, false, false)]
    [InlineData(ProjectPipelineState.AcquiringSources, true, false, false)]
    [InlineData(ProjectPipelineState.AcquiringSources, false, true, true)]
    [InlineData(ProjectPipelineState.Converting, false, false, false)]
    [InlineData(ProjectPipelineState.Converting, false, true, true)]
    [InlineData(ProjectPipelineState.Converting, true, true, false)]
    [InlineData(ProjectPipelineState.Merging, false, false, false)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge, false, false, true)]
    [InlineData(ProjectPipelineState.Completed, false, false, true)]
    [InlineData(ProjectPipelineState.Completed, true, false, false)]
    public void Output_and_chapter_editing_follow_stable_or_paused_state(
        ProjectPipelineState phase, bool busy, bool paused, bool editable)
    {
        var state = _service.Create(Input(phase, isBusy: busy, isRunPaused: paused));
        Assert.Equal(editable, state.CanChangeExportOptions);
        Assert.Equal(editable, state.CanChooseOutputFolder);
    }

    [Fact]
    public void Create_WhenPreparingWithTracks_AllowsProjectStartAndEditing()
    {
        var state = _service.Create(Input(ProjectPipelineState.Preparing, trackCount: 3));

        Assert.Equal(ProjectPipelineState.Preparing, state.Phase);
        Assert.True(state.CanStartExport);
        Assert.True(state.CanEditTrackOrder);
        Assert.True(state.CanChangeBookMetadata);
        Assert.Equal("Projekt vorbereiten", state.ExportButtonText);
        Assert.Equal(Visibility.Visible, state.ExportButtonVisibility);
        Assert.Equal(Visibility.Collapsed, state.ExportProgressVisibility);
    }

    [Fact]
    public void Create_WhenPreparingWithoutTracks_HidesStartAndCancelActions()
    {
        var state = _service.Create(Input(ProjectPipelineState.Preparing, trackCount: 0));

        Assert.False(state.CanStartExport);
        Assert.False(state.CanCancelExport);
        Assert.Equal(Visibility.Collapsed, state.ExportButtonVisibility);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
    }

    [Fact]
    public void Create_WhenBusyDuringPreparing_LocksInteractionWithoutCreatingAnotherState()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Preparing,
            isBusy: true,
            hasSelectedProject: true));

        Assert.Equal(ProjectPipelineState.Preparing, state.Phase);
        Assert.False(state.CanStartExport);
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanOpenSettings);
        Assert.False(state.CanInspectSelectedResumeProject);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPausablePipelineIsActive_UsesPrimaryPauseAndHidesProjectCancel(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(phase, isBusy: true, progress: 35) with { IsDiscImporting = phase == ProjectPipelineState.AcquiringSources, IsExporting = phase == ProjectPipelineState.Converting });

        Assert.Equal(phase, state.Phase);
        Assert.False(state.CanChangeBookMetadata);
        Assert.False(state.CanEditTrackOrder);
        Assert.False(state.CanChangeExportPreset);
        Assert.True(state.CanStartExport);
        Assert.Equal("Pause", state.ExportButtonText);
        Assert.Equal("Abbrechen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanRefreshResumeProjects);
        Assert.Equal(Visibility.Visible, state.ExportProgressVisibility);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPausablePipelineIsWaitingForExternalInput_KeepsPrimaryPauseAndHidesProjectCancel(
        ProjectPipelineState phase)
    {
        var state = _service.Create(Input(phase, isBusy: false, progress: 35) with { IsDiscImporting = phase == ProjectPipelineState.AcquiringSources, IsExporting = phase == ProjectPipelineState.Converting });

        Assert.Equal(phase, state.Phase);
        Assert.True(state.CanStartExport);
        Assert.Equal("Pause", state.ExportButtonText);
        Assert.Equal("Abbrechen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanRefreshResumeProjects);
        Assert.Equal(Visibility.Visible, state.ExportProgressVisibility);
    }

    [Fact]
    public void Create_WhenMergeIsActive_OffersPauseAndHidesSecondaryAction()
    {
        var state = _service.Create(Input(ProjectPipelineState.Merging, isBusy: true, progress: 35) with { IsExporting = true });

        Assert.True(state.CanStartExport);
        Assert.Equal("Pause", state.ExportButtonText);
        Assert.Equal("Abbrechen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanRefreshResumeProjects);
        Assert.Equal(Visibility.Visible, state.ExportProgressVisibility);
    }

    [Fact]
    public void Create_WhenReviewing_AllowsProjectEditingAndPassiveProjectSwitch()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            progress: 100,
            hasSelectedProject: true));

        Assert.True(state.CanStartExport);
        Assert.True(state.CanEditTrackOrder);
        Assert.True(state.CanChangeBookMetadata);
        Assert.True(state.CanChangeExportPreset);
        Assert.True(state.CanChooseOutputFolder);
        Assert.True(state.CanOpenSettings);
        Assert.True(state.CanSelectFolder);
        Assert.True(state.CanRefreshResumeProjects);
        Assert.True(state.CanInspectSelectedResumeProject);
        Assert.Equal("Zusammenfügen", state.ExportButtonText);
        Assert.Equal("Projekt schließen", state.SecondaryButtonText);
        Assert.False(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenReviewPresetIsPartiallyPrepared_UsesContinueConversionActionAndStillAllowsAddingSources()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            needsReconversion: true,
            hasPreparedTracks: true,
            isMp3Disc: true,
            progress: 40));

        Assert.Equal("Konvertieren", state.ExportButtonText);
        Assert.True(state.CanAddProjectSources);
        Assert.Equal(Visibility.Visible, state.AddProjectSourcesVisibility);
    }

    [Fact]
    public void Create_WhenCompleted_AllowsProjectSwitchAndMetadataEditing()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Completed,
            isMp3Disc: true,
            progress: 100,
            hasSelectedProject: true));

        Assert.True(state.CanSelectFolder);
        Assert.True(state.CanOpenSettings);
        Assert.True(state.CanRefreshResumeProjects);
        Assert.True(state.CanInspectSelectedResumeProject);
        Assert.True(state.CanEditTrackOrder);
        Assert.True(state.CanChangeBookMetadata);
        Assert.True(state.CanAddProjectSources);
        Assert.Equal("Zusammenfügen", state.ExportButtonText);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing, false, true)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge, false, true)]
    [InlineData(ProjectPipelineState.Completed, false, true)]
    [InlineData(ProjectPipelineState.AcquiringSources, false, false)]
    [InlineData(ProjectPipelineState.Converting, false, false)]
    [InlineData(ProjectPipelineState.Merging, false, false)]
    [InlineData(ProjectPipelineState.Preparing, true, false)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge, true, false)]
    [InlineData(ProjectPipelineState.Completed, true, false)]
    public void Metadata_editing_requires_a_resting_project_and_no_busy_workflow(
        ProjectPipelineState phase, bool isBusy, bool expected)
    {
        Assert.Equal(expected, _service.Create(Input(phase, isBusy: isBusy)).CanChangeBookMetadata);
    }

    [Fact]
    public void Create_WhenCompletedPresetIsNotPrepared_UsesNewConversionAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Completed,
            needsReconversion: true,
            isMp3Disc: true,
            progress: 0));

        Assert.True(state.CanStartExport);
        Assert.Equal("Neu konvertieren", state.ExportButtonText);
        Assert.True(state.CanAddProjectSources);
    }

    [Fact]
    public void Create_WhenCompletedPresetIsPartiallyPrepared_UsesContinueConversionAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Completed,
            needsReconversion: true,
            hasPreparedTracks: true,
            isMp3Disc: true,
            progress: 15));

        Assert.Equal("Konvertieren", state.ExportButtonText);
        Assert.True(state.CanAddProjectSources);
    }

    [Fact]
    public void Create_WhenReviewingMp3Disc_AllowsAddingMoreDiscs()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            isMp3Disc: true));

        Assert.True(state.CanAddProjectSources);
        Assert.Equal(Visibility.Visible, state.AddProjectSourcesVisibility);
    }
    [Fact]
    public void Create_WhenReviewingAudioDisc_AllowsAddingMoreDiscs()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            isAudioDisc: true));

        Assert.True(state.CanAddProjectSources);
        Assert.Equal(Visibility.Visible, state.AddProjectSourcesVisibility);
    }

    [Fact]
    public void Create_WhenReviewingLocalProject_AllowsAddingSources()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            isLocal: true));

        Assert.True(state.CanAddProjectSources);
        Assert.Equal(Visibility.Visible, state.AddProjectSourcesVisibility);
        Assert.Equal("Quellen hinzufügen", state.AddProjectSourcesButtonText);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Create_WhenCurrentPresetNeedsReconversion_KeepsSourceExtensionAvailableForEveryProjectType(
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal)
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            needsReconversion: true,
            isMp3Disc: isMp3Disc,
            isAudioDisc: isAudioDisc,
            isLocal: isLocal));

        Assert.True(state.CanAddProjectSources);
        Assert.Equal(Visibility.Visible, state.AddProjectSourcesVisibility);
        Assert.Equal("Neu konvertieren", state.ExportButtonText);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPipelineIsPaused_EnablesContinueButKeepsMetadataLocked(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(
            phase,
            isRunPaused: true,
            isLocal: true,
            progress: 40));

        Assert.True(state.CanStartExport);
        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal(state.SecondaryButtonVisibility == Visibility.Visible ? "Verwerfen" : "Speichern", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
        Assert.False(state.CanChangeBookMetadata);
        Assert.True(state.CanChooseOutputFolder);
        Assert.True(state.CanChangeExportOptions);
        Assert.False(state.CanChangeExportPreset);
        Assert.False(state.CanEditTrackOrder);
        Assert.False(state.CanOpenSettings);
        Assert.False(state.CanAddProjectSources);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPipelineFailed_OffersNonDestructiveProjectClose(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(
            phase,
            isRunFailed: true,
            isLocal: true,
            progress: 40));

        Assert.False(state.CanStartExport);
        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal("Projekt schließen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, state.SecondaryButtonVisibility);
        Assert.True(state.CanCancelExport);
        Assert.False(state.CanOpenSettings);
        Assert.False(state.CanAddProjectSources);
    }

    [Fact]
    public void Create_WhenReviewProjectHasPreviousExportFailure_KeepsRetryAndCloseActions()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            isRunFailed: true,
            isLocal: true,
            progress: 100));

        Assert.True(state.CanStartExport);
        Assert.Equal("Zusammenfügen", state.ExportButtonText);
        Assert.Equal("Projekt schließen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
        Assert.True(state.CanChangeBookMetadata);
        Assert.True(state.CanChooseOutputFolder);
        Assert.True(state.CanChangeExportOptions);
        Assert.True(state.CanOpenSettings);
    }


    [Fact]
    public void Create_WhenPresetReconversionIsPaused_UsesDiscardAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Converting,
            isRunPaused: true,
            isLocal: true,
            isPresetReconversionRun: true));

        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal(state.SecondaryButtonVisibility == Visibility.Visible ? "Verwerfen" : "Speichern", state.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, state.SecondaryButtonVisibility);
        Assert.True(state.CanStartExport);
        Assert.True(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenProjectExtensionIsPaused_UsesDiscardAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.AcquiringSources,
            isRunPaused: true,
            isProjectExtensionRun: true,
            isLocal: true));

        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal(state.SecondaryButtonVisibility == Visibility.Visible ? "Verwerfen" : "Speichern", state.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, state.SecondaryButtonVisibility);
        Assert.True(state.CanStartExport);
        Assert.True(state.CanCancelExport);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPipelineIsRunning_UsesPrimaryPauseAction(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(phase, isBusy: true) with { IsDiscImporting = phase == ProjectPipelineState.AcquiringSources, IsExporting = phase == ProjectPipelineState.Converting });

        Assert.Equal("Pause", state.ExportButtonText);
        Assert.True(state.CanStartExport);
        Assert.Equal("Abbrechen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenAudioDiscPreparationIsBusy_HidesSecondaryButton()
    {
        var state = _service.Create(Input(ProjectPipelineState.Preparing, isBusy: true));

        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenTrackAnalysisCanBeCanceled_ShowsEnabledCancelAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Preparing,
            isBusy: true,
            trackCount: 3,
            canCancelTrackAnalysis: true));

        Assert.Equal("Abbrechen", state.SecondaryButtonText);
        Assert.Equal(Visibility.Visible, state.SecondaryButtonVisibility);
        Assert.Equal(Visibility.Collapsed, state.ExportButtonVisibility);
        Assert.True(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenPreparingProbeCannotBeCanceled_HidesCancelAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.Preparing,
            isBusy: true,
            trackCount: 3,
            canCancelTrackAnalysis: false));

        Assert.Equal(Visibility.Collapsed, state.SecondaryButtonVisibility);
        Assert.False(state.CanCancelExport);
    }

}
