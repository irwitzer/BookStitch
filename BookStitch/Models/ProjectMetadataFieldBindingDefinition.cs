namespace BookStitch.Models;

public sealed class ProjectMetadataFieldBindingDefinition
{
    public ProjectMetadataFieldBindingDefinition(
        string bindingId,
        ProjectMetadataField sourceField,
        ProjectMetadataField targetField,
        bool defaultEnabled)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
            throw new ArgumentException("Binding ID must not be empty.", nameof(bindingId));
        if (sourceField == targetField)
            throw new ArgumentException("Source and target fields must be different.", nameof(targetField));
        if (!ProjectMetadataFields.IsCanonicalField(sourceField))
            throw new ArgumentException("Source field must be canonical.", nameof(sourceField));
        if (!ProjectMetadataFields.IsCanonicalField(targetField))
            throw new ArgumentException("Target field must be canonical.", nameof(targetField));

        BindingId = bindingId.Trim();
        SourceField = sourceField;
        TargetField = targetField;
        DefaultEnabled = defaultEnabled;
    }

    public string BindingId { get; }
    public ProjectMetadataField SourceField { get; }
    public ProjectMetadataField TargetField { get; }
    public bool DefaultEnabled { get; }
}

public static class ProjectMetadataFieldBindingCatalog
{
    public const string NarratorsToComposersId = "narrators-to-composers";
    public const string TitleToAlbumId = "title-to-album";

    public static ProjectMetadataFieldBindingDefinition NarratorsToComposers { get; } = new(
        NarratorsToComposersId,
        ProjectMetadataField.Narrators,
        ProjectMetadataField.Composers,
        defaultEnabled: true);

    public static ProjectMetadataFieldBindingDefinition TitleToAlbum { get; } = new(
        TitleToAlbumId,
        ProjectMetadataField.Title,
        ProjectMetadataField.Album,
        defaultEnabled: true);

    public static IReadOnlyList<ProjectMetadataFieldBindingDefinition> All { get; } =
    [
        NarratorsToComposers,
        TitleToAlbum
    ];
}
