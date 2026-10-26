using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Central productive selector for the one chapter state visible in UI, review, and export.
/// </summary>
public sealed class ChapterModeEngineService
{
    private readonly ChapterAutoEngineService _automatedEngine;
    private readonly ChapterMasterEngineService _chapterMasterEngine;
    private readonly ChapterOriginalEngineService _originalEngine;
    private readonly ChapterCustomEngineService _customEngine;

    public ChapterModeEngineService()
        : this(
            new ChapterAutoEngineService(),
            new ChapterMasterEngineService(),
            new ChapterOriginalEngineService(),
            new ChapterCustomEngineService())
    {
    }

    public ChapterModeEngineService(
        ChapterAutoEngineService automatedEngine,
        ChapterMasterEngineService chapterMasterEngine,
        ChapterCustomEngineService customEngine)
        : this(
            automatedEngine,
            chapterMasterEngine,
            new ChapterOriginalEngineService(),
            customEngine)
    {
    }

    public ChapterModeEngineService(
        ChapterAutoEngineService automatedEngine,
        ChapterMasterEngineService chapterMasterEngine,
        ChapterOriginalEngineService originalEngine,
        ChapterCustomEngineService customEngine)
    {
        _automatedEngine = automatedEngine ?? throw new ArgumentNullException(nameof(automatedEngine));
        _chapterMasterEngine = chapterMasterEngine ?? throw new ArgumentNullException(nameof(chapterMasterEngine));
        _originalEngine = originalEngine ?? throw new ArgumentNullException(nameof(originalEngine));
        _customEngine = customEngine ?? throw new ArgumentNullException(nameof(customEngine));
    }

    public ActiveChapterResult Apply(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = ChapterSettingsService.Normalize(
            request.ChapterSettings,
            request.LegacyUseLeadingZeros);

        if (string.Equals(settings.Mode, ChapterSettingsService.ModeAuto, StringComparison.Ordinal))
            return FromEngine(settings.Mode, _automatedEngine.Apply(request with { ChapterSettings = settings }));

        if (string.Equals(settings.Mode, ChapterSettingsService.ModeChapterMaster, StringComparison.Ordinal))
            return FromEngine(settings.Mode, _chapterMasterEngine.Apply(request with { ChapterSettings = settings }));

        if (string.Equals(settings.Mode, ChapterSettingsService.ModeOriginal, StringComparison.Ordinal))
            return _originalEngine.Apply(request with { ChapterSettings = settings });

        if (string.Equals(settings.Mode, ChapterSettingsService.ModeNone, StringComparison.Ordinal))
        {
            ApplyIndexesAndClearChapters(request.Tracks);
            return new ActiveChapterResult(settings.Mode, []);
        }

        if (string.Equals(settings.Mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal))
            return _customEngine.Apply(request with { ChapterSettings = settings });

        ApplyIndexesAndClearChapters(request.Tracks);
        return new ActiveChapterResult(settings.Mode, []);
    }

    public void ResetChapterMasterToAutomatedBaseline() =>
        _chapterMasterEngine.ResetToAutomatedBaseline();

    private static ActiveChapterResult FromEngine(string mode, ChapterAutoEngineResult result) =>
        new(mode, result.RenderedTimeline, result);

    private static void ApplyIndexesAndClearChapters(IEnumerable<TrackInfo> tracks)
    {
        var activeIndex = 0;
        foreach (var track in tracks)
        {
            track.Index = track.IsExcluded ? 0 : ++activeIndex;
            track.ChapterTitle = string.Empty;
        }
    }
}
