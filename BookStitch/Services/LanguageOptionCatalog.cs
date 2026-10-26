using System.Globalization;

namespace BookStitch.Services;

public sealed record LanguageOption(string Value, string DisplayText, string SearchText);

public static class LanguageOptionCatalog
{
    private static readonly string[] CuratedTags =
    [
        "de", "de-DE", "de-AT", "de-CH",
        "en", "en-GB", "en-US", "en-CA", "en-AU",
        "es", "es-ES", "es-MX", "es-419",
        "fr", "fr-FR", "fr-CA", "fr-CH",
        "it",
        "pt", "pt-PT", "pt-BR",
        "zh", "zh-Hans", "zh-Hant",
        "nl", "pl", "cs", "sk", "hu", "ro",
        "sv", "da", "nb", "fi", "is",
        "ru", "uk", "tr", "el", "ar", "he",
        "hi", "ja", "ko"
    ];

    private static readonly Lazy<IReadOnlyList<LanguageOption>> Options = new(BuildOptions);

    public static IReadOnlyList<LanguageOption> All => Options.Value;

    private static IReadOnlyList<LanguageOption> BuildOptions()
    {
        var result = new List<LanguageOption>(CuratedTags.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in CuratedTags)
        {
            if (!MetadataPreferencesService.TryCanonicalizeLanguage(tag, out var canonical) ||
                !seen.Add(canonical))
            {
                continue;
            }

            string displayName;
            string englishName;
            try
            {
                var culture = CultureInfo.GetCultureInfo(canonical);
                displayName = culture.DisplayName;
                englishName = culture.EnglishName;
            }
            catch (CultureNotFoundException)
            {
                displayName = canonical;
                englishName = canonical;
            }

            result.Add(new LanguageOption(
                canonical,
                $"{displayName} — {canonical}",
                $"{displayName} {englishName} {canonical}"));
        }

        return result;
    }
}
