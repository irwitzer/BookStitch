using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscRipServiceTests
{
    [Theory]
    [InlineData(150, 0)]
    [InlineData(12_965, 12_815)]
    [InlineData(282_250, 282_100)]
    [InlineData(293_126, 292_976)]
    public void ConvertTocSectorToReadLba_RemovesCdLeadIn(int tocSector, int expectedLba)
    {
        Assert.Equal(expectedLba, AudioDiscRipService.ConvertTocSectorToReadLba(tocSector));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(149)]
    public void ConvertTocSectorToReadLba_RejectsOffsetsBeforeFirstAudioSector(int tocSector)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioDiscRipService.ConvertTocSectorToReadLba(tocSector));
    }
}
