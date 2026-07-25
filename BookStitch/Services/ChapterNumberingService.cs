using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ChapterNumberingService
{
    private static readonly Regex LeadingChapterNumberRegex = new(
        @"^\s*\d+\s+(?<title>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string FormatNumber(int index, int chapterCount, bool useLeadingZeros)
    {
        if (index < 1)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (chapterCount < 1)
            throw new ArgumentOutOfRangeException(nameof(chapterCount));

        if (!useLeadingZeros)
            return index.ToString(CultureInfo.InvariantCulture);

        var width = chapterCount.ToString(CultureInfo.InvariantCulture).Length;
        return index.ToString($"D{width}", CultureInfo.InvariantCulture);
    }

    public string BuildTitle(int index, int chapterCount, string title, bool useLeadingZeros)
    {
        var normalizedTitle = (title ?? string.Empty).Trim();
        var number = FormatNumber(index, chapterCount, useLeadingZeros);

        return string.IsNullOrWhiteSpace(normalizedTitle)
            ? number
            : $"{number} {normalizedTitle}";
    }


    public bool TryGetTitleWithoutNumber(string? chapterTitle, out string title)
    {
        title = string.Empty;
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return false;

        var match = LeadingChapterNumberRegex.Match(chapterTitle);
        if (!match.Success)
            return false;

        title = match.Groups["title"].Value.Trim();
        return true;
    }

    public string ReformatLeadingNumber(
        string? chapterTitle,
        int newIndex,
        int chapterCount,
        bool useLeadingZeros)
    {
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return chapterTitle ?? string.Empty;

        var match = LeadingChapterNumberRegex.Match(chapterTitle);
        if (!match.Success)
            return chapterTitle;

        return BuildTitle(
            newIndex,
            chapterCount,
            match.Groups["title"].Value,
            useLeadingZeros);
    }
}
