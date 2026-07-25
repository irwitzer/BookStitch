using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class AudioProbeInfoTests
{
    [Fact]
    public void HasPlausibleAudioProperties_WithCompleteAudioData_ReturnsTrue()
    {
        var info = new AudioProbeInfo
        {
            Success = true,
            CodecType = "audio",
            CodecName = "flac",
            Channels = 2,
            SampleRateHz = 44100,
            Duration = TimeSpan.FromSeconds(30)
        };

        Assert.True(info.HasPlausibleAudioProperties);
    }

    [Theory]
    [InlineData(0, 44100, 30)]
    [InlineData(2, 0, 30)]
    [InlineData(2, 44100, 0)]
    public void HasPlausibleAudioProperties_WithMissingTechnicalData_ReturnsFalse(
        int channels,
        int sampleRate,
        int durationSeconds)
    {
        var info = new AudioProbeInfo
        {
            Success = true,
            CodecType = "audio",
            CodecName = "flac",
            Channels = channels,
            SampleRateHz = sampleRate,
            Duration = TimeSpan.FromSeconds(durationSeconds)
        };

        Assert.False(info.HasPlausibleAudioProperties);
    }
}
