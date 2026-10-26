using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportUiStateInputServiceTests
{
    [Fact]
    public void Pending_ui_transition_and_pause_request_are_forwarded_without_becoming_completed_pause()
    {
        var owner = new PipelineUiTransition(PipelineUiTransitionKind.Pausing, PipelineUiTransitionTarget.Primary);
        var source = CreateSource() with { PauseRequested = true, UiTransition = owner,
            ExecutionState = new(true, false, false, false) };
        var input = ExportUiStateInputService.Create(source);
        Assert.Same(owner, input.UiTransition);
        Assert.True(input.PauseRequested);
        Assert.False(input.IsRunPaused);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Resume_preview_execution_guards_are_projected_from_their_runtime_owners(bool importing, bool exporting, bool started)
    {
        var input = ExportUiStateInputService.Create(CreateSource() with
        { IsDiscImporting = importing, IsExporting = exporting, ResumeDiscPreviewProcessingStarted = started });
        Assert.Equal(importing, input.IsDiscImporting);
        Assert.Equal(exporting, input.IsExporting);
        Assert.Equal(started, input.ResumeDiscPreviewProcessingStarted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_preserves_prepared_resume_preview_without_inventing_it(bool hasPreview)
    {
        var source = CreateSource() with { HasPreparedResumeDiscPreview = hasPreview };
        Assert.Equal(hasPreview, ExportUiStateInputService.Create(source).HasPreparedResumeDiscPreview);
        Assert.False(ExportUiStateInputService.Create(CreateSource()).HasPreparedResumeDiscPreview);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(@"D:\Export", true)]
    public void Create_DerivesOutputFolderAvailability(
        string? outputFolder,
        bool expected)
    {
        var result = ExportUiStateInputService.Create(
            CreateSource(outputFolder: outputFolder));

        Assert.Equal(expected, result.HasOutputFolder);
    }

    [Fact]
    public void Create_PreservesPipelineExecutionState()
    {
        var source = CreateSource();

        var result = ExportUiStateInputService.Create(source);

        Assert.Equal(source.ExecutionState.IsBusy, result.IsBusy);
        Assert.Equal(source.ExecutionState.IsPaused, result.IsRunPaused);
        Assert.Equal(source.ExecutionState.IsFailed, result.IsRunFailed);
        Assert.Equal(
            source.ExecutionState.IsProjectExtensionRun,
            result.IsProjectExtensionRun);
        Assert.Equal(source.IsPresetReconversionRun, result.IsPresetReconversionRun);
    }

    [Fact]
    public void Create_PreservesPipelineProjectState()
    {
        var source = CreateSource();

        var result = ExportUiStateInputService.Create(source);

        Assert.Equal(
            source.ProjectState.NeedsDiscImport,
            result.LoadedResumeProjectNeedsDiscImport);
        Assert.Equal(
            source.ProjectState.IsMp3Disc,
            result.LoadedResumeProjectIsMp3Disc);
        Assert.Equal(
            source.ProjectState.IsAudioDisc,
            result.LoadedResumeProjectIsAudioDisc);
        Assert.Equal(
            source.ProjectState.IsLocal,
            result.LoadedResumeProjectIsLocal);
    }

    [Fact]
    public void Create_DerivesSelectedResumeProjectAvailability()
    {
        var withoutSelection = ExportUiStateInputService.Create(
            CreateSource(selectedResumeProject: null));
        var withSelection = ExportUiStateInputService.Create(
            CreateSource(selectedResumeProject: new object()));

        Assert.False(withoutSelection.HasSelectedResumeProject);
        Assert.True(withSelection.HasSelectedResumeProject);
    }

    [Fact]
    public void Create_PreservesRawPipelineStateValues()
    {
        var source = CreateSource(
            outputFolder: @"D:\Export",
            selectedResumeProject: new object());

        var result = ExportUiStateInputService.Create(source);

        Assert.Equal(source.CurrentState, result.CurrentState);
        Assert.Equal(source.ExecutionState.IsBusy, result.IsBusy);
        Assert.Equal(source.ExecutionState.IsPaused, result.IsRunPaused);
        Assert.Equal(source.ExecutionState.IsFailed, result.IsRunFailed);
        Assert.Equal(
            source.ExecutionState.IsProjectExtensionRun,
            result.IsProjectExtensionRun);
        Assert.Equal(
            source.ManualMergeReviewNeedsReconversion,
            result.ManualMergeReviewNeedsReconversion);
        Assert.Equal(
            source.ManualMergeReviewHasPreparedTracks,
            result.ManualMergeReviewHasPreparedTracks);
        Assert.Equal(source.FfmpegAvailable, result.FfmpegAvailable);
        Assert.Equal(
            source.ProjectState.NeedsDiscImport,
            result.LoadedResumeProjectNeedsDiscImport);
        Assert.Equal(
            source.ProjectState.IsMp3Disc,
            result.LoadedResumeProjectIsMp3Disc);
        Assert.Equal(
            source.ProjectState.IsAudioDisc,
            result.LoadedResumeProjectIsAudioDisc);
        Assert.Equal(
            source.ProjectState.IsLocal,
            result.LoadedResumeProjectIsLocal);
        Assert.Equal(source.TrackCount, result.TrackCount);
        Assert.Equal(source.ExportProgressPercent, result.ExportProgressPercent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pause_and_continuation_project_the_runtime_reconversion_owner_without_granting_generic_rollback(bool hasContext)
    {
        var transitions = new PipelinePauseTransitionService();
        var pause = transitions.CreateCompletedPause();
        var source = CreateSource() with { CurrentState = ProjectPipelineState.Converting,
            ExecutionState = new(pause.IsBusy, pause.IsPipelinePaused, pause.IsPipelineFailed, false),
            IsPresetReconversionRun = hasContext };
        var paused = ExportUiStateInputService.Create(source);
        Assert.True(paused.IsRunPaused); Assert.Equal(hasContext, paused.IsPresetReconversionRun);
        var continuation = transitions.CreateContinuationStart();
        var resumed = ExportUiStateInputService.Create(source with {
            ExecutionState = new(continuation.IsBusy, continuation.IsPipelinePaused, continuation.IsPipelineFailed, false) });
        Assert.False(resumed.IsRunPaused); Assert.Equal(hasContext, resumed.IsPresetReconversionRun);
        Assert.False(resumed.IsProjectExtensionRun);
    }

    private static ExportUiStateSource CreateSource(
        string? outputFolder = null,
        object? selectedResumeProject = null) =>
        new(
            ProjectPipelineState.ReviewBeforeMerge,
            new PipelineExecutionState(
                IsBusy: true,
                IsPaused: true,
                IsFailed: true,
                IsProjectExtensionRun: true),
            ManualMergeReviewNeedsReconversion: true,
            ManualMergeReviewHasPreparedTracks: true,
            FfmpegAvailable: true,
            new PipelineProjectState(
                NeedsDiscImport: true,
                IsMp3Disc: true,
                IsAudioDisc: true,
                IsLocal: true),
            TrackCount: 7,
            OutputFolder: outputFolder,
            ExportProgressPercent: 42.5,
            SelectedResumeProject: selectedResumeProject,
            IsPresetReconversionRun: true);
}
