using BookStitch.Models;

namespace BookStitch.Services;

public sealed class DiscDriveMeasurementStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DiscDriveMeasurement> _latest = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _touchedDriveRoots = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? Changed;

    public bool MarkAndCheckFirstTouch(string? driveRoot)
    {
        var root = DiscDriveConfigurationService.NormalizeRootPath(driveRoot);
        if (string.IsNullOrWhiteSpace(root))
            return false;

        lock (_syncRoot)
            return _touchedDriveRoots.Add(root);
    }

    public void Record(DiscDriveMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        var key = $"{measurement.DriveRoot}\u001F{measurement.Method}";
        lock (_syncRoot)
            _latest[key] = measurement;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DiscDriveMeasurement> Snapshot()
    {
        lock (_syncRoot)
        {
            return _latest.Values
                .OrderBy(item => item.DriveRoot, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Method, StringComparer.Ordinal)
                .ToList();
        }
    }
}
