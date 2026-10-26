using System.Globalization;

namespace BookStitch.Services;

public static class ToolTipTextService
{
    public static string Get(string key, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return LocalizationText.Get(key, culture);
    }
}
