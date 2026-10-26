using BookStitch.Models;

namespace BookStitch.Services;

// One workspace owns this runtime state. It never writes or adds a disc to a manifest.
public sealed class DiscResumePreviewState
{
    public Mp3DiscPendingChapterDisc? Mp3Disc { get; private set; }
    public AudioDiscProjectManifestDisc? AudioDisc { get; private set; }
    public bool ProcessingStarted { get; private set; }
    public bool Mp3DuplicateAccepted { get; private set; }

    public void MarkProcessingStarted() => ProcessingStarted = DiscNumber is not null;

    public int? DiscNumber => Mp3Disc?.DiscNumber ?? AudioDisc?.DiscNumber;

    public void Set(Mp3DiscPendingChapterDisc disc, bool acceptDuplicate = false)
    {
        Clear();
        Mp3Disc = disc;
        Mp3DuplicateAccepted = acceptDuplicate;
    }

    public void Set(AudioDiscProjectManifestDisc disc)
    {
        Clear();
        AudioDisc = disc;
    }

    public bool MatchesMp3(int number, string signature) => Mp3Disc?.DiscNumber == number &&
        string.Equals(Mp3Disc.Signature, signature, StringComparison.OrdinalIgnoreCase);

    public bool CanResumeMp3Import(int number, string signature) => ProcessingStarted && MatchesMp3(number, signature);

    public bool MatchesAudio(int number, string identity) => AudioDisc?.DiscNumber == number &&
        string.Equals(AudioDisc.DiscIdentity, identity, StringComparison.OrdinalIgnoreCase);

    public void Clear()
    {
        Mp3Disc = null;
        AudioDisc = null;
        ProcessingStarted = false;
        Mp3DuplicateAccepted = false;
    }

    // Keep existing objects and their user edits/order; append only newly visible sources.
    public static IReadOnlyList<TrackInfo> AppendAudioTracks(
        IEnumerable<TrackInfo> current, IEnumerable<TrackInfo> preview)
    {
        var tracks = current.ToList();
        var ids = tracks.Select(track => track.SourceId).ToHashSet();
        tracks.AddRange(preview.Where(track => ids.Add(track.SourceId)));
        return tracks;
    }
}
