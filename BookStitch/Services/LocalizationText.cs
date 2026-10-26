using System.Globalization;
using System.Resources;
using System.Collections;

namespace BookStitch.Services;

public static class LocalizationText
{
    private static readonly ResourceManager Resources = new(
        "BookStitch.Resources.LocalizationResources", typeof(LocalizationText).Assembly);

    public static string Get(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static IEnumerable<string> GetKeys() =>
        Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key);
}
