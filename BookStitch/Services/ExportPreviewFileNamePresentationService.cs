namespace BookStitch.Services;

public static class ExportPreviewFileNamePresentationService
{
    public static string Create(
        int trackCount,
        string outputRelativePathPreview) =>
        trackCount == 0
            ? LocalizationText.Get("ExportPreview.Output.None")
            : LocalizationText.Format("ExportPreview.Output.Path", outputRelativePathPreview);
}