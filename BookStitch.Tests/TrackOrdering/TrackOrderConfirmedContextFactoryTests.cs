using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.TrackOrdering;

public sealed class TrackOrderConfirmedContextFactoryTests
{
    [Fact]
    public void CreateAudioDiscContexts_MapsDiscTrackAndGlobalIndexBySemanticRole()
    {
        var tracks = new[]
        {
            AudioTrack(index: 1, disc: 1, track: 1),
            AudioTrack(index: 2, disc: 1, track: 2),
            AudioTrack(index: 3, disc: 2, track: 1),
            AudioTrack(index: 4, disc: 2, track: 2)
        };

        var contexts = TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(tracks);

        AssertContext(contexts[tracks[0]], disc: 1, track: 1, global: 1);
        AssertContext(contexts[tracks[1]], disc: 1, track: 2, global: 2);
        AssertContext(contexts[tracks[2]], disc: 2, track: 1, global: 3);
        AssertContext(contexts[tracks[3]], disc: 2, track: 2, global: 4);
    }

    [Fact]
    public void CreateAudioDiscContexts_SortsByConfirmedGlobalIndex()
    {
        var tracks = new[]
        {
            AudioTrack(index: 3, disc: 2, track: 1),
            AudioTrack(index: 1, disc: 1, track: 1),
            AudioTrack(index: 4, disc: 2, track: 2),
            AudioTrack(index: 2, disc: 1, track: 2)
        };

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.AudioDisc,
            TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(tracks));

        Assert.Equal(
            new[] { 1, 2, 3, 4 },
            tracks
                .OrderBy(track => track.AutomaticOrderPosition)
                .Select(track => track.Index)
                .ToArray());
    }

    [Fact]
    public void CreateMp3DiscContexts_SuppliesProjectWideGlobalPositionForAcquisitionPreviewOrder()
    {
        var tracks = Enumerable.Range(50, 47)
            .Select(number => Mp3Track(disc: 1, fileName: $"{number:000}.mp3"))
            .Concat(Enumerable.Range(1, 49).Select(number => Mp3Track(disc: 2, fileName: $"{number:000}.mp3")))
            .ToArray();

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks));

        Assert.Equal(
            Enumerable.Range(1, 47).Select(number => (int?)number),
            tracks.Where(track => track.DiscNumber == 1).Select(track => track.AutomaticOrderPosition));
        Assert.Equal(
            Enumerable.Range(48, 49).Select(number => (int?)number),
            tracks.Where(track => track.DiscNumber == 2).Select(track => track.AutomaticOrderPosition));
    }

    private static TrackInfo AudioTrack(int index, int disc, int track) =>
        new()
        {
            Index = index,
            DiscNumber = disc,
            TrackNumber = track,
            FileName = $"{index:000}_Track_{track:00}.wav",
            FilePath = $@"C:\project\CD {disc:00}\{index:000}_Track_{track:00}.wav"
        };

    private static TrackInfo Mp3Track(int disc, string fileName) =>
        new()
        {
            DiscNumber = disc,
            FileName = fileName,
            FilePath = $@"C:\project\CD {disc:00}\{fileName}"
        };

    private static void AssertContext(ConfirmedOrderContext context, int disc, int track, int global)
    {
        Assert.Equal(disc, context.DiscNumber);
        Assert.Equal(track, context.TrackNumber);
        Assert.Equal(global, context.GlobalPosition);
    }
}
