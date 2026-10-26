namespace BookStitch.Services;

public enum PrimaryButtonVisualState
{
    Primary,
    Muted,
    Disabled
}

public static class MainActionButtonVisualStateService
{
    public static PrimaryButtonVisualState ResolveProjectAction(bool hasLoadedProject, bool isEnabled)
    {
        if (!isEnabled)
            return PrimaryButtonVisualState.Disabled;

        return hasLoadedProject
            ? PrimaryButtonVisualState.Muted
            : PrimaryButtonVisualState.Primary;
    }

    public static PrimaryButtonVisualState ResolveMetadataAction(bool canChangeBookMetadata) =>
        canChangeBookMetadata
            ? PrimaryButtonVisualState.Primary
            : PrimaryButtonVisualState.Disabled;
}
