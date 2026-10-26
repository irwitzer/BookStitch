using System.Globalization;
using System.Text.Json;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class GenreListServiceTests
{
    [Theory]
    [InlineData("de")][InlineData("en")][InlineData("es")][InlineData("bn-BD")]
    [InlineData("cs")][InlineData("da")][InlineData("fi")][InlineData("fr")]
    [InlineData("gsw-CH")][InlineData("it")][InlineData("ja")][InlineData("nb")]
    [InlineData("nl")][InlineData("pl")][InlineData("pt-BR")][InlineData("ru")]
    [InlineData("sv")][InlineData("tr")][InlineData("vi")][InlineData("zh-CN")][InlineData("zh-HK")]
    public void Catalog_has_complete_localized_groups_sorted_in_their_own_language(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var settings = new AppSettings();
        var all = GenreListService.GetGenres(settings, culture);
        Assert.Equal(74, all.Count);
        Assert.Equal(74, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, value => Assert.True(GenreListService.IsSelectableGenre(value)));
        settings.IncludeGenreCategories = false;
        var tags = GenreListService.GetGenres(settings, culture);
        settings.IncludeGenreCategories = true;
        settings.IncludeGenreLibraryTags = false;
        var categories = GenreListService.GetGenres(settings, culture);
        Assert.Equal(12, tags.Count);
        Assert.Equal(62, categories.Count);
        Assert.Equal(tags.Concat(categories), all);
        var comparer = StringComparer.Create(culture, ignoreCase: true);
        Assert.Equal(tags.OrderBy(value => value, comparer).ThenBy(value => value, StringComparer.Ordinal), tags);
        Assert.Equal(categories.OrderBy(value => value, comparer).ThenBy(value => value, StringComparer.Ordinal), categories);
        Assert.All(GenreListService.CatalogKeys, key => Assert.NotEqual(key, LocalizationText.Get(key, culture)));
        Assert.DoesNotContain("iBook", all);
        Assert.DoesNotContain(GenreListService.LibraryTagsSeparator, all);
        Assert.DoesNotContain(GenreListService.GenresSeparator, all);
    }

    [Fact]
    public void Language_switch_and_english_override_change_only_available_choices()
    {
        var settings = new AppSettings { DefaultGenre = "Hörbuch" };
        var german = GenreListService.GetGenres(settings, CultureInfo.GetCultureInfo("de"));
        var spanish = GenreListService.GetGenres(settings, CultureInfo.GetCultureInfo("es"));
        Assert.Contains("Hörbuch", german);
        Assert.Contains("Audiolibro", spanish);
        Assert.DoesNotContain("Hörbuch", spanish);
        settings.GenreListAlwaysEnglish = true;
        var english = GenreListService.GetGenres(settings, CultureInfo.GetCultureInfo("ja"));
        Assert.Contains("Audiobook", english);
        Assert.Equal(GenreListService.GetGenres(false, culture: CultureInfo.GetCultureInfo("en")), english);
        Assert.Equal("Hörbuch", settings.DefaultGenre);
    }

    [Theory]
    [InlineData(true, true)][InlineData(true, false)][InlineData(false, true)][InlineData(false, false)]
    public void Private_tags_prefix_every_group_configuration_without_replacing_public_choices(bool tags, bool categories)
    {
        var normal = GenreListService.GetGenres(false, tags, categories, culture: CultureInfo.GetCultureInfo("es"));
        var withPrivate = GenreListService.GetGenres(true, tags, categories, culture: CultureInfo.GetCultureInfo("es"));
        Assert.Equal(new[] { "iBook Hörbuch", "iBook Kleinkunst", "iBook Wissen", "iBook Kinderbücher DE", "iBook Kinderbücher CH" }, withPrivate.Take(5));
        Assert.Equal(normal, withPrivate.Skip(5));
        Assert.NotEmpty(normal);
    }

    [Fact]
    public void Fallback_is_always_audiobook_and_legacy_or_custom_values_remain_selectable()
    {
        Assert.Equal("Audiobook", GenreListService.GetDefaultGenre(false));
        Assert.Equal("Audiobook", GenreListService.GetDefaultGenre(true));
        foreach (var value in new[] { "Hörbuch", "iBook", "Biografie & Memoiren", "Mein eigenes Genre", "iBook Hörbuch" })
            Assert.True(GenreListService.IsSelectableGenre(value));
        Assert.False(GenreListService.IsSelectableGenre(GenreListService.LibraryTagsSeparator));
        Assert.False(GenreListService.IsSelectableGenre(GenreListService.GenresSeparator));
        Assert.False(GenreListService.IsSelectableGenre(null));
    }

    [Fact]
    public void Settings_are_backward_compatible_and_round_trip_without_losing_existing_genres()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"DefaultGenre\":\"Mein Genre\",\"UsePrivateGenreList\":true}")!;
        Assert.True(legacy.IncludeGenreLibraryTags);
        Assert.True(legacy.IncludeGenreCategories);
        Assert.False(legacy.GenreListAlwaysEnglish);
        legacy.IncludeGenreLibraryTags = false;
        legacy.GenreListAlwaysEnglish = true;
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(legacy))!;
        Assert.False(loaded.IncludeGenreLibraryTags);
        Assert.True(loaded.IncludeGenreCategories);
        Assert.True(loaded.GenreListAlwaysEnglish);
        Assert.True(loaded.UsePrivateGenreList);
        Assert.Equal("Mein Genre", loaded.DefaultGenre);
    }
}
