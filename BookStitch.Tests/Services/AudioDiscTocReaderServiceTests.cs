using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscTocReaderServiceTests
{
    [Fact]
    public void ParseTocBuffer_ReturnsFrameAccurateMusicBrainzToc()
    {
        var buffer = CreateTocBuffer(
            firstTrack: 1,
            lastTrack: 3,
            (1, 150),
            (2, 15363),
            (3, 32314),
            (0xAA, 46592));

        var toc = AudioDiscTocReaderService.ParseTocBuffer(buffer, expectedTrackCount: 3);

        Assert.NotNull(toc);
        Assert.Equal(1, toc.FirstTrackNumber);
        Assert.Equal(3, toc.LastTrackNumber);
        Assert.Equal(46592, toc.LeadOutSectorOffset);
        Assert.Equal(new[] { 150, 15363, 32314 }, toc.TrackSectorOffsets);
        Assert.Equal(
            AudioDiscReaderService.CreateMusicBrainzDiscId(1, 3, 46592, new[] { 150, 15363, 32314 }),
            toc.MusicBrainzDiscId);
    }

    [Fact]
    public void ParseTocBuffer_RejectsTrackCountMismatch()
    {
        var buffer = CreateTocBuffer(
            firstTrack: 1,
            lastTrack: 2,
            (1, 150),
            (2, 15363),
            (0xAA, 32314));

        Assert.Null(AudioDiscTocReaderService.ParseTocBuffer(buffer, expectedTrackCount: 3));
    }

    [Fact]
    public void ParseTocBuffer_RejectsMissingLeadOut()
    {
        var buffer = CreateTocBuffer(
            firstTrack: 1,
            lastTrack: 2,
            (1, 150),
            (2, 15363),
            (3, 32314));

        Assert.Null(AudioDiscTocReaderService.ParseTocBuffer(buffer, expectedTrackCount: 2));
    }

    private static byte[] CreateTocBuffer(
        byte firstTrack,
        byte lastTrack,
        params (int TrackNumber, int SectorOffset)[] descriptors)
    {
        var buffer = new byte[4 + (descriptors.Length * 8)];
        var payloadLength = buffer.Length - 2;
        buffer[0] = (byte)(payloadLength >> 8);
        buffer[1] = (byte)payloadLength;
        buffer[2] = firstTrack;
        buffer[3] = lastTrack;

        for (var index = 0; index < descriptors.Length; index++)
        {
            var descriptorOffset = 4 + (index * 8);
            var descriptor = descriptors[index];
            buffer[descriptorOffset + 1] = 0x10;
            buffer[descriptorOffset + 2] = (byte)descriptor.TrackNumber;

            var totalSeconds = descriptor.SectorOffset / 75;
            buffer[descriptorOffset + 5] = (byte)(totalSeconds / 60);
            buffer[descriptorOffset + 6] = (byte)(totalSeconds % 60);
            buffer[descriptorOffset + 7] = (byte)(descriptor.SectorOffset % 75);
        }

        return buffer;
    }
}
