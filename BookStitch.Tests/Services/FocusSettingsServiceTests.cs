using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FocusSettingsServiceTests
{
    [Theory]
    [InlineData(null, FocusProfile.Standard)]
    [InlineData("", FocusProfile.Standard)]
    [InlineData("off", FocusProfile.Off)]
    [InlineData("Foreground", FocusProfile.Foreground)]
    [InlineData("unknown", FocusProfile.Standard)]
    public void NormalizeProfile_ReturnsExpectedProfile(string? value, FocusProfile expected)
    {
        Assert.Equal(expected, FocusSettingsService.NormalizeProfile(value));
    }

    [Fact]
    public void Off_DisablesAttentionForEveryEvent()
    {
        Assert.False(FocusSettingsService.GetAttentionPlan(FocusProfile.Off).IsEnabled);
    }

    [Theory]
    [InlineData(FocusProfile.Standard, 5, false)]
    [InlineData(FocusProfile.Foreground, 10, true)]
    public void Standard_ReturnsExpectedAttentionPlan(
        FocusProfile profile,
        int expectedFlashCount,
        bool expectedForeground)
    {
        var plan = FocusSettingsService.GetAttentionPlan(profile);

        Assert.Equal(expectedFlashCount, plan.FlashCount);
        Assert.Equal(expectedForeground, plan.BringToForeground);
        Assert.Equal(expectedForeground, plan.UseTemporaryTopmost);
    }
}
