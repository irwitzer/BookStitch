using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConceptMetadataFieldDefinition(ProjectMetadataField Field)
{
    public ConceptMetadataFieldDescriptor Descriptor => ConceptMetadataFieldCatalog.Get(Field);
    public bool IsMultiline => Descriptor.IsMultiline;
    public bool IsReadOnly => Descriptor.IsReadOnly;
}

public static class ConceptMetadataGroupCatalog
{
    public static IReadOnlyList<ConceptMetadataFieldDefinition> PublicationAdditional { get; } =
    [
        new(ProjectMetadataField.Source),
        new(ProjectMetadataField.Copyright),
        new(ProjectMetadataField.Website)
    ];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> Language { get; } =
    [
        new(ProjectMetadataField.Languages),
        new(ProjectMetadataField.OriginalLanguages)
    ];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> CatalogData { get; } =
    [
        new(ProjectMetadataField.Keywords),
        new(ProjectMetadataField.ISBN),
        new(ProjectMetadataField.Barcode),
        new(ProjectMetadataField.ASIN),
        new(ProjectMetadataField.MusicBrainzReleaseId)
    ];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> Sorting { get; } =
    [
        new(ProjectMetadataField.SortTitle),
        new(ProjectMetadataField.SortAuthors),
        new(ProjectMetadataField.SortNarrators),
        new(ProjectMetadataField.SortAlbum),
        new(ProjectMetadataField.SortSeries)
    ];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> DescriptionAdditional { get; } = [];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> PassThrough { get; } =
    [
        new(ProjectMetadataField.ContentType),
        new(ProjectMetadataField.TargetLanguages),
        new(ProjectMetadataField.Script),
        new(ProjectMetadataField.PhonogramCopyright),
        new(ProjectMetadataField.Comment),
        new(ProjectMetadataField.CatalogNumbers),
        new(ProjectMetadataField.Categories)
    ];

    public static IReadOnlyList<ConceptMetadataFieldDefinition> Technical { get; } = [];

    public static ConceptMetadataFieldDefinition Description { get; } =
        new(ProjectMetadataField.Description);
}
