using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainActionButtonVisualStateServiceTests
{
    [Theory]
    [InlineData(false, true, PrimaryButtonVisualState.Primary)]
    [InlineData(true, true, PrimaryButtonVisualState.Muted)]
    [InlineData(false, false, PrimaryButtonVisualState.Disabled)]
    [InlineData(true, false, PrimaryButtonVisualState.Disabled)]
    public void Project_actions_map_loaded_and_enabled_state(
        bool hasLoadedProject,
        bool isEnabled,
        PrimaryButtonVisualState expected)
    {
        Assert.Equal(expected, MainActionButtonVisualStateService.ResolveProjectAction(hasLoadedProject, isEnabled));
    }

    [Theory]
    [InlineData(true, PrimaryButtonVisualState.Primary)]
    [InlineData(false, PrimaryButtonVisualState.Disabled)]
    public void Metadata_action_is_primary_only_while_editing_is_allowed(
        bool canChangeBookMetadata,
        PrimaryButtonVisualState expected)
    {
        Assert.Equal(expected, MainActionButtonVisualStateService.ResolveMetadataAction(canChangeBookMetadata));
    }
}
