using Xunit;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Tests.Services;

public sealed class TrackExclusionTests
{
    [Fact]
    public void ChapterModeEngine_SkipsExcludedTracksAndKeepsActiveChaptersContinuous()
    {
        var tracks = new List<TrackInfo>
        {
            new() { SourceId = Guid.NewGuid(), ChapterTitle = "001 Titel", DurationTicks = TimeSpan.FromSeconds(1).Ticks },
            new() { SourceId = Guid.NewGuid(), ChapterTitle = "002 Titel", IsExcluded = true, DurationTicks = TimeSpan.FromSeconds(1).Ticks },
            new() { SourceId = Guid.NewGuid(), ChapterTitle = "003 Titel", DurationTicks = TimeSpan.FromSeconds(1).Ticks }
        };
        var result = new ChapterModeEngineService().Apply(new ChapterAutoEngineRequest(
            tracks,
            ProjectManifestTypes.FolderProject,
            "exclude-test",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(1, "", MetadataOrigin.None, "", MetadataOrigin.None),
            new ChapterSettings { Mode = ChapterSettingsService.ModeAuto, NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            LegacyUseLeadingZeros: false));

        Assert.Equal(1, tracks[0].Index);
        Assert.Equal(0, tracks[1].Index);
        Assert.Equal(2, tracks[2].Index);
        Assert.Equal("2 Kapitel", tracks[2].ChapterTitle);
        Assert.Equal(["1 Kapitel", "2 Kapitel"], result.RenderedTimeline.Select(chapter => chapter.Title));
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
