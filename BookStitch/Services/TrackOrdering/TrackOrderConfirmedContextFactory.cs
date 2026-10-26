using BookStitch.Models;
using BookStitch.Models.TrackOrdering;

namespace BookStitch.Services.TrackOrdering;

public static class TrackOrderConfirmedContextFactory
{
    public static IReadOnlyDictionary<TrackInfo, ConfirmedOrderContext> CreateAudioDiscContexts(
        IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return tracks
            .Where(track => track.Index > 0 || track.DiscNumber.HasValue || track.TrackNumber.HasValue)
            .ToDictionary(
                track => track,
                track => new ConfirmedOrderContext(
                    DiscNumber: track.DiscNumber,
                    TrackNumber: track.TrackNumber,
                    GlobalPosition: track.Index > 0 ? track.Index : null));
    }

    public static IReadOnlyDictionary<TrackInfo, ConfirmedOrderContext> CreateMp3DiscContexts(
        IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return tracks
            .Select((track, index) => new { Track = track, GlobalPosition = index + 1 })
            .Where(item => item.Track.DiscNumber.HasValue)
            .ToDictionary(
                item => item.Track,
                item => new ConfirmedOrderContext(
                    DiscNumber: item.Track.DiscNumber,
                    TrackNumber: item.Track.TrackNumber,
                    GlobalPosition: item.GlobalPosition));
    }
}
