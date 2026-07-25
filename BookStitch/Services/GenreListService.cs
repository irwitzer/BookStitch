namespace BookStitch.Services;

public static class GenreListService
{
    public static IReadOnlyList<string> PublicGenres { get; } =
    [
        "iBook",
        "Audiobook",
        "Hörbuch",
        "Spoken Word",
        "Podcast",
        "Audio Drama",
        "Radio Play",
        "Fiction",
        "Nonfiction",
        "Education",
        "Lecture",
        "Interview",
        "Comedy",
        "Children's Audiobook"
    ];

    public static IReadOnlyList<string> PrivateGenres { get; } =
    [
        "iBook Hörbuch",
        "iBook Kleinkunst",
        "iBook Wissen",
        "iBook Kinderbücher DE",
        "iBook Kinderbücher CH"
    ];

    public static IReadOnlyList<string> GetGenres(bool usePrivateGenreList) =>
        usePrivateGenreList ? PrivateGenres : PublicGenres;
}
