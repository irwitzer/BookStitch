using System.Globalization;
using BookStitch.Models;

namespace BookStitch.Services;

// Resource keys identify choices only. Project/export metadata remain user-visible free text.
public static class GenreListService
{
    public const string FallbackGenre = "Audiobook";
    // Historical pseudo-values are rejected, but are no longer shown in any list.
    public const string LibraryTagsSeparator = "──── Bibliothek-Tags ────";
    public const string GenresSeparator = "──── Genres ────";

    public static IReadOnlyList<string> PrivateLibraryTags { get; } = Array.AsReadOnly(new[]
    {
        "iBook Hörbuch", "iBook Kleinkunst", "iBook Wissen",
        "iBook Kinderbücher DE", "iBook Kinderbücher CH"
    });

    public static IReadOnlyList<string> CatalogKeys { get; } = Array.AsReadOnly(new[]
    {
        "Genre.Tag.AudioGuide",
        "Genre.Tag.Documentary",
        "Genre.Tag.Audiobook",
        "Genre.Tag.AudioDrama",
        "Genre.Tag.Interview",
        "Genre.Tag.Reading",
        "Genre.Tag.LiveRecording",
        "Genre.Tag.Podcast",
        "Genre.Tag.Broadcast",
        "Genre.Tag.SpokenWord",
        "Genre.Tag.LanguageCourse",
        "Genre.Tag.Lecture",
        "Genre.Category.Adventure",
        "Genre.Category.MindfulnessMeditation",
        "Genre.Category.Autobiography",
        "Genre.Category.GeneralFiction",
        "Genre.Category.Education",
        "Genre.Category.Biography",
        "Genre.Category.DetectiveFiction",
        "Genre.Category.DystopianFiction",
        "Genre.Category.EpicFantasy",
        "Genre.Category.Erotica",
        "Genre.Category.Fables",
        "Genre.Category.FamilyRelationships",
        "Genre.Category.FamilySaga",
        "Genre.Category.Fantasy",
        "Genre.Category.Finance",
        "Genre.Category.ContemporaryFiction",
        "Genre.Category.History",
        "Genre.Category.BedtimeStories",
        "Genre.Category.HistoricalFiction",
        "Genre.Category.Horror",
        "Genre.Category.HumorSatire",
        "Genre.Category.YoungAdult",
        "Genre.Category.ChildrensLiterature",
        "Genre.Category.Classics",
        "Genre.Category.CookingFood",
        "Genre.Category.CrimeFiction",
        "Genre.Category.ArtCulture",
        "Genre.Category.ShortStories",
        "Genre.Category.SelfHelpPersonalDevelopment",
        "Genre.Category.Romance",
        "Genre.Category.LiteraryFiction",
        "Genre.Category.Poetry",
        "Genre.Category.FairyTales",
        "Genre.Category.MedicineHealth",
        "Genre.Category.Memoirs",
        "Genre.Category.Music",
        "Genre.Category.MythsLegends",
        "Genre.Category.NatureEnvironment",
        "Genre.Category.Philosophy",
        "Genre.Category.PoliticsSociety",
        "Genre.Category.PostApocalypticFiction",
        "Genre.Category.Psychology",
        "Genre.Category.PsychologicalThriller",
        "Genre.Category.AdviceGuides",
        "Genre.Category.Mystery",
        "Genre.Category.Travel",
        "Genre.Category.ReligionSpirituality",
        "Genre.Category.RomanticComedy",
        "Genre.Category.Nonfiction",
        "Genre.Category.GothicFiction",
        "Genre.Category.ScienceFiction",
        "Genre.Category.Espionage",
        "Genre.Category.Sports",
        "Genre.Category.LanguageLearning",
        "Genre.Category.TechnologyComputing",
        "Genre.Category.TheatreDrama",
        "Genre.Category.Thriller",
        "Genre.Category.TrueCrime",
        "Genre.Category.UrbanFantasy",
        "Genre.Category.Western",
        "Genre.Category.BusinessCareer",
        "Genre.Category.Science",
    });

    public static IReadOnlyList<string> PublicLibraryTags => GetGroup(true, CultureInfo.CurrentUICulture);
    public static IReadOnlyList<string> SharedGenres => GetGroup(false, CultureInfo.CurrentUICulture);
    public static IReadOnlyList<string> PublicGenres => GetGenres(false);
    public static IReadOnlyList<string> PrivateGenres => GetGenres(true);

    public static IReadOnlyList<string> GetGenres(AppSettings settings, CultureInfo? culture = null) =>
        GetGenres(settings.UsePrivateGenreList, settings.IncludeGenreLibraryTags,
            settings.IncludeGenreCategories, settings.GenreListAlwaysEnglish, culture);

    public static IReadOnlyList<string> GetGenres(bool usePrivateGenreList,
        bool includeLibraryTags = true, bool includeCategories = true,
        bool alwaysEnglish = false, CultureInfo? culture = null)
    {
        var language = alwaysEnglish ? CultureInfo.GetCultureInfo("en") : culture ?? CultureInfo.CurrentUICulture;
        // Defensive recovery for malformed/older external settings. Private choices do not
        // satisfy the public-group invariant.
        if (!includeLibraryTags && !includeCategories) includeLibraryTags = true;
        return (usePrivateGenreList ? PrivateLibraryTags : [])
            .Concat(includeLibraryTags ? GetGroup(true, language) : [])
            .Concat(includeCategories ? GetGroup(false, language) : [])
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string[] GetGroup(bool libraryTags, CultureInfo culture) =>
        CatalogKeys.Where(key => key.StartsWith(libraryTags ? "Genre.Tag." : "Genre.Category.", StringComparison.Ordinal))
            .Select(key => LocalizationText.Get(key, culture))
            .Where(value => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("Genre.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Create(culture, ignoreCase: true))
            .ThenBy(value => value, StringComparer.Ordinal).ToArray();

    public static string GetDefaultGenre(bool usePrivateGenreList) => FallbackGenre;

    public static bool IsSeparator(string? genre) =>
        string.Equals(genre, LibraryTagsSeparator, StringComparison.Ordinal) ||
        string.Equals(genre, GenresSeparator, StringComparison.Ordinal);

    public static bool IsSelectableGenre(string? genre) =>
        !string.IsNullOrWhiteSpace(genre) && !IsSeparator(genre);
}
