using System.Globalization;

namespace BookStitch.Services;

public static class ChapterFallbackLabelProvider
{
    public static string GetLabel(CultureInfo? culture = null)
    {
        var value = LocalizationText.Get("GenericChapterLabel", culture);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException("The localized generic chapter label is missing.");
    }
}
