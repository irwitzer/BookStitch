namespace BookStitch.Services;

/// <summary>
/// Independent development entry point whose current baseline is the stable V4 auto engine.
/// Experimental Chapter Master rules belong here and must not be added to ChapterAutoEngineService.
/// DEVELOPMENT STATE: SYNCED_WITH_AUTO.
/// Master must stay functionally synced with Auto until an explicit fork request.
/// During a later INTENTIONAL_FORK, Master changes must not be auto-backported to Auto
/// or erased by parity fixes; resynchronization also requires an explicit request.
/// </summary>
public sealed class ChapterMasterEngineService
{
    private readonly Func<ChapterAutoEngineService> _automatedBaselineFactory;
    private ChapterAutoEngineService _engine;

    public ChapterMasterEngineService()
        : this(() => new ChapterAutoEngineService())
    {
    }

    internal ChapterMasterEngineService(Func<ChapterAutoEngineService> automatedBaselineFactory)
    {
        _automatedBaselineFactory = automatedBaselineFactory ?? throw new ArgumentNullException(nameof(automatedBaselineFactory));
        _engine = _automatedBaselineFactory();
    }

    public ChapterAutoEngineResult Apply(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var automatedSettings = ChapterSettingsService.Normalize(
            request.ChapterSettings,
            request.LegacyUseLeadingZeros);
        automatedSettings = CloneWithMode(automatedSettings, ChapterSettingsService.ModeAuto);
        return _engine.Apply(request with { ChapterSettings = automatedSettings });
    }

    public void ResetToAutomatedBaseline()
    {
        _engine = _automatedBaselineFactory();
    }

    private static Models.ChapterSettings CloneWithMode(Models.ChapterSettings settings, string mode) => new()
    {
        Mode = mode,
        NumberingStyle = settings.NumberingStyle,
        PreviewDataSource = settings.PreviewDataSource,
        Slot1 = settings.Slot1,
        Separator1 = settings.Separator1,
        Slot2 = settings.Slot2,
        Separator2 = settings.Separator2,
        Slot3 = settings.Slot3
    };
}
