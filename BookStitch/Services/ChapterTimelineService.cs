using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class ChapterTimelineService
{
    public IReadOnlyList<EffectiveChapterInfo> BuildTimeline(IReadOnlyList<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var result = new List<EffectiveChapterInfo>();
        var currentStartTicks = 0L;

        foreach (var track in tracks)
        {
            var trackDurationTicks = TrackDurationService.GetEffectiveDurationTicks(track);
            var validChapters = NormalizeEmbeddedChapters(track, trackDurationTicks).ToList();

            if (validChapters.Count == 0)
            {
                AddSyntheticChapter(result, track, currentStartTicks, trackDurationTicks);
            }
            else
            {
                foreach (var chapter in validChapters)
                    AddEmbeddedChapter(result, track, chapter, currentStartTicks);
            }

            currentStartTicks += trackDurationTicks;
        }

        return result;
    }

    private static IEnumerable<NormalizedEmbeddedChapter> NormalizeEmbeddedChapters(
        TrackInfo track,
        long trackDurationTicks)
    {
        foreach (var chapter in track.EmbeddedChapters.OrderBy(chapter => chapter.StartTicks))
        {
            if (chapter.EndTicks <= chapter.StartTicks)
                continue;

            var startTicks = Math.Max(0, chapter.StartTicks);
            if (startTicks >= trackDurationTicks)
                continue;

            var endTicks = Math.Min(chapter.EndTicks, trackDurationTicks);
            if (endTicks <= startTicks)
                continue;

            yield return new NormalizedEmbeddedChapter(
                chapter,
                chapter.Title.Trim(),
                startTicks,
                endTicks);
        }
    }

    private static void AddSyntheticChapter(
        List<EffectiveChapterInfo> result,
        TrackInfo track,
        long globalStartTicks,
        long trackDurationTicks)
    {
        var outputChapterIndex = result.Count + 1;
        result.Add(new EffectiveChapterInfo(
            outputChapterIndex,
            track,
            SourceEmbeddedChapter: null,
            BuildSyntheticChapterTitle(track, outputChapterIndex),
            globalStartTicks,
            globalStartTicks + trackDurationTicks,
            SourceStartTicks: 0,
            SourceEndTicks: trackDurationTicks,
            IsSynthetic: true));
    }

    private static void AddEmbeddedChapter(
        List<EffectiveChapterInfo> result,
        TrackInfo track,
        NormalizedEmbeddedChapter chapter,
        long globalTrackStartTicks)
    {
        var outputChapterIndex = result.Count + 1;
        result.Add(new EffectiveChapterInfo(
            outputChapterIndex,
            track,
            chapter.Source,
            string.IsNullOrWhiteSpace(chapter.Title)
                ? BuildFallbackChapterTitle(outputChapterIndex)
                : chapter.Title,
            globalTrackStartTicks + chapter.StartTicks,
            globalTrackStartTicks + chapter.EndTicks,
            chapter.StartTicks,
            chapter.EndTicks,
            IsSynthetic: false));
    }

    private static string BuildSyntheticChapterTitle(TrackInfo track, int outputChapterIndex)
    {
        if (!string.IsNullOrWhiteSpace(track.ChapterTitle))
            return track.ChapterTitle.Trim();

        if (!string.IsNullOrWhiteSpace(track.TagTitle))
            return track.TagTitle.Trim();

        if (!string.IsNullOrWhiteSpace(track.FileName))
            return Path.GetFileNameWithoutExtension(track.FileName).Trim();

        return BuildFallbackChapterTitle(outputChapterIndex);
    }

    private static string BuildFallbackChapterTitle(int outputChapterIndex)
    {
        return ChapterFallbackLabelProvider.GetLabel() + " " +
               outputChapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record NormalizedEmbeddedChapter(
        TrackEmbeddedChapterInfo Source,
        string Title,
        long StartTicks,
        long EndTicks);
}
