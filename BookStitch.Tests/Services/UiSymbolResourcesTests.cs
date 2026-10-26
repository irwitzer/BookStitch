using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class UiSymbolResourcesTests
{
    [Theory]
    [InlineData(nameof(UiSymbolResources.Folder), "\U0001F4C1")]
    [InlineData(nameof(UiSymbolResources.AudioDisc), "\U0001F4BF")]
    [InlineData(nameof(UiSymbolResources.MetadataPanelExpanded), "\uE70E")]
    [InlineData(nameof(UiSymbolResources.MetadataPanelCollapsed), "\uE70D")]
    [InlineData(nameof(UiSymbolResources.Settings), "\uE713")]
    [InlineData(nameof(UiSymbolResources.ProjectTypeAudioDisc), "\u266A")]
    [InlineData(nameof(UiSymbolResources.ProjectTypeMp3Disc), "\u25C9")]
    [InlineData(nameof(UiSymbolResources.ProjectTypeLocalFiles), "\u25A3")]
    [InlineData(nameof(UiSymbolResources.BlockingFileWarning), "\u26D4")]
    [InlineData(nameof(UiSymbolResources.ChapterWarning), "\u26A0\uFE0F")]
    [InlineData(nameof(UiSymbolResources.Checked), "\u2713")]
    [InlineData(nameof(UiSymbolResources.UncheckedBox), "\u25A1")]
    public void SemanticSymbol_UsesExpectedValue(string resourceName, string expected)
    {
        var field = typeof(UiSymbolResources).GetField(resourceName);

        Assert.NotNull(field);
        Assert.Equal(expected, field!.GetRawConstantValue());
    }
}
