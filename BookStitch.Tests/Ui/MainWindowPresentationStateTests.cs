using BookStitch.Presentation;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MainWindowPresentationStateTests
{
    [Fact]
    public void Progress_is_projected_behind_transition_then_current_status_returns_on_release()
    {
        var state = new MainWindowPresentationState();
        var owner = state.BeginUiTransition(PipelineUiTransitionKind.Pausing, PipelineUiTransitionTarget.Primary);
        Assert.Equal(LocalizationText.Get("Pipeline.Pause.Preparing"), state.ProjectStatus("copy progress"));
        Assert.Equal(LocalizationText.Get("Pipeline.Pause.Preparing"), state.ProjectStatus("late ffmpeg progress"));
        state.EndUiTransition(owner);
        Assert.Equal("pause completed", state.ProjectStatus("pause completed"));
    }

    [Fact]
    public void Duplicate_or_nested_transition_cannot_replace_or_release_the_original_owner()
    {
        var state = new MainWindowPresentationState();
        var owner = state.BeginUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.DiscStopSave);
        Assert.Null(state.BeginUiTransition(PipelineUiTransitionKind.Pausing, PipelineUiTransitionTarget.Primary));
        state.EndUiTransition(null);
        Assert.Same(owner, state.UiTransition);
        Assert.Equal(LocalizationText.Get("Pipeline.Transition.StopSaving"), state.ProjectStatus("rip progress"));
        state.EndUiTransition(owner);
        Assert.Null(state.UiTransition);
        Assert.Equal("save failed", state.ProjectStatus("save failed"));
    }

    [Fact]
    public void Old_completion_cannot_clear_transition_of_a_new_project_even_with_same_kind_and_target()
    {
        var state = new MainWindowPresentationState();
        var old = state.BeginUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary);
        state.ClearUiTransition();
        var current = state.BeginUiTransition(PipelineUiTransitionKind.Stopping, PipelineUiTransitionTarget.Secondary);
        state.EndUiTransition(old);
        Assert.Same(current, state.UiTransition);
    }

    [Fact]
    public void Pause_requested_without_ui_owner_still_has_priority_without_claiming_pause_completed()
    {
        var state = new MainWindowPresentationState { PauseRequested = true };
        Assert.False(state.IsPipelinePaused);
        Assert.Equal(LocalizationText.Get("Pipeline.Pause.Preparing"), state.ProjectStatus("ordinary progress"));
        state.PauseRequested = false;
        Assert.Equal("ordinary progress", state.ProjectStatus("ordinary progress"));
    }

    [Fact]
    public void New_state_starts_with_all_boolean_values_false()
    {
        var state = new MainWindowPresentationState();

        Assert.False(state.IsBusy);
        Assert.False(state.IsExporting);
        Assert.False(state.IsDiscImporting);
        Assert.False(state.IsPipelinePaused);
        Assert.False(state.IsPipelineFailed);
        Assert.False(state.IsProjectExtensionRun);
        Assert.False(state.PauseRequested);
    }

    [Theory]
    [InlineData(nameof(MainWindowPresentationState.IsBusy))]
    [InlineData(nameof(MainWindowPresentationState.IsExporting))]
    [InlineData(nameof(MainWindowPresentationState.IsDiscImporting))]
    [InlineData(nameof(MainWindowPresentationState.IsPipelinePaused))]
    [InlineData(nameof(MainWindowPresentationState.IsPipelineFailed))]
    [InlineData(nameof(MainWindowPresentationState.IsProjectExtensionRun))]
    [InlineData(nameof(MainWindowPresentationState.PauseRequested))]
    public void Boolean_state_property_can_be_changed(
        string propertyName)
    {
        var state = new MainWindowPresentationState();
        var property = typeof(MainWindowPresentationState).GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property {propertyName} was not found.");

        property.SetValue(state, true);

        Assert.True((bool)(property.GetValue(state)
            ?? throw new InvalidOperationException(
                $"Property {propertyName} returned null.")));

        property.SetValue(state, false);

        Assert.False((bool)(property.GetValue(state)
            ?? throw new InvalidOperationException(
                $"Property {propertyName} returned null.")));
    }
}