using BookStitch.Models;

namespace BookStitch.Services;

public static class ChapterEffectiveCountService
{
    public static int EstimateEffectiveActiveChapterCount(
        IEnumerable<TrackInfo> tracks,
        int totalDiscs)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var activeTracks = tracks
            .Where(track => !track.IsExcluded)
            .ToList();
        var actualActiveCount = activeTracks.Count;
        if (actualActiveCount == 0)
            return 0;

        var importedEffectiveChapterCount = CountEffectiveChapters(activeTracks);
        if (totalDiscs <= 1)
            return importedEffectiveChapterCount;

        var importedDiscCounts = activeTracks
            .Where(track => track.DiscNumber is > 0)
            .GroupBy(track => track.DiscNumber!.Value)
            .Select(group => CountEffectiveChapters(group.ToList()))
            .Where(count => count > 0)
            .ToList();

        if (importedDiscCounts.Count == 0)
            return importedEffectiveChapterCount;

        var averageChaptersPerDisc = importedDiscCounts.Average();
        var estimatedFinalChapters = (int)Math.Ceiling(averageChaptersPerDisc * totalDiscs);
        return Math.Max(importedEffectiveChapterCount, estimatedFinalChapters);
    }

    private static int CountEffectiveChapters(IReadOnlyList<TrackInfo> tracks)
    {
        return new ChapterTimelineService().BuildTimeline(tracks).Count;
    }
}
