using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ChapterSettingsPreviewService
{
    private readonly ChapterTimelineService _chapterTimelineService;
    private readonly ChapterTitleRenderer _titleRenderer = new();
    private readonly ChapterCustomEngineService _customEngineService;
    private readonly ChapterOriginalEngineService _originalEngineService;

    public ChapterSettingsPreviewService()
        : this(new ChapterTimelineService(), new ChapterCustomEngineService(), new ChapterOriginalEngineService())
    {
    }

    public ChapterSettingsPreviewService(ChapterTimelineService chapterTimelineService)
        : this(chapterTimelineService, new ChapterCustomEngineService(), new ChapterOriginalEngineService())
    {
    }

    public ChapterSettingsPreviewService(
        ChapterTimelineService chapterTimelineService,
        ChapterCustomEngineService customEngineService,
        ChapterOriginalEngineService originalEngineService)
    {
        _chapterTimelineService = chapterTimelineService ?? throw new ArgumentNullException(nameof(chapterTimelineService));
        _customEngineService = customEngineService ?? throw new ArgumentNullException(nameof(customEngineService));
        _originalEngineService = originalEngineService ?? throw new ArgumentNullException(nameof(originalEngineService));
    }

    public string CreatePreview(
        ChapterSettings? settings,
        bool legacyUseLeadingZeros,
        ChapterPreviewData? currentProjectData,
        int maxItems = 1)
    {
        maxItems = Math.Clamp(maxItems, 1, 5);
        var normalized = ChapterSettingsService.Normalize(settings, legacyUseLeadingZeros);
        var useProject = string.Equals(
            normalized.PreviewDataSource,
            ChapterSettingsService.PreviewDataSourceCurrentProject,
            StringComparison.Ordinal) &&
                         (maxItems > 1 ? currentProjectData is not null : currentProjectData is { Tracks.Count: > 0 });
        var data = useProject ? currentProjectData! : CreateSampleData();
        var tracks = CloneTracks(data.Tracks);
        int? effectiveChapterCount = useProject ? null : 123;

        if (string.Equals(normalized.Mode, ChapterSettingsService.ModeNone, StringComparison.Ordinal))
            return string.Empty;

        if (useProject && data.RenderedTimeline is not null)
            return JoinPreviewTitles(data.RenderedTimeline.Select(chapter => chapter.Title), maxItems);

        if (ChapterSettingsService.UsesAutomaticEngine(normalized.Mode))
        {
            if (useProject)
                ApplyCurrentProjectResolvedAutoContents(tracks, data);
            else
                ApplySampleAutoContents(tracks);

            ApplyAutoNumbering(
                tracks,
                ChapterSettingsService.GetNumberingStyle(normalized, legacyUseLeadingZeros),
                effectiveChapterCount);
        }
        else if (string.Equals(normalized.Mode, ChapterSettingsService.ModeOriginal, StringComparison.Ordinal))
        {
            return CreateOriginalPreview(normalized, legacyUseLeadingZeros, data, maxItems);
        }
        else
        {
            return JoinPreviewTitles(_customEngineService.Apply(CreatePreviewRequest(
                    tracks,
                    normalized,
                    legacyUseLeadingZeros,
                    data,
                    effectiveChapterCount))
                .RenderedTimeline.Select(chapter => chapter.Title), maxItems);
        }

        if (maxItems > 1)
            return JoinPreviewTitles(tracks.Where(track => !track.IsExcluded).OrderBy(track => track.Index)
                .Select(track => track.ChapterTitle), maxItems);

        var previewTrack = FindSyntheticPreviewTrack(tracks);
        return previewTrack?.ChapterTitle
               ?? tracks.FirstOrDefault(track => !track.IsExcluded)?.ChapterTitle
               ?? tracks.FirstOrDefault()?.ChapterTitle
               ?? string.Empty;
    }

    private static string JoinPreviewTitles(IEnumerable<string> titles, int maxItems) =>
        string.Join(Environment.NewLine, titles.Take(maxItems));

    private string CreateOriginalPreview(
        ChapterSettings settings,
        bool legacyUseLeadingZeros,
        ChapterPreviewData data,
        int maxItems)
    {
        var tracks = CloneTracks(data.Tracks);
        if (tracks.All(track => track.EmbeddedChapters.Count == 0))
        {
            var firstTrack = tracks.FirstOrDefault(track => !track.IsExcluded) ?? tracks.FirstOrDefault();
            if (firstTrack is not null)
            {
                firstTrack.DurationTicks = Math.Max(firstTrack.DurationTicks ?? 0, TimeSpan.FromSeconds(1).Ticks);
                firstTrack.EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = firstTrack.TagTitle,
                        StartTicks = 0,
                        EndTicks = TimeSpan.FromSeconds(1).Ticks
                    }
                ];
            }
        }

        return JoinPreviewTitles(_originalEngineService.Apply(CreatePreviewRequest(
                tracks,
                settings,
                legacyUseLeadingZeros,
                data,
                effectiveChapterCount: null))
            .RenderedTimeline.Select(chapter => chapter.Title), maxItems);
    }

    private static ChapterAutoEngineRequest CreatePreviewRequest(
        IList<TrackInfo> tracks,
        ChapterSettings settings,
        bool legacyUseLeadingZeros,
        ChapterPreviewData data,
        int? effectiveChapterCount) =>
        new(
            tracks,
            ProjectManifestTypes.FolderProject,
            "chapter-preview",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(
                0,
                data.BookTitle,
                MetadataOrigin.User,
                data.Album,
                MetadataOrigin.User),
            settings,
            legacyUseLeadingZeros,
            effectiveChapterCount);

    public static ChapterPreviewData CreateSampleData()
    {
        return new ChapterPreviewData(
            LocalizationText.Get("Settings.ChapterSample.BookTitle"),
            LocalizationText.Get("Settings.ChapterSample.Album"),
            [
                new TrackInfo { FileName = LocalizationText.Get("Settings.ChapterSample.File1"), TagTitle = LocalizationText.Get("Settings.ChapterSample.Tag1") },
                new TrackInfo { FileName = LocalizationText.Get("Settings.ChapterSample.File2"), TagTitle = LocalizationText.Get("Settings.ChapterSample.Tag2") },
                new TrackInfo { FileName = LocalizationText.Get("Settings.ChapterSample.File3"), TagTitle = LocalizationText.Get("Settings.ChapterSample.Tag3") },
                new TrackInfo { FileName = LocalizationText.Get("Settings.ChapterSample.File4"), TagTitle = LocalizationText.Get("Settings.ChapterSample.Tag4") },
                new TrackInfo { FileName = LocalizationText.Get("Settings.ChapterSample.File5"), TagTitle = LocalizationText.Get("Settings.ChapterSample.Tag5") }
            ]);
    }

    private static void ApplySampleAutoContents(IReadOnlyList<TrackInfo> tracks)
    {
        var sampleContents = new[]
        {
            LocalizationText.Get("Settings.ChapterSample.Content1"),
            LocalizationText.Get("Settings.ChapterSample.Content2"),
            LocalizationText.Get("Settings.ChapterSample.Content3"),
            LocalizationText.Get("Settings.ChapterSample.Content4"),
            LocalizationText.Get("Settings.ChapterSample.Content5")
        };

        for (var index = 0; index < tracks.Count && index < sampleContents.Length; index++)
            tracks[index].ChapterTitle = sampleContents[index];
    }

    private void ApplyCurrentProjectResolvedAutoContents(
        IReadOnlyList<TrackInfo> tracks,
        ChapterPreviewData data)
    {
        foreach (var track in tracks)
        {
            if (track.SourceId != Guid.Empty &&
                data.ResolvedAutoContentsBySourceId?.TryGetValue(track.SourceId, out var content) == true)
            {
                track.ChapterTitle = content;
                continue;
            }

            if (_titleRenderer.TryGetTitleWithoutNumber(track.ChapterTitle, out var titleWithoutNumber))
                track.ChapterTitle = titleWithoutNumber;
        }
    }

    private void ApplyAutoNumbering(
        IList<TrackInfo> tracks,
        ChapterNumberingStyle numberingStyle,
        int? effectiveChapterCount)
    {
        var activeTracks = tracks.Where(track => !track.IsExcluded).ToList();
        var timeline = _chapterTimelineService.BuildTimeline(activeTracks);
        var totalChapterCount = Math.Max(timeline.Count, effectiveChapterCount ?? 0);
        if (totalChapterCount == 0)
            return;

        foreach (var chapter in timeline
                     .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
                     .Select(group => group.First()))
        {
            chapter.SourceTrack.ChapterTitle = _titleRenderer.RenderContent(
                chapter.OutputChapterIndex,
                totalChapterCount,
                chapter.SourceTrack.ChapterTitle,
                numberingStyle);
        }
    }

    private static List<TrackInfo> CloneTracks(IEnumerable<TrackInfo> tracks)
    {
        return tracks
            .Select(track => new TrackInfo
            {
                SourceId = track.SourceId,
                SourceTrackNumber = track.SourceTrackNumber,
                SourceDiscNumber = track.SourceDiscNumber,
                SourceMetadataFields = track.SourceMetadataFields,
                Index = track.Index,
                IsExcluded = track.IsExcluded,
                DiscNumber = track.DiscNumber,
                TrackNumber = track.TrackNumber,
                AutomaticOrderPosition = track.AutomaticOrderPosition,
                FilePath = track.FilePath,
                FileName = track.FileName,
                RelativeFolder = track.RelativeFolder,
                ProjectRelativeFolder = track.ProjectRelativeFolder,
                Extension = track.Extension,
                TagTitle = track.TagTitle,
                Artist = track.Artist,
                ChapterTitle = track.ChapterTitle,
                EmbeddedChapters = track.EmbeddedChapters
                    .Select(chapter => new TrackEmbeddedChapterInfo
                    {
                        Title = chapter.Title,
                        StartTicks = chapter.StartTicks,
                        EndTicks = chapter.EndTicks
                    })
                    .ToList(),
                Duration = track.Duration,
                DurationTicks = track.DurationTicks
            })
            .ToList();
    }

    private TrackInfo? FindSyntheticPreviewTrack(IReadOnlyList<TrackInfo> tracks)
    {
        var activeTracks = tracks.Where(track => !track.IsExcluded).ToList();
        if (activeTracks.Count == 0)
            return null;

        return _chapterTimelineService.BuildTimeline(activeTracks)
            .FirstOrDefault(chapter => chapter.IsSynthetic)
            ?.SourceTrack;
    }
}

public sealed record ChapterPreviewData(
    string BookTitle,
    string Album,
    IReadOnlyList<TrackInfo> Tracks,
    IReadOnlyDictionary<Guid, string>? ResolvedAutoContentsBySourceId = null,
    IReadOnlyList<EffectiveChapterInfo>? RenderedTimeline = null);
