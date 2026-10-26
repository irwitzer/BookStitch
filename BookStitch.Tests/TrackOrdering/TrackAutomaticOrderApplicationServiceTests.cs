using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.TrackOrdering;

public sealed class TrackAutomaticOrderApplicationServiceTests
{
    [Fact]
    public void CalculateAndApply_WritesAutomaticOrderPositionBackToOriginalTrackInfoItems()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                FileName = "02 Chapter.mp3",
                SourceTrackNumber = 2
            },
            new TrackInfo
            {
                FileName = "01 Chapter.mp3",
                SourceTrackNumber = 1
            }
        };

        var result = new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.LocalFolder);

        Assert.Equal(TrackOrderConfidence.VeryHigh, result.OverallConfidence);
        Assert.Equal([2, 1], tracks.Select(track => track.AutomaticOrderPosition));
    }

    [Fact]
    public void Apply_ClearsAutomaticOrderPositionWhenResultDoesNotContainStableTrackKey()
    {
        var track = new TrackInfo
        {
            FilePath = @"C:\Books\Track.mp3",
            FileName = "Track.mp3",
            AutomaticOrderPosition = 5
        };
        var result = new AutomaticTrackOrderResult(
            [
                new AutomaticTrackOrderItemResult(
                    "path:C:\\Other\\Track.mp3",
                    1,
                    TrackOrderConfidence.Low)
            ],
            TrackOrderConfidence.Low,
            []);

        new TrackAutomaticOrderApplicationService().Apply([track], result);

        Assert.Null(track.AutomaticOrderPosition);
    }
}
