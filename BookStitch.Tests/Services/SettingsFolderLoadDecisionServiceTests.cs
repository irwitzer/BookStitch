using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SettingsFolderLoadDecisionServiceTests
{
    [Theory]
    [InlineData(null, false, "", false)]
    [InlineData("", true, "", false)]
    [InlineData("   ", true, "   ", false)]
    [InlineData(@"D:\Missing", false, @"D:\Missing", false)]
    [InlineData(@"D:\Audio", true, @"D:\Audio", true)]
    public void CreateOutputFolderDecision_RequiresExistingNonEmptyFolder(
        string? value,
        bool directoryExists,
        string expectedValue,
        bool expectedShouldApply)
    {
        var result = SettingsFolderLoadDecisionService.CreateOutputFolderDecision(
            value,
            directoryExists);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedShouldApply, result.ShouldApply);
    }

    [Theory]
    [InlineData(null, "", false)]
    [InlineData("", "", false)]
    [InlineData("   ", "   ", false)]
    [InlineData(@"D:\Audio", @"D:\Audio", true)]
    public void CreateSelectedFolderDecision_RequiresNonEmptyFolder(
        string? value,
        string expectedValue,
        bool expectedShouldApply)
    {
        var result = SettingsFolderLoadDecisionService.CreateSelectedFolderDecision(value);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedShouldApply, result.ShouldApply);
    }
}