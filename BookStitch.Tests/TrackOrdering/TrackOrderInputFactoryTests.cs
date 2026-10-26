using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.TrackOrdering;

public sealed class TrackOrderInputFactoryTests
{
    [Fact]
    public void Create_MapsTrackInfoMetadataAndStableKeysIntoOrderInputs()
    {
        var first = new TrackInfo
        {
            FilePath = @"C:\Books\Disc 02\Track 01.mp3",
            FileName = "Track 01.mp3",
            RelativeFolder = "Disc 02",
            SourceDiscNumber = 2,
            SourceDiscTotal = 3,
            SourceTrackNumber = 1,
            SourceTrackTotal = 9,
            DiscNumber = 20,
            TrackNumber = 10
        };
        var second = new TrackInfo
        {
            FilePath = @"C:\Books\Disc 01\Track 02.mp3",
            FileName = "Track 02.mp3",
            RelativeFolder = "Disc 01",
            SourceDiscNumber = 1,
            SourceDiscTotal = 3,
            SourceTrackNumber = 2,
            SourceTrackTotal = 9,
            DiscNumber = 10,
            TrackNumber = 20
        };

        var inputs = new TrackOrderInputFactory().Create([first, second]);

        Assert.Equal(2, inputs.Count);
        Assert.Equal(new[] { 1, 0 }, inputs.Select(input => input.DeterministicInputPosition));
        Assert.Equal(2, inputs[0].Metadata.DiscNumber);
        Assert.Equal(3, inputs[0].Metadata.DiscTotal);
        Assert.Equal(1, inputs[0].Metadata.TrackNumber);
        Assert.Equal(9, inputs[0].Metadata.TrackTotal);
        Assert.NotEqual(first.DiscNumber, inputs[0].Metadata.DiscNumber);
        Assert.NotEqual(first.TrackNumber, inputs[0].Metadata.TrackNumber);
        Assert.Equal(@"C:\Books\Disc 02\Track 01.mp3", inputs[0].FullPath);
        Assert.Equal("Track 01.mp3", inputs[0].FileName);
        Assert.Equal("Disc 02", inputs[0].RelativeFolder);
        Assert.StartsWith("path:", inputs[0].TrackKey, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(inputs[0].TrackKey, inputs[1].TrackKey);
    }

    [Fact]
    public void Create_PassesConfirmedContextForMatchingTrack()
    {
        var track = new TrackInfo { FileName = "Track.mp3", DiscNumber = 2, TrackNumber = 7 };
        var context = new ConfirmedOrderContext(DiscNumber: 3, TrackNumber: 4, GlobalPosition: 12);

        var input = Assert.Single(new TrackOrderInputFactory().Create(
            [track],
            new Dictionary<TrackInfo, ConfirmedOrderContext> { [track] = context }));

        Assert.Same(context, input.ConfirmedContext);
    }
}
