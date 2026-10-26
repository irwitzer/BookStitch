using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SoundSettingsServiceTests
{
    [Theory]
    [InlineData(null, SoundLibrary.Bass)]
    [InlineData("", SoundLibrary.Bass)]
    [InlineData("None", SoundLibrary.None)]
    [InlineData("Bass", SoundLibrary.Bass)]
    [InlineData("hammondorgan", SoundLibrary.HammondOrgan)]
    [InlineData("Warm", SoundLibrary.Warm)]
    [InlineData("retro", SoundLibrary.Retro)]
    [InlineData("Digital", SoundLibrary.Bass)]
    [InlineData("unknown", SoundLibrary.Bass)]
    public void NormalizeLibrary_ReturnsExpectedLibrary(string? value, SoundLibrary expected)
    {
        Assert.Equal(expected, SoundSettingsService.NormalizeLibrary(value));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(65, 65)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void NormalizeVolumePercent_ClampsValue(int value, int expected)
    {
        Assert.Equal(expected, SoundSettingsService.NormalizeVolumePercent(value));
    }
}
