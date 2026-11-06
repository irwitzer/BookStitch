using BookStitch.Services;
using Xunit;
namespace BookStitch.Tests.Models;
public sealed class AudioProfilePresentationTests
{
    [Theory]
    [InlineData("aac", "LC", "AAC-LC")]
    [InlineData("AAC", "HE-AAC", "HE-AAC v1")]
    [InlineData("aac", "HE-AACv2", "HE-AAC v2")]
    [InlineData("aac", "Main", "Main")]
    [InlineData("mp3", null, "—")]
    [InlineData("vorbis", "unknown", "—")]
    [InlineData("flac", "", "—")]
    [InlineData("other", "existing-profile", "existing-profile")]
    public void Profiles_preserve_probe_information_without_inventing_missing_values(string codec, string? profile, string expected) =>
        Assert.Equal(expected, AudioProfilePresentationService.Format(codec, profile));
}
