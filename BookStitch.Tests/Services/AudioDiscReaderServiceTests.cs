using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscReaderServiceTests
{
    [Fact]
    public void CreateDiscIdentity_IsStableForEquivalentTrackOrder()
    {
        var tracks = new[]
        {
            (Number: 2, StartMilliseconds: 180000L, DurationMilliseconds: 240000L),
            (Number: 1, StartMilliseconds: 0L, DurationMilliseconds: 180000L)
        };

        var identity = AudioDiscReaderService.CreateDiscIdentity(tracks);
        var reorderedIdentity = AudioDiscReaderService.CreateDiscIdentity(tracks.Reverse());

        Assert.Equal(identity, reorderedIdentity);
        Assert.Equal(64, identity.Length);
    }

    [Fact]
    public void CreateDiscIdentity_ChangesWhenTrackLayoutChanges()
    {
        var original = AudioDiscReaderService.CreateDiscIdentity(
        [
            (1, 0L, 180000L),
            (2, 180000L, 240000L)
        ]);
        var changed = AudioDiscReaderService.CreateDiscIdentity(
        [
            (1, 0L, 180000L),
            (2, 180000L, 241000L)
        ]);

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void CreateTrackIdentity_IsUniquePerTrack()
    {
        const string discIdentity = "disc-identity";

        var first = AudioDiscReaderService.CreateTrackIdentity(discIdentity, 1, 0, 180000);
        var second = AudioDiscReaderService.CreateTrackIdentity(discIdentity, 2, 180000, 240000);

        Assert.NotEqual(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(64, second.Length);
    }
    [Fact]
    public void CreateMusicBrainzDiscId_MatchesDocumentedReferenceDisc()
    {
        var offsets = new[] { 150, 15363, 32314, 46592, 63414, 80489 };

        var discId = AudioDiscReaderService.CreateMusicBrainzDiscId(1, 6, 95462, offsets);

        Assert.Equal("49HHV7Eb8UKF3aQiNmu1GR8vKTY-", discId);
    }

}
