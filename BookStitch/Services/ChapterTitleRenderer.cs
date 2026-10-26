using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class ChapterTitleRenderer
{
    private const char FigureSpace = '\u2007';

    private static readonly Regex LeadingChapterNumberRegex = new(
        @"^\s*\d+\s+(?<title>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Render(
        int chapterPosition,
        int activeChapterCount,
        string? existingChapterTitle,
        ChapterNumberingStyle numberingStyle)
    {
        if (chapterPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(chapterPosition));

        if (activeChapterCount < 1)
            throw new ArgumentOutOfRangeException(nameof(activeChapterCount));

        var title = ExtractTitleText(existingChapterTitle, out var hadLeadingNumber);
        if (!hadLeadingNumber && numberingStyle != ChapterNumberingStyle.None)
            return existingChapterTitle ?? string.Empty;

        if (numberingStyle == ChapterNumberingStyle.None)
            return title;

        var number = FormatNumber(chapterPosition, activeChapterCount, numberingStyle);
        return string.IsNullOrWhiteSpace(title)
            ? number.TrimEnd()
            : $"{number} {title}";
    }

    public string RenderContent(
        int chapterPosition,
        int activeChapterCount,
        string? chapterContent,
        ChapterNumberingStyle numberingStyle)
    {
        if (chapterPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(chapterPosition));

        if (activeChapterCount < 1)
            throw new ArgumentOutOfRangeException(nameof(activeChapterCount));

        var title = (chapterContent ?? string.Empty).Trim();
        if (numberingStyle == ChapterNumberingStyle.None)
            return title;

        var number = FormatNumber(chapterPosition, activeChapterCount, numberingStyle);
        return string.IsNullOrWhiteSpace(title)
            ? number.TrimEnd()
            : $"{number} {title}";
    }

    public string FormatNumber(
        int chapterPosition,
        int activeChapterCount,
        ChapterNumberingStyle numberingStyle)
    {
        if (chapterPosition < 1)
            throw new ArgumentOutOfRangeException(nameof(chapterPosition));

        if (activeChapterCount < 1)
            throw new ArgumentOutOfRangeException(nameof(activeChapterCount));

        return numberingStyle switch
        {
            ChapterNumberingStyle.Plain => chapterPosition.ToString(CultureInfo.InvariantCulture),
            ChapterNumberingStyle.ZeroPadded => chapterPosition.ToString(
                $"D{CalculateNumberWidth(activeChapterCount)}",
                CultureInfo.InvariantCulture),
            ChapterNumberingStyle.SpacePadded => chapterPosition
                .ToString(CultureInfo.InvariantCulture)
                .PadRight(CalculateNumberWidth(activeChapterCount), FigureSpace),
            ChapterNumberingStyle.None => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(numberingStyle))
        };
    }

    public bool TryGetTitleWithoutNumber(string? chapterTitle, out string title)
    {
        title = ExtractTitleText(chapterTitle, out var hadLeadingNumber);
        return hadLeadingNumber;
    }

    private static string ExtractTitleText(string? chapterTitle, out bool hadLeadingNumber)
    {
        hadLeadingNumber = false;
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return string.Empty;

        var match = LeadingChapterNumberRegex.Match(chapterTitle);
        if (!match.Success)
            return chapterTitle;

        hadLeadingNumber = true;
        return match.Groups["title"].Value.Trim();
    }

    public static int CalculateNumberWidth(int activeChapterCount)
    {
        return Math.Max(1, activeChapterCount.ToString(CultureInfo.InvariantCulture).Length);
    }
}
