using System.Globalization;

namespace BookStitch.Models;

public sealed record AudioDiscTrackInfo(
    int TrackNumber,
    TimeSpan StartPosition,
    TimeSpan Duration,
    string TrackIdentity,
    int? SectorOffset = null)
{
    public string TrackLabel => $"Track {TrackNumber:00}";

    public string DurationText => Duration.ToString(Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
}

public sealed record AudioDiscToc(
    int FirstTrackNumber,
    int LastTrackNumber,
    int LeadOutSectorOffset,
    IReadOnlyList<int> TrackSectorOffsets,
    string MusicBrainzDiscId)
{
    public string QueryString => string.Join("+",
    [
        FirstTrackNumber.ToString(CultureInfo.InvariantCulture),
        LastTrackNumber.ToString(CultureInfo.InvariantCulture),
        LeadOutSectorOffset.ToString(CultureInfo.InvariantCulture),
        .. TrackSectorOffsets.Select(offset => offset.ToString(CultureInfo.InvariantCulture))
    ]);
}

public sealed record AudioDiscInfo(
    string DriveRoot,
    string DriveLetter,
    IReadOnlyList<AudioDiscTrackInfo> Tracks,
    TimeSpan TotalDuration,
    string DiscIdentity,
    AudioDiscToc? Toc = null)
{
    public int TrackCount => Tracks.Count;

    public bool HasMusicBrainzToc => Toc is not null;

    public string TotalDurationText => TotalDuration.ToString(TotalDuration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
}

public sealed record AudioDiscReadResult(
    bool IsAudioDisc,
    AudioDiscInfo? Disc,
    string ErrorMessage)
{
    public static AudioDiscReadResult Success(AudioDiscInfo disc) => new(true, disc, string.Empty);

    public static AudioDiscReadResult NotAudioDisc(string message = "Keine lesbare Audio-CD erkannt.") => new(false, null, message);
}
