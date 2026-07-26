namespace BookStitch.Services;

public static class GenreListService
{
    public const string LibraryTagsSeparator = "──── Bibliothek-Tags ────";
    public const string GenresSeparator = "──── Genres ────";

    public static IReadOnlyList<string> PublicLibraryTags { get; } =
    [
        "Audiobook",
        "Hörbuch",
        "Spoken Word",
        "iBook",
        "Hörspiel",
        "Lesung",
        "Vortrag",
        "Podcast"
    ];

    public static IReadOnlyList<string> PrivateLibraryTags { get; } =
    [
        "iBook Hörbuch",
        "iBook Kleinkunst",
        "iBook Wissen",
        "iBook Kinderbücher DE",
        "iBook Kinderbücher CH"
    ];

    public static IReadOnlyList<string> SharedGenres { get; } =
    [
        "Thriller",
        "Krimi",
        "Fantasy",
        "Science-Fiction",
        "Historischer Roman",
        "Liebesroman",
        "Belletristik",
        "Biografie & Memoiren",
        "Kinderhörbuch",
        "Jugendbuch",
        "Sachbuch",
        "Ratgeber",
        "Geschichte",
        "Humor",
        "Horror",
        "True Crime",
        "Klassiker",
        "Abenteuer",
        "Wirtschaft & Karriere",
        "Religion & Spiritualität",
        "Philosophie",
        "Psychologie",
        "Politik & Gesellschaft",
        "Wissenschaft",
        "Medizin & Gesundheit",
        "Persönlichkeitsentwicklung",
        "Familie & Beziehungen",
        "Reise & Abenteuer",
        "Märchen & Sagen",
        "Lyrik",
        "Drama",
        "Erotik"
    ];

    public static IReadOnlyList<string> PublicGenres { get; } = BuildGenres(PublicLibraryTags);

    public static IReadOnlyList<string> PrivateGenres { get; } = BuildGenres(PrivateLibraryTags);

    public static IReadOnlyList<string> GetGenres(bool usePrivateGenreList) =>
        usePrivateGenreList ? PrivateGenres : PublicGenres;

    public static string GetDefaultGenre(bool usePrivateGenreList) =>
        usePrivateGenreList ? PrivateLibraryTags[0] : PublicLibraryTags[0];

    public static bool IsSeparator(string? genre) =>
        string.Equals(genre, LibraryTagsSeparator, StringComparison.Ordinal) ||
        string.Equals(genre, GenresSeparator, StringComparison.Ordinal);

    public static bool IsSelectableGenre(string? genre) =>
        !string.IsNullOrWhiteSpace(genre) && !IsSeparator(genre);

    private static string[] BuildGenres(IReadOnlyList<string> libraryTags) =>
    [
        LibraryTagsSeparator,
        ..libraryTags,
        GenresSeparator,
        ..SharedGenres
    ];
}
