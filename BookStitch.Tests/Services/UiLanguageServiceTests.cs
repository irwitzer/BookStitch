using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class UiLanguageServiceTests
{
    [Fact]
    public void NewLanguageCanUseItsNativeNameAndAParentSatelliteWithNeutralFallback()
    {
        var option = new UiLanguageOption("ja-JP", "", "日本語");
        Assert.Equal("日本語", option.DisplayName);
        var resources = new System.Resources.ResourceManager(
            "BookStitch.Tests.Fixtures.Localization.LanguageFixture", typeof(UiLanguageServiceTests).Assembly);
        var culture = CultureInfo.GetCultureInfo(option.Id);
        Assert.Equal("こんにちは", resources.GetString("Greeting", culture));
        Assert.Equal("Fallback only", resources.GetString("NeutralOnly", culture));
        Assert.Equal("Fallback", resources.GetString("Greeting", CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Null(resources.GetString("Unknown", culture));
    }

    [Fact]
    public void AllTwentyOneLanguagesAreRegisteredAndNeutralGermanResourcesRemainAvailable()
    {
        string[] expected = ["de-DE", "en-US", "es-ES", "fr-FR", "cs-CZ", "ja-JP", "pt-BR", "ru-RU", "sv-SE",
            "zh-CN", "da-DK", "it-IT", "nb-NO", "nl-NL", "pl-PL", "tr-TR", "vi-VN", "zh-HK", "bn-BD", "fi-FI", "gsw-CH"];
        Assert.Equal(expected.Order(), UiLanguageService.Choices.Where(c => c.Id != UiLanguageIds.System).Select(c => c.Id).Order());
        Assert.Equal(UiLanguageService.Choices.Count, UiLanguageService.Choices.Select(c => c.Id).Distinct().Count());
        foreach (var id in expected)
        {
            Assert.Equal(id, UiLanguageService.Normalize(id.ToUpperInvariant()));
            Assert.NotEqual("GenericChapterLabel", LocalizationText.Get("GenericChapterLabel", CultureInfo.GetCultureInfo(id)));
        }
        Assert.Contains("GenericChapterLabel", LocalizationText.GetKeys());
        Assert.Equal("Chapter", LocalizationText.Get("GenericChapterLabel", CultureInfo.InvariantCulture));
        Assert.Equal("Chapter", LocalizationText.Get("GenericChapterLabel", CultureInfo.GetCultureInfo("is-IS")));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("zh-CN")]
    public void LanguageOrderAndEndonymsAreStableAcrossUiCultures(string uiCulture)
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(uiCulture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            var choices = UiLanguageService.Choices;
            Assert.Equal(UiLanguageIds.System, choices[0].Id);
            string[] ids = ["en-US", "de-DE", "gsw-CH", "es-ES", "fr-FR", "cs-CZ", "da-DK", "it-IT",
                "nl-NL", "nb-NO", "pl-PL", "pt-BR", "fi-FI", "sv-SE", "vi-VN", "tr-TR", "ru-RU",
                "bn-BD", "ja-JP", "zh-CN", "zh-HK"];
            string[] names = ["English", "Deutsch", "Bärndütsch", "Español", "Français", "Čeština", "Dansk",
                "Italiano", "Nederlands", "Norsk bokmål", "Polski", "Português (Brasil)", "Suomi", "Svenska",
                "Tiếng Việt", "Türkçe", "Русский", "বাংলা (বাংলাদেশ)", "日本語", "简体中文", "繁體中文（香港）"];
            Assert.Equal(ids, choices.Skip(1).Select(c => c.Id));
            Assert.Equal(names, choices.Skip(1).Select(c => c.DisplayName));
            foreach (var id in ids)
            {
                Assert.Equal(id, UiLanguageService.Normalize(id.ToUpperInvariant()));
                Assert.Equal(id, UiLanguageService.Resolve(id, CultureInfo.GetCultureInfo("de-DE")).Name);
                var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings { UiLanguage = id });
                Assert.Equal(id, System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!.UiLanguage);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.CurrentCulture = previousFormat;
        }
    }

    [Fact]
    public void MissingAndUnknownChoicesUseSystem()
    {
        Assert.Equal(UiLanguageService.System, new AppSettings().UiLanguage);
        Assert.Equal(UiLanguageService.System, UiLanguageService.Normalize(null));
        Assert.Equal(UiLanguageService.System, UiLanguageService.Normalize("unknown"));
        Assert.Equal(UiLanguageService.German, UiLanguageService.Normalize("DE-de"));
    }

    [Fact]
    public void StartupCultureKeepsGermanFormatsIndependentOfUiChoice()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            UiLanguageService.ApplyAtStartup(UiLanguageService.System, CultureInfo.GetCultureInfo("ja-JP"));
            Assert.Equal("ja-JP", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);

            UiLanguageService.ApplyAtStartup(UiLanguageService.German, CultureInfo.GetCultureInfo("ja-JP"));
            Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ExistingVersionTwoSettingsAcquireSystemWithoutLosingPreferences()
    {
        var settings = new AppSettings { DefaultGenre = "Krimi", UiLanguage = "bad" };
        using var document = System.Text.Json.JsonDocument.Parse("{}");

        Assert.True(AppSettingsMigrationService.MigrateToCurrent(
            settings, 2, AppSettingsLegacyData.Read(document.RootElement)));
        Assert.Equal(UiLanguageService.System, settings.UiLanguage);
        Assert.Equal("Krimi", settings.DefaultGenre);
    }
}
