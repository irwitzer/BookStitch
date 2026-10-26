using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterSourcePolicyTests
{
    private readonly ChapterSourcePolicy _policy = new();

    [Fact]
    public void Default_UsesExplicitV4PriorityIndependentOfEnumValues()
    {
        var ordered = new[]
        {
            ChapterCandidateSource.EmbeddedChapterTitle,
            ChapterCandidateSource.TrackTitle,
            ChapterCandidateSource.Filename,
            ChapterCandidateSource.CanonicalTitle,
            ChapterCandidateSource.CanonicalAlbum
        };

        Assert.Equal(ordered, ordered.OrderBy(_policy.GetTieBreakPriority));
        Assert.True((int)ChapterCandidateSource.CanonicalTitle > (int)ChapterCandidateSource.CanonicalAlbum);
        Assert.True(
            _policy.GetTieBreakPriority(ChapterCandidateSource.CanonicalTitle) <
            _policy.GetTieBreakPriority(ChapterCandidateSource.CanonicalAlbum));
    }

    [Fact]
    public void AuthorArtistContext_IsNotSelectable()
    {
        Assert.False(_policy.IsSelectable(ChapterCandidateSource.AuthorArtistContext));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _policy.GetTieBreakPriority(ChapterCandidateSource.AuthorArtistContext));
    }
}
