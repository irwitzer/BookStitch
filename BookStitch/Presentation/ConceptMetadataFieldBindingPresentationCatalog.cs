namespace BookStitch.Presentation;

public sealed record ConceptMetadataFieldBindingPresentationDefinition(
    string BindingId,
    string LabelResourceKey,
    string InfoTitleResourceKey,
    string InfoBodyResourceKey);

public static class ConceptMetadataFieldBindingPresentationCatalog
{
    private static readonly Dictionary<string, ConceptMetadataFieldBindingPresentationDefinition> Definitions =
        new(StringComparer.Ordinal)
        {
            [Models.ProjectMetadataFieldBindingCatalog.NarratorsToComposersId] = new(
                Models.ProjectMetadataFieldBindingCatalog.NarratorsToComposersId,
                "ComposerSpeakerToggleLabel",
                "InfoComposerSpeakerTitle",
                "InfoComposerSpeakerBody"),
            [Models.ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = new(
                Models.ProjectMetadataFieldBindingCatalog.TitleToAlbumId,
                "AlbumTitleToggleLabel",
                "InfoAlbumTitleTitle",
                "InfoAlbumTitleBody")
        };

    public static IReadOnlyCollection<ConceptMetadataFieldBindingPresentationDefinition> All => Definitions.Values;

    public static ConceptMetadataFieldBindingPresentationDefinition Get(string bindingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        if (!Definitions.TryGetValue(bindingId, out var definition))
            throw new KeyNotFoundException($"No metadata field-binding presentation is registered for '{bindingId}'.");
        return definition;
    }
}
