using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OutputFolderPrimaryActionServiceTests
{
    [Theory]
    [InlineData(null, "Open")]
    [InlineData("", "Open")]
    [InlineData("invalid", "Open")]
    [InlineData("open", "Open")]
    [InlineData("CHANGE", "Change")]
    public void Normalize_ReturnsCanonicalPersistentAction(string? value, string expected)
    {
        Assert.Equal(expected, OutputFolderPrimaryActionService.Normalize(value));
    }

    [Theory]
    [InlineData("Open", true, OutputFolderPrimaryAction.Open)]
    [InlineData("Change", true, OutputFolderPrimaryAction.Change)]
    [InlineData("Open", false, OutputFolderPrimaryAction.Change)]
    [InlineData("Change", false, OutputFolderPrimaryAction.Change)]
    public void Resolve_UsesChangeAsRuntimeFallbackWhenOutputFolderIsUnavailable(
        string preferredAction,
        bool canOpenOutputFolder,
        OutputFolderPrimaryAction expected)
    {
        var result = OutputFolderPrimaryActionService.Resolve(preferredAction, canOpenOutputFolder);

        Assert.Equal(expected, result);
    }
}
