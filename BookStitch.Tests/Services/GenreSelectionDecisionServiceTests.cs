using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class GenreSelectionDecisionServiceTests
{
    [Theory]
    [InlineData("", "", false, "", false)]
    [InlineData("", "Hörbuch", false, "Hörbuch", true)]
    [InlineData("Hörbuch", "Hörbuch", false, "Hörbuch", false)]
    [InlineData("Hörbuch", "Roman", false, "Roman", true)]
    [InlineData("Hörbuch", "────────", true, "────────", false)]
    public void Create_ControlsSeparatorAndChangeApplication(
        string currentValue,
        string normalizedValue,
        bool isSeparator,
        string expectedValue,
        bool expectedShouldApply)
    {
        var result = GenreSelectionDecisionService.Create(
            currentValue,
            normalizedValue,
            isSeparator);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedShouldApply, result.ShouldApply);
    }
}