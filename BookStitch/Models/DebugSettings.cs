namespace BookStitch.Models;

public sealed class DebugSettings
{
    public const double DefaultWindowWidth = 1080;
    public const double DefaultWindowHeight = 760;
    public const int DefaultTrackGridRowHeightLevel = 0;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = DefaultWindowWidth;

    public double WindowHeight { get; set; } = DefaultWindowHeight;

    public string LastTab { get; set; } = "Status";

    public bool ForceShowFfmpegSetupButton { get; set; }

    public bool PerformanceDiagnosticsEnabled { get; set; }

    public bool UseBoxedDiscWaitDialog { get; set; }

    public int TrackGridRowHeightLevel { get; set; } = DefaultTrackGridRowHeightLevel;

    public int? MetadataPanelAnimationMillisecondsOverride { get; set; }

    public int? ProjectSetupPageAnimationMillisecondsOverride { get; set; }

    public bool RememberDeveloperToolsWindowPlacement { get; set; } = true;

    public bool RememberGlobalSettingsWindowPlacement { get; set; }

    public bool RememberProjectSetupDialogPlacement { get; set; }

    public bool RememberConceptDialogPlacement { get; set; }

    public bool RememberResumeProjectDialogPlacement { get; set; }

    public DebugWindowPlacement GlobalSettingsWindowPlacement { get; set; } = new();

    public DebugWindowPlacement ProjectSetupDialogPlacement { get; set; } = new();

    public DebugWindowPlacement ConceptDialogPlacement { get; set; } = new();

    public DebugWindowPlacement ResumeProjectDialogPlacement { get; set; } = new();
}

public sealed class DebugWindowPlacement
{
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }
}
