namespace BookStitch.Models;

public sealed record DiscDriveMeasurement(
    string DriveRoot,
    string DriveReference,
    string Method,
    DateTimeOffset StartedUtc,
    double ElapsedMilliseconds,
    string Result,
    bool Success,
    bool TimedOut,
    bool Cancelled,
    bool? IsReady,
    bool? MediumPresent,
    bool IsFirstTouchInSession);
