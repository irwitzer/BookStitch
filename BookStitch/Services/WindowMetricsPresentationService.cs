using System.Globalization;

namespace BookStitch.Services;

public sealed record WindowMetricsCandidate(
    string Name,
    double ActualWidth,
    double ActualHeight,
    double MinWidth,
    double MinHeight,
    double MaxWidth,
    double MaxHeight,
    bool IsMainWindow,
    bool IsDeveloperTools,
    bool IsActive,
    string TechnicalTypeName = "");

public sealed record WindowMetricsPresentation(string MainWindowText, string SecondaryWindowText);

public sealed class WindowMetricsPresentationService
{
    private WindowMetricsCandidate? _lastSecondaryWindow;

    public WindowMetricsPresentation Update(IReadOnlyList<WindowMetricsCandidate> windows)
    {
        windows ??= [];
        var main = windows.FirstOrDefault(window => window.IsMainWindow);
        var activeRelevant = windows.FirstOrDefault(window => window.IsActive && !window.IsMainWindow && !window.IsDeveloperTools);
        if (activeRelevant is not null)
            _lastSecondaryWindow = activeRelevant;
        else if (windows.Any(window => window.IsActive && window.IsMainWindow))
            _lastSecondaryWindow = null;

        return new WindowMetricsPresentation(FormatWindow(main), FormatWindow(_lastSecondaryWindow));
    }

    public static string FormatWindow(WindowMetricsCandidate? window)
    {
        if (window is null)
            return "–";
        var technical = string.IsNullOrWhiteSpace(window.TechnicalTypeName) ? string.Empty : $"Technisch: {window.TechnicalTypeName}\n";
        return $"{window.Name}\n" +
               technical +
               $"Aktuell: {FormatDimension(window.ActualWidth)} × {FormatDimension(window.ActualHeight)} DIP\n" +
               $"Min: {FormatDimension(window.MinWidth)} × {FormatDimension(window.MinHeight)} DIP\n" +
               $"Max: {FormatDimension(window.MaxWidth)} × {FormatDimension(window.MaxHeight)} DIP";
    }

    public static string FormatDimension(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "∞";
        if (double.IsNegativeInfinity(value))
            return "−∞";
        if (double.IsNaN(value))
            return "–";
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
