using System.Collections;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed partial class NaturalStringComparer : IComparer<string>, IComparer
{
    public int Compare(string? x, string? y)
    {
        var originalX = x ?? "";
        var originalY = y ?? "";

        x = originalX.Trim();
        y = originalY.Trim();

        var xParts = SplitIntoParts(x);
        var yParts = SplitIntoParts(y);

        var max = Math.Max(xParts.Count, yParts.Count);

        for (var i = 0; i < max; i++)
        {
            if (i >= xParts.Count) return -1;
            if (i >= yParts.Count) return 1;

            var xp = xParts[i];
            var yp = yParts[i];

            var xIsNumber = long.TryParse(xp, out var xNumber);
            var yIsNumber = long.TryParse(yp, out var yNumber);

            int result;

            if (xIsNumber && yIsNumber)
            {
                result = xNumber.CompareTo(yNumber);
            }
            else
            {
                result = string.Compare(xp, yp, StringComparison.CurrentCultureIgnoreCase);
            }

            if (result != 0)
                return result;
        }

        // Stable fallback for values that are naturally equal, e.g. "3", "03" and "003".
        var fallback = string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        if (fallback != 0)
            return fallback;

        return string.Compare(originalX, originalY, StringComparison.Ordinal);
    }

    public int Compare(object? x, object? y)
    {
        return Compare(x?.ToString(), y?.ToString());
    }

    private static List<string> SplitIntoParts(string text)
    {
        return NumberRegex()
            .Split(text)
            .Where(part => part.Length > 0)
            .ToList();
    }

    [GeneratedRegex("(\\d+)")]
    private static partial Regex NumberRegex();
}
