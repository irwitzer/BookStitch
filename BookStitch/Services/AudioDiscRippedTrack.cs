namespace BookStitch.Services;

public sealed record AudioDiscRippedTrack(
    int DiscNumber,
    int GlobalIndex,
    int TrackNumber,
    Guid SourceId,
    string FilePath,
    TimeSpan Duration);
