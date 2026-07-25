using Xunit;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Tests.Services;

public sealed class TrackExclusionTests
{
    [Fact]
    public void Renumber_SkipsExcludedTracksAndKeepsActiveChaptersContinuous()
    {
        var service = new TrackListActionService();
        var tracks = new List<TrackInfo>
        {
            new() { ChapterTitle = "001 Titel" },
            new() { ChapterTitle = "002 Titel", IsExcluded = true },
            new() { ChapterTitle = "003 Titel" }
        };
        service.Renumber(tracks);
        Assert.Equal(1, tracks[0].Index);
        Assert.Equal(0, tracks[1].Index);
        Assert.Equal(2, tracks[2].Index);
        Assert.Equal("2 Titel", tracks[2].ChapterTitle);
    }

    [Fact]
    public void ExcludeAndRestore_PreservesChapterTitle()
    {
        var service = new TrackListActionService();
        var track = new TrackInfo { ChapterTitle = "004 Titel" };
        service.ExcludeSelected([track]);
        Assert.True(track.IsExcluded);
        Assert.Equal("", track.ChapterTitle);
        service.RestoreSelected([track]);
        Assert.False(track.IsExcluded);
        Assert.Equal("004 Titel", track.ChapterTitle);
    }
}
