namespace BookStitch.Services;

public sealed class MainWindowServiceComposition
{
    private MainWindowServiceComposition(
        MainWindowFoundationComposition foundation,
        MainWindowMediaComposition media,
        MainWindowTrackComposition tracks,
        MainWindowDiscComposition discs,
        MainWindowStatusComposition status,
        MainWindowPipelineComposition pipeline,
        MainWindowWorkflowComposition workflows)
    {
        Foundation = foundation;
        Media = media;
        Tracks = tracks;
        Discs = discs;
        Status = status;
        Pipeline = pipeline;
        Workflows = workflows;
    }

    public MainWindowFoundationComposition Foundation { get; }
    public MainWindowMediaComposition Media { get; }
    public MainWindowTrackComposition Tracks { get; }
    public MainWindowDiscComposition Discs { get; }
    public MainWindowStatusComposition Status { get; }
    public MainWindowPipelineComposition Pipeline { get; }
    public MainWindowWorkflowComposition Workflows { get; }

    public static MainWindowServiceComposition Create(
        BookStitchEnvironmentContext environmentContext,
        Func<string> getFfmpegPath,
        Func<bool> useBoxedDiscWaitDialog)
    {
        ArgumentNullException.ThrowIfNull(environmentContext);
        ArgumentNullException.ThrowIfNull(getFfmpegPath);
        ArgumentNullException.ThrowIfNull(useBoxedDiscWaitDialog);

        var foundation = MainWindowFoundationComposition.Create(environmentContext);
        var media = MainWindowMediaComposition.Create(() => foundation.GlobalSettingsManager.Current.CoverTransparencyMode);
        var tracks = MainWindowTrackComposition.Create();
        var discs = MainWindowDiscComposition.Create();
        var status = MainWindowStatusComposition.Create();
        var pipeline = MainWindowPipelineComposition.Create();

        var workflows = MainWindowWorkflowComposition.Create(
            foundation,
            media.FfmpegRunnerService,
            pipeline.LocalProjectImportService,
            getFfmpegPath,
            discs.Mp3DiscPreparationService,
            discs.Mp3DiscImportService,
            discs.DiscDriveService,
            discs.AudioDiscPipelineTimingService,
            useBoxedDiscWaitDialog,
            discs.AudioDiscReaderService);

        return new MainWindowServiceComposition(
            foundation,
            media,
            tracks,
            discs,
            status,
            pipeline,
            workflows);
    }
}