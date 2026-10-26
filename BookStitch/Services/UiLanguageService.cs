using System.Globalization;
using System.Threading;
using BookStitch.Models;

namespace BookStitch.Services;

public static class UiLanguageService
{
    public const string System = UiLanguageIds.System;
    public const string German = UiLanguageIds.German;
    public const string English = UiLanguageIds.English;
    public const string Spanish = UiLanguageIds.Spanish;
    public const string French = UiLanguageIds.French;
    public const string Czech = UiLanguageIds.Czech;

    // Canonical display order, shared by every language selector. System is a separate
    // preference, followed by the five product-priority languages. Remaining endonyms:
    // Latin alphabet (diacritics treated as their base letters), then Cyrillic, Bengali,
    // and Han names in ordinal character order (Japanese, simplified/traditional Chinese).
    // Keep this explicit: OS collation versions and the active UI culture must not reorder it.
    // Register new translations in the appropriate position here; RESX may use a parent culture.
    public static IReadOnlyList<UiLanguageOption> Choices { get; } =
    [
        new(System, "LanguageSystem"),
        new(English, "LanguageEnglish"),
        new(German, "LanguageGerman"),
        new("gsw-CH", "", "Bärndütsch"),
        new(Spanish, "LanguageSpanish"),
        new(French, "LanguageFrench"),
        new(Czech, "LanguageCzech"),
        new("da-DK", "", "Dansk"),
        new("it-IT", "", "Italiano"),
        new("nl-NL", "", "Nederlands"),
        new("nb-NO", "", "Norsk bokmål"),
        new("pl-PL", "", "Polski"),
        new("pt-BR", "", "Português (Brasil)"),
        new("fi-FI", "", "Suomi"),
        new("sv-SE", "", "Svenska"),
        new("vi-VN", "", "Tiếng Việt"),
        new("tr-TR", "", "Türkçe"),
        new("ru-RU", "", "Русский"),
        new("bn-BD", "", "বাংলা (বাংলাদেশ)"),
        new("ja-JP", "", "日本語"),
        new("zh-CN", "", "简体中文"),
        new("zh-HK", "", "繁體中文（香港）")
    ];

    public static string Normalize(string? value) =>
        Choices.FirstOrDefault(choice => string.Equals(choice.Id, value, StringComparison.OrdinalIgnoreCase))?.Id ?? System;

    public static CultureInfo Resolve(string? value, CultureInfo windowsUiCulture) =>
        Normalize(value) == System ? windowsUiCulture : CultureInfo.GetCultureInfo(Normalize(value));

    public static void ApplyAtStartup(string? value, CultureInfo windowsUiCulture)
    {
        var uiCulture = Resolve(value, windowsUiCulture);
        var formatCulture = CultureInfo.GetCultureInfo(German);
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = formatCulture;
        Thread.CurrentThread.CurrentUICulture = uiCulture;
        Thread.CurrentThread.CurrentCulture = formatCulture;
    }
}

public sealed record UiLanguageOption(string Id, string ResourceKey, string? NativeName = null)
{
    public string DisplayName => NativeName ?? LocalizationText.Get(ResourceKey);
}
