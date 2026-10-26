namespace BookStitch.Services;


public static class CoverPreviewPresentationService
{
    public static string GetPlaceholderSource(AppearanceMode mode) =>
        AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, mode).RelativePath;

    public static string GetPlaceholderPackUri(AppearanceMode mode) =>
        AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, mode).PackUriString;

    public static string NormalizeSource(string? value, AppearanceMode mode, string? placeholderOverride = null)
    {
        var placeholder = placeholderOverride ?? GetPlaceholderSource(mode);
        return string.IsNullOrWhiteSpace(value) || AppearanceAssetCatalog.IsCoverPlaceholderPath(value) || !IsSafeExistingCover(value)
            ? placeholder
            : value;
    }

    public static string NormalizeSource(
        string? value,
        string defaultSource) =>
        string.IsNullOrWhiteSpace(value) || !IsSafeExistingCover(value)
            ? defaultSource
            : value;

    private static bool IsSafeExistingCover(string value)
    {
        if (!System.IO.File.Exists(value)) return true;
        try { CoverInputSafety.ValidateFile(value); return true; }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Cover preview rejected: {0}", ex.Message);
            return false;
        }
    }



    public static bool IsHintVisible(
        string? processedCoverPath,
        bool canChangeBookMetadata) =>
        string.IsNullOrWhiteSpace(processedCoverPath) &&
        canChangeBookMetadata;

    public static bool CanRemove(
        string? processedCoverPath,
        bool canChangeBookMetadata) =>
        !string.IsNullOrWhiteSpace(processedCoverPath) &&
        canChangeBookMetadata;
}
