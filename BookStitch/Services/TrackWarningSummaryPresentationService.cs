namespace BookStitch.Services;

public static class TrackWarningSummaryPresentationService
{
    public static TrackWarningSummaryPresentation Create(
        int fileWarningCount,
        int chapterWarningCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fileWarningCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chapterWarningCount);

        return new TrackWarningSummaryPresentation(
            fileWarningCount,
            chapterWarningCount,
            CreateSummary(
                fileWarningCount,
                "MainWindow.Warning.File.One",
                "MainWindow.Warning.File.Many"),
            CreateSummary(
                chapterWarningCount,
                "MainWindow.Warning.Chapter.One",
                "MainWindow.Warning.Chapter.Many"));
    }

    private static string CreateSummary(
        int count,
        string singularKey,
        string pluralKey) =>
        count == 0
            ? string.Empty
            : LocalizationText.Format(count == 1 ? singularKey : pluralKey, count);
}

public sealed record TrackWarningSummaryPresentation(
    int FileWarningCount,
    int ChapterWarningCount,
    string FileWarningSummary,
    string ChapterWarningSummary)
{
    public bool HasFileWarnings => FileWarningCount > 0;
    public bool HasChapterWarnings => ChapterWarningCount > 0;
    public bool ShowSeparator => HasFileWarnings && HasChapterWarnings;
    public bool IsVisible => HasFileWarnings || HasChapterWarnings;
}
