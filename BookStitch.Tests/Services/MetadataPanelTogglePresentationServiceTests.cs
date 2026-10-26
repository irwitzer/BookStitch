using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MetadataPanelTogglePresentationServiceTests
{
    [Fact]
    public void Create_ReturnsExpandedPresentation()
    {
        var result = MetadataPanelTogglePresentationService.Create(isExpanded: true);

        Assert.Equal(UiSymbolResources.MetadataPanelExpanded, result.Glyph);
        Assert.True(result.IsExpanded);
    }

    [Fact]
    public void Create_ReturnsCollapsedPresentation()
    {
        var result = MetadataPanelTogglePresentationService.Create(isExpanded: false);

        Assert.Equal(UiSymbolResources.MetadataPanelCollapsed, result.Glyph);
        Assert.False(result.IsExpanded);
    }
}