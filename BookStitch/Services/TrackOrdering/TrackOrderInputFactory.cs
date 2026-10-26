using BookStitch.Models;
using BookStitch.Models.TrackOrdering;

namespace BookStitch.Services.TrackOrdering;

public sealed class TrackOrderInputFactory
{
    private readonly TrackListStateService _trackListStateService;
    private readonly NaturalStringComparer _naturalComparer;

    public TrackOrderInputFactory()
        : this(new TrackListStateService(), new NaturalStringComparer())
    {
    }

    public TrackOrderInputFactory(
        TrackListStateService trackListStateService,
        NaturalStringComparer naturalComparer)
    {
        _trackListStateService = trackListStateService ?? throw new ArgumentNullException(nameof(trackListStateService));
        _naturalComparer = naturalComparer ?? throw new ArgumentNullException(nameof(naturalComparer));
    }

    public IReadOnlyList<TrackOrderInput> Create(
        IReadOnlyList<TrackInfo> tracks,
        IReadOnlyDictionary<TrackInfo, ConfirmedOrderContext>? confirmedContexts = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var fallbackPositions = tracks
            .OrderBy(track => track.RelativeFolder ?? string.Empty, _naturalComparer)
            .ThenBy(track => track.FileName ?? string.Empty, _naturalComparer)
            .ThenBy(track => track.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select((track, index) => new { Track = track, Position = index })
            .ToDictionary(item => item.Track, item => item.Position);

        return tracks
            .Select(track => new TrackOrderInput(
                _trackListStateService.CreateKey(track),
                fallbackPositions[track],
                track.FilePath ?? string.Empty,
                track.FileName ?? string.Empty,
                track.RelativeFolder ?? string.Empty,
                new OriginalOrderMetadata(
                    track.SourceTrackNumber,
                    track.SourceTrackTotal,
                    track.SourceDiscNumber,
                    track.SourceDiscTotal),
                confirmedContexts?.TryGetValue(track, out var confirmed) == true ? confirmed : null))
            .ToList();
    }
}
