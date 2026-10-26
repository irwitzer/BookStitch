using BookStitch.Models;
using BookStitch.Presentation;
using Xunit;

namespace BookStitch.Tests.Presentation;

public sealed class ConceptMetadataFieldBindingPresentationCatalogTests
{
    [Theory]
    [InlineData(
        ProjectMetadataFieldBindingCatalog.NarratorsToComposersId,
        "ComposerSpeakerToggleLabel",
        "InfoComposerSpeakerTitle",
        "InfoComposerSpeakerBody")]
    [InlineData(
        ProjectMetadataFieldBindingCatalog.TitleToAlbumId,
        "AlbumTitleToggleLabel",
        "InfoAlbumTitleTitle",
        "InfoAlbumTitleBody")]
    public void BindingDefinitionsExposeTheirSemanticResources(
        string bindingId,
        string labelResourceKey,
        string infoTitleResourceKey,
        string infoBodyResourceKey)
    {
        var definition = ConceptMetadataFieldBindingPresentationCatalog.Get(bindingId);

        Assert.Equal(bindingId, definition.BindingId);
        Assert.Equal(labelResourceKey, definition.LabelResourceKey);
        Assert.Equal(infoTitleResourceKey, definition.InfoTitleResourceKey);
        Assert.Equal(infoBodyResourceKey, definition.InfoBodyResourceKey);
    }
}
