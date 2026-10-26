using System.IO;

namespace BookStitch.Services;

internal static class OutputFolderBadgeDisplayService
{
    public static string BuildText(
        string? outputFolder,
        double availableWidth,
        Func<string, double> measureText)
    {
        ArgumentNullException.ThrowIfNull(measureText);

        if (availableWidth <= 0 || string.IsNullOrWhiteSpace(outputFolder))
            return "";

        var normalized = outputFolder.Trim()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        var segments = normalized
            .Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (segments.Count > 0 && segments[0].EndsWith(":", StringComparison.Ordinal))
            segments.RemoveAt(0);

        if (segments.Count == 0)
            return EllipsizeLastPathSegment(normalized, availableWidth, measureText);

        var visibleTail = segments[^1];

        if (measureText(visibleTail) > availableWidth)
            return EllipsizeLastPathSegment(visibleTail, availableWidth, measureText);

        for (var index = segments.Count - 2; index >= 0; index--)
        {
            var candidate = segments[index] + Path.DirectorySeparatorChar + visibleTail;
            if (measureText(candidate) > availableWidth)
                break;

            visibleTail = candidate;
        }

        return visibleTail;
    }


    public static double CalculateWidth(
        string? displayText,
        double maximumWidth,
        double horizontalChromeWidth,
        Func<string, double> measureText)
    {
        ArgumentNullException.ThrowIfNull(measureText);

        if (maximumWidth <= 0)
            return 0;

        var chromeWidth = Math.Clamp(horizontalChromeWidth, 0, maximumWidth);
        var textWidth = string.IsNullOrEmpty(displayText)
            ? 0
            : Math.Max(0, measureText(displayText));

        return Math.Min(maximumWidth, textWidth + chromeWidth);
    }

    private static string EllipsizeLastPathSegment(
        string segment,
        double availableWidth,
        Func<string, double> measureText)
    {
        if (measureText(segment) <= availableWidth)
            return segment;

        const string ellipsis = "…";
        if (measureText(ellipsis) > availableWidth)
            return "";

        var low = 0;
        var high = segment.Length;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var candidate = segment[..mid] + ellipsis;

            if (measureText(candidate) <= availableWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return segment[..low] + ellipsis;
    }
}
