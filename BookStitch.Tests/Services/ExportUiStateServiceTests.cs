using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportUiStateServiceTests
{
    private readonly ExportUiStateService _service = new();

    private static ExportUiStateInput Input(
        ProjectPipelineState state,
        bool isBusy = false,
        bool isRunPaused = false,
        bool isRunFailed = false,
        bool isProjectExtensionRun = false,
        bool needsReconversion = false,
        bool needsDiscImport = false,
        bool isMp3Disc = false,
        bool isAudioDisc = false,
        bool isLocal = false,
        int trackCount = 10,
        bool hasOutputFolder = true,
        double progress = 0,
        bool hasSelectedProject = false) => new(
            CurrentState: state,
            IsBusy: isBusy,
            IsRunPaused: isRunPaused,
            IsRunFailed: isRunFailed,
            IsProjectExtensionRun: isProjectExtensionRun,
            ManualMergeReviewNeedsReconversion: needsReconversion,
            FfmpegAvailable: true,
            LoadedResumeProjectNeedsDiscImport: needsDiscImport,
            LoadedResumeProjectIsMp3Disc: isMp3Disc,
            LoadedResumeProjectIsAudioDisc: isAudioDisc,
            LoadedResumeProjectIsLocal: isLocal,
            TrackCount: trackCount,
            HasOutputFolder: hasOutputFolder,
            ExportProgressPercent: progress,
            HasSelectedResumeProject: hasSelectedProject);

    [Fact]
    public void Create_WhenPreparingWithTracks_AllowsProjectStartAndEditing()
    {
        var state = _service.Create(Input(ProjectPipelineState.Preparing, trackCount: 3));

        Assert.Equal(ProjectPipelineState.Preparing, state.Phase);
        Assert.True(state.CanStartExport);
        Assert.True(state.CanEditTrackOrder);
        Assert.Equal("Projekt starten", state.ExportButtonText);
        Assert.Equal(Visibility.Collapsed, state.ExportProgressVisibility);
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
    [InlineData(ProjectPipelineState.Merging)]
    public void Create_WhenPipelineIsActive_LocksEditingAndAllowsCancel(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(phase, isBusy: true, progress: 35));

        Assert.Equal(phase, state.Phase);
        Assert.False(state.CanChangeBookMetadata);
        Assert.False(state.CanEditTrackOrder);
        Assert.False(state.CanChangeExportPreset);
        Assert.True(state.CanCancelExport);
        Assert.Equal(Visibility.Visible, state.ExportProgressVisibility);
    }

    [Fact]
    public void Create_WhenReviewing_AllowsProjectEditingButNotProjectSwitch()
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
        Assert.False(state.CanSelectFolder);
        Assert.False(state.CanRefreshResumeProjects);
        Assert.Equal("Zusammenfügen", state.ExportButtonText);
        Assert.Equal("Projekt schließen", state.SecondaryButtonText);
        Assert.True(state.CanCancelExport);
    }

    [Fact]
    public void Create_WhenReviewPresetNeedsReconversion_UsesReconversionAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.ReviewBeforeMerge,
            needsReconversion: true,
            progress: 100));

        Assert.Equal("Neu konvertieren", state.ExportButtonText);
    }

    [Fact]
    public void Create_WhenCompleted_KeepsProjectEditableAndAllowsProjectSwitch()
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
        Assert.True(state.CanAddProjectSources);
        Assert.Equal("Zusammenfügen", state.ExportButtonText);
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
    public void Create_WhenCurrentPresetNeedsReconversion_HidesSourceExtensionForEveryProjectType(
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

        Assert.False(state.CanAddProjectSources);
        Assert.Equal(Visibility.Collapsed, state.AddProjectSourcesVisibility);
        Assert.Equal("Neu konvertieren", state.ExportButtonText);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPipelineIsPaused_EnablesContinueAndSafeEditing(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(
            phase,
            isRunPaused: true,
            isLocal: true,
            progress: 40));

        Assert.True(state.CanStartExport);
        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal("Projekt abbrechen", state.SecondaryButtonText);
        Assert.True(state.CanCancelExport);
        Assert.True(state.CanChangeBookMetadata);
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
    public void Create_WhenPipelineFailed_UsesProjectCancelAction(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(
            phase,
            isRunFailed: true,
            isLocal: true,
            progress: 40));

        Assert.False(state.CanStartExport);
        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal("Projekt abbrechen", state.SecondaryButtonText);
        Assert.True(state.CanCancelExport);
        Assert.False(state.CanOpenSettings);
        Assert.False(state.CanAddProjectSources);
    }


    [Fact]
    public void Create_WhenProjectExtensionIsPaused_UsesStopAction()
    {
        var state = _service.Create(Input(
            ProjectPipelineState.AcquiringSources,
            isRunPaused: true,
            isProjectExtensionRun: true,
            isLocal: true));

        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.Equal("Stoppen", state.SecondaryButtonText);
        Assert.True(state.CanStartExport);
        Assert.True(state.CanCancelExport);
    }

    [Theory]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    public void Create_WhenPipelineIsRunning_UsesPauseAction(ProjectPipelineState phase)
    {
        var state = _service.Create(Input(phase, isBusy: true));

        Assert.Equal("Weiter", state.ExportButtonText);
        Assert.False(state.CanStartExport);
        Assert.Equal("Pause", state.SecondaryButtonText);
        Assert.True(state.CanCancelExport);
    }

}
