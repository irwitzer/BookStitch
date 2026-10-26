using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ChapterCustomEngineService
{
    private readonly ChapterTimelineService _chapterTimelineService;
    private readonly ChapterCustomTitleService _customTitleService;

    public ChapterCustomEngineService()
        : this(new ChapterTimelineService(), new ChapterCustomTitleService())
    {
    }

    public ChapterCustomEngineService(
        ChapterTimelineService chapterTimelineService,
        ChapterCustomTitleService customTitleService)
    {
        _chapterTimelineService = chapterTimelineService ?? throw new ArgumentNullException(nameof(chapterTimelineService));
        _customTitleService = customTitleService ?? throw new ArgumentNullException(nameof(customTitleService));
    }

    public ActiveChapterResult Apply(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = ChapterSettingsService.Normalize(request.ChapterSettings, request.LegacyUseLeadingZeros);
        if (!string.Equals(settings.Mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal))
            throw new InvalidOperationException("ChapterCustomEngineService can only process custom chapter settings.");

        var activeTracks = request.Tracks.Where(track => !track.IsExcluded).ToArray();
        var timeline = _chapterTimelineService.BuildTimeline(activeTracks);
        var activeChapterCount = Math.Max(timeline.Count, request.EffectiveActiveChapterCount ?? 0);
        if (activeChapterCount == 0)
        {
            ApplyTrackIndexes(request.Tracks);
            return new ActiveChapterResult(settings.Mode, []);
        }

        var renderedTimeline = timeline
            .Select(chapter => chapter with
            {
                Title = _customTitleService.Render(
                    CreateChapterTitleContextTrack(chapter),
                    settings,
                    chapter.OutputChapterIndex,
                    activeChapterCount,
                    request.CanonicalMetadata.TitleValue,
                    request.CanonicalMetadata.AlbumValue)
            })
            .ToArray();

        ApplyTrackIndexes(request.Tracks);
        ApplyFirstRenderedTitlePerTrack(request.Tracks, renderedTimeline);
        return new ActiveChapterResult(settings.Mode, renderedTimeline);
    }

    private static void ApplyTrackIndexes(IEnumerable<TrackInfo> tracks)
    {
        var activeIndex = 0;
        foreach (var track in tracks)
            track.Index = track.IsExcluded ? 0 : ++activeIndex;
    }

    private static void ApplyFirstRenderedTitlePerTrack(
        IEnumerable<TrackInfo> tracks,
        IReadOnlyList<EffectiveChapterInfo> renderedTimeline)
    {
        var firstByTrack = renderedTimeline
            .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
            .ToDictionary(group => group.Key, group => group.First(), ReferenceEqualityComparer<TrackInfo>.Instance);

        foreach (var track in tracks.Where(track => !track.IsExcluded))
        {
            if (firstByTrack.TryGetValue(track, out var chapter))
                track.ChapterTitle = chapter.Title;
        }
    }

    private static TrackInfo CreateChapterTitleContextTrack(EffectiveChapterInfo chapter)
    {
        if (chapter.IsSynthetic)
            return chapter.SourceTrack;

        var source = chapter.SourceTrack;
        return new TrackInfo
        {
            Index = source.Index,
            DiscNumber = source.DiscNumber,
            TrackNumber = source.TrackNumber,
            AutomaticOrderPosition = source.AutomaticOrderPosition,
            FilePath = source.FilePath,
            RelativeFolder = source.RelativeFolder,
            ProjectRelativeFolder = source.ProjectRelativeFolder,
            Extension = source.Extension,
            Artist = source.Artist,
            Duration = source.Duration,
            DurationTicks = chapter.EndTicks - chapter.StartTicks,
            TagTitle = chapter.Title,
            ChapterTitle = chapter.Title,
            FileName = string.IsNullOrWhiteSpace(chapter.Title)
                ? source.FileName
                : chapter.Title
        };
    }
}
