using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscExportAvailabilityServiceTests
{
    [Fact]
    public void CanStart_ReturnsTrueWhenAllRequirementsAreMet()
    {
        var result = AudioDiscExportAvailabilityService.CanStart(
            isAwaitingRip: true,
            isBusy: false,
            isExporting: false,
            isDiscImporting: false,
            hasActiveManifest: true,
            ffmpegAvailable: true);

        Assert.True(result);
    }

    [Theory]
    [InlineData(false, false, false, false, true, true)]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(true, false, true, false, true, true)]
    [InlineData(true, false, false, true, true, true)]
    [InlineData(true, false, false, false, false, true)]
    [InlineData(true, false, false, false, true, false)]
    public void CanStart_ReturnsFalseWhenAnyRequirementIsMissing(
        bool isAwaitingRip,
        bool isBusy,
        bool isExporting,
        bool isDiscImporting,
        bool hasActiveManifest,
        bool ffmpegAvailable)
    {
        var result = AudioDiscExportAvailabilityService.CanStart(
            isAwaitingRip,
            isBusy,
            isExporting,
            isDiscImporting,
            hasActiveManifest,
            ffmpegAvailable);

        Assert.False(result);
    }
}