namespace BookStitch.Models;

public sealed record EffectiveChapterInfo(
    int OutputChapterIndex,
    TrackInfo SourceTrack,
    TrackEmbeddedChapterInfo? SourceEmbeddedChapter,
    string Title,
    long StartTicks,
    long EndTicks,
    long SourceStartTicks,
    long SourceEndTicks,
    bool IsSynthetic);
