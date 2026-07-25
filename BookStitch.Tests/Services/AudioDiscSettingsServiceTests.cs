using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscSettingsServiceTests
{
    [Theory]
    [InlineData(null, AudioDiscWorkingFormat.Flac)]
    [InlineData("", AudioDiscWorkingFormat.Flac)]
    [InlineData("Flac", AudioDiscWorkingFormat.Flac)]
    [InlineData("wma", AudioDiscWorkingFormat.Flac)]
    [InlineData("AAC256", AudioDiscWorkingFormat.Flac)]
    [InlineData("unknown", AudioDiscWorkingFormat.Flac)]
    public void NormalizeWorkingFormat_ReturnsExpectedFormat(
        string? value,
        AudioDiscWorkingFormat expected)
    {
        Assert.Equal(expected, AudioDiscSettingsService.NormalizeWorkingFormat(value));
    }
}
