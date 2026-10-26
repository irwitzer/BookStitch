using BookStitch.Models;

namespace BookStitch.Services;

public static class AnimationTimingSettingsService
{
    public const int MetadataPanelDefaultMilliseconds = 550;
    public const int ProjectSetupPageDefaultMilliseconds = 220;
    public const int MinimumMilliseconds = 0;
    public const int MaximumMilliseconds = 2000;

    public static int GetMetadataPanelMilliseconds(DebugSettings? debugSettings) =>
        Normalize(debugSettings?.MetadataPanelAnimationMillisecondsOverride
                  ?? MetadataPanelDefaultMilliseconds);

    public static int GetProjectSetupPageMilliseconds(DebugSettings? debugSettings) =>
        Normalize(debugSettings?.ProjectSetupPageAnimationMillisecondsOverride
                  ?? ProjectSetupPageDefaultMilliseconds);

    public static int? ToMetadataPanelOverride(int milliseconds)
    {
        var normalized = Normalize(milliseconds);
        return normalized == MetadataPanelDefaultMilliseconds ? null : normalized;
    }

    public static int? ToProjectSetupPageOverride(int milliseconds)
    {
        var normalized = Normalize(milliseconds);
        return normalized == ProjectSetupPageDefaultMilliseconds ? null : normalized;
    }

    public static int Normalize(int milliseconds) =>
        Math.Clamp(milliseconds, MinimumMilliseconds, MaximumMilliseconds);
}
