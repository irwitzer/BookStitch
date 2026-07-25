using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class GenreListServiceTests
{
    [Fact]
    public void GetGenres_DefaultPublicList_UsesReleaseGenresInRequestedOrder()
    {
        var genres = GenreListService.GetGenres(usePrivateGenreList: false);

        Assert.Equal(
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
        ], genres);
    }

    [Fact]
    public void GetGenres_PrivateList_UsesOnlyIbookGenresInRequestedOrder()
    {
        var genres = GenreListService.GetGenres(usePrivateGenreList: true);

        Assert.Equal(
        [
            "iBook Hörbuch",
            "iBook Kleinkunst",
            "iBook Wissen",
            "iBook Kinderbücher DE",
            "iBook Kinderbücher CH"
        ], genres);
    }
}
