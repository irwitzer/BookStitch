using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class GenreListServiceTests
{
    [Fact]
    public void GetGenres_DefaultPublicList_UsesLibraryTagsSeparatorAndSharedGenresInRequestedOrder()
    {
        var genres = GenreListService.GetGenres(usePrivateGenreList: false);

        Assert.Equal(
        [
            "──── Bibliothek-Tags ────",
            "Audiobook",
            "Hörbuch",
            "Spoken Word",
            "iBook",
            "Hörspiel",
            "Lesung",
            "Vortrag",
            "Podcast",
            "──── Genres ────",
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
        ], genres);
    }

    [Fact]
    public void GetGenres_PrivateList_ReplacesOnlyLibraryTagsAndKeepsSharedGenres()
    {
        var genres = GenreListService.GetGenres(usePrivateGenreList: true);

        Assert.Equal(
        [
            "──── Bibliothek-Tags ────",
            "iBook Hörbuch",
            "iBook Kleinkunst",
            "iBook Wissen",
            "iBook Kinderbücher DE",
            "iBook Kinderbücher CH",
            "──── Genres ────",
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
        ], genres);
    }

    [Fact]
    public void GetDefaultGenre_UsesFirstSelectableLibraryTagAndNeverSeparator()
    {
        Assert.Equal("Audiobook", GenreListService.GetDefaultGenre(usePrivateGenreList: false));
        Assert.Equal("iBook Hörbuch", GenreListService.GetDefaultGenre(usePrivateGenreList: true));
        Assert.False(GenreListService.IsSelectableGenre(GenreListService.LibraryTagsSeparator));
        Assert.False(GenreListService.IsSelectableGenre(GenreListService.GenresSeparator));
    }
}
