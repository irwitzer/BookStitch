namespace BookStitch.Services;

public static class ExportPreviewCountPresentationService
{
    public static ExportPreviewCountPresentation Create(
        int trackCount,
        int chapterCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trackCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chapterCount);

        var trackSummary = LocalizationText.Format("ExportPreview.TrackCount", trackCount);
        var chapterSummary = LocalizationText.Format(chapterCount switch
        {
            0 => "ExportPreview.ChapterCount.Zero",
            1 => "ExportPreview.ChapterCount.One",
            _ => "ExportPreview.ChapterCount.Many"
        }, chapterCount);

        return new ExportPreviewCountPresentation(
            trackSummary,
            chapterSummary);
    }
}

public sealed record ExportPreviewCountPresentation(
    string TrackSummary,
    string ChapterSummary);
