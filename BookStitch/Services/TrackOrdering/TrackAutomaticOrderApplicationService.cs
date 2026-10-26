using BookStitch.Models;
using BookStitch.Models.TrackOrdering;

namespace BookStitch.Services.TrackOrdering;

public sealed class TrackAutomaticOrderApplicationService
{
    private readonly TrackOrderInputFactory _inputFactory;
    private readonly AutomaticTrackOrderService _orderService;

    public TrackAutomaticOrderApplicationService()
        : this(new TrackOrderInputFactory(), new AutomaticTrackOrderService())
    {
    }

    public TrackAutomaticOrderApplicationService(
        TrackOrderInputFactory inputFactory,
        AutomaticTrackOrderService orderService)
    {
        _inputFactory = inputFactory ?? throw new ArgumentNullException(nameof(inputFactory));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    public AutomaticTrackOrderResult CalculateAndApply(
        IReadOnlyList<TrackInfo> tracks,
        TrackOrderProjectType projectType,
        IReadOnlyDictionary<TrackInfo, ConfirmedOrderContext>? confirmedContexts = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var request = new AutomaticTrackOrderRequest(
            projectType,
            _inputFactory.Create(tracks, confirmedContexts));
        var result = _orderService.Analyze(request);
        Apply(tracks, result);
        return result;
    }

    public void Apply(IReadOnlyList<TrackInfo> tracks, AutomaticTrackOrderResult result)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(result);

        var byKey = result.Tracks.ToDictionary(item => item.TrackKey, StringComparer.OrdinalIgnoreCase);
        var inputs = _inputFactory.Create(tracks);
        foreach (var pair in tracks.Zip(inputs))
        {
            pair.First.AutomaticOrderPosition = byKey.TryGetValue(pair.Second.TrackKey, out var item)
                ? item.AutomaticOrderPosition
                : null;
        }
    }
}
