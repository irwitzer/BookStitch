using BookStitch.Models;
using System.Globalization;

namespace BookStitch.Services;

public enum ConceptFieldLockKind
{
    None,
    LanguagePreference,
    GenrePreference
}

public enum ConceptFieldInteractionMode
{
    Standard,
    PassThrough
}

public sealed record ConceptFieldInfoDescriptor(
    string TitleResourceKey,
    string BodyResourceKey);

public sealed record ConceptMetadataFieldDescriptor(
    string Id,
    ProjectMetadataField? Field,
    string NameResourceKey,
    string TechnicalName,
    ConceptFieldLockKind LockKind = ConceptFieldLockKind.None,
    bool IsMultiline = false,
    bool IsReadOnly = false,
    ConceptFieldInteractionMode InteractionMode = ConceptFieldInteractionMode.Standard,
    int? MaxInputLength = 400,
    bool HideEditorWhenLocked = false,
    bool LockControlsOutputPolicy = true,
    ConceptFieldInfoDescriptor? Info = null)
{
    public IReadOnlyList<string> SearchTerms =>
        CreateSearchTerms(NameResourceKey, TechnicalName);

    internal static IReadOnlyList<string> CreateSearchTerms(string resourceKey, string technicalName) =>
        new[] { LocalizationText.Get(resourceKey), LocalizationText.Get(resourceKey, CultureInfo.GetCultureInfo("en")), technicalName }
            .Distinct(StringComparer.Ordinal).ToArray();

    public string GetDisplayName(bool showTechnicalName) =>
        showTechnicalName ? TechnicalName : LocalizationText.Get(NameResourceKey);
}

public static class ConceptMetadataFieldCatalog
{
    public const string SeriesPartId = "series-part";

    private static readonly ConceptFieldInfoDescriptor PassThroughInfo =
        new("InfoPassThroughTitle", "InfoPassThroughBody");

    private static readonly IReadOnlyDictionary<ProjectMetadataField, ConceptMetadataFieldDescriptor> Fields =
        new Dictionary<ProjectMetadataField, ConceptMetadataFieldDescriptor>
        {
            [ProjectMetadataField.Title] = Field(ProjectMetadataField.Title, "Metadata.Field.Title", "Title"),
            [ProjectMetadataField.Subtitle] = Field(ProjectMetadataField.Subtitle, "Metadata.Field.Subtitle", "Subtitle"),
            [ProjectMetadataField.OriginalTitle] = Field(ProjectMetadataField.OriginalTitle, "Metadata.Field.OriginalTitle", "Original Title"),
            [ProjectMetadataField.Album] = Field(ProjectMetadataField.Album, "Metadata.Field.Album", "Album"),
            [ProjectMetadataField.Description] = Field(ProjectMetadataField.Description, "Metadata.Field.Description", "Description") with { IsMultiline = true, MaxInputLength = null },
            [ProjectMetadataField.Keywords] = Field(ProjectMetadataField.Keywords, "Metadata.Field.Keywords", "Keywords"),
            [ProjectMetadataField.ContentType] = PassThroughField(ProjectMetadataField.ContentType, "Metadata.Field.ContentType", "Content Type"),
            [ProjectMetadataField.Comment] = PassThroughField(ProjectMetadataField.Comment, "Metadata.Field.Comment", "Comment"),
            [ProjectMetadataField.Authors] = Field(ProjectMetadataField.Authors, "Metadata.Field.Authors", "Authors"),
            [ProjectMetadataField.Narrators] = Field(ProjectMetadataField.Narrators, "Metadata.Field.Narrators", "Narrators"),
            [ProjectMetadataField.Translators] = Field(ProjectMetadataField.Translators, "Metadata.Field.Translators", "Translators"),
            [ProjectMetadataField.Composers] = Field(ProjectMetadataField.Composers, "Metadata.Field.Composers", "Composers"),
            [ProjectMetadataField.Series] = Field(ProjectMetadataField.Series, "Metadata.Field.Series", "Series"),
            [ProjectMetadataField.Volume] = Field(ProjectMetadataField.Volume, "Metadata.Field.Volume", "Volume") with
            {
                Info = new ConceptFieldInfoDescriptor("InfoVolumeTitle", "InfoVolumeBody")
            },
            [ProjectMetadataField.Edition] = Field(ProjectMetadataField.Edition, "Metadata.Field.Edition", "Edition"),
            [ProjectMetadataField.AbridgedStatus] = Field(ProjectMetadataField.AbridgedStatus, "Metadata.Field.AbridgedStatus", "Abridged Status") with { MaxInputLength = null },
            [ProjectMetadataField.AudioWorkType] = Field(ProjectMetadataField.AudioWorkType, "Metadata.Field.AudioWorkType", "Audio Work Type") with { MaxInputLength = null },
            [ProjectMetadataField.Languages] = Field(ProjectMetadataField.Languages, "Metadata.Field.Languages", "Languages", ConceptFieldLockKind.LanguagePreference),
            [ProjectMetadataField.OriginalLanguages] = Field(ProjectMetadataField.OriginalLanguages, "Metadata.Field.OriginalLanguages", "Original Languages"),
            [ProjectMetadataField.TargetLanguages] = PassThroughField(ProjectMetadataField.TargetLanguages, "Metadata.Field.TargetLanguages", "Target Languages"),
            [ProjectMetadataField.Script] = PassThroughField(ProjectMetadataField.Script, "Metadata.Field.Script", "Script"),
            [ProjectMetadataField.Publisher] = Field(ProjectMetadataField.Publisher, "Metadata.Field.Publisher", "Publisher"),
            [ProjectMetadataField.ReleaseDate] = Field(ProjectMetadataField.ReleaseDate, "Metadata.Field.ReleaseDate", "Release Date") with { MaxInputLength = null },
            [ProjectMetadataField.Year] = Field(ProjectMetadataField.Year, "Metadata.Field.Year", "Year") with { MaxInputLength = null },
            [ProjectMetadataField.Copyright] = Field(ProjectMetadataField.Copyright, "Metadata.Field.Copyright", "Copyright"),
            [ProjectMetadataField.PhonogramCopyright] = PassThroughField(ProjectMetadataField.PhonogramCopyright, "Metadata.Field.PhonogramCopyright", "Phonogram Copyright"),
            [ProjectMetadataField.License] = Field(ProjectMetadataField.License, "Metadata.Field.License", "License"),
            [ProjectMetadataField.Genres] = Field(ProjectMetadataField.Genres, "Metadata.Field.Genres", "Genres", ConceptFieldLockKind.GenrePreference) with
            {
                HideEditorWhenLocked = true,
                LockControlsOutputPolicy = false
            },
            [ProjectMetadataField.Categories] = PassThroughField(ProjectMetadataField.Categories, "Metadata.Field.Categories", "Categories"),
            [ProjectMetadataField.ISBN] = Field(ProjectMetadataField.ISBN, "Metadata.Field.ISBN", "ISBN"),
            [ProjectMetadataField.Barcode] = Field(ProjectMetadataField.Barcode, "Metadata.Field.Barcode", "Barcode"),
            [ProjectMetadataField.ASIN] = Field(ProjectMetadataField.ASIN, "Metadata.Field.ASIN", "ASIN"),
            [ProjectMetadataField.CatalogNumbers] = PassThroughField(ProjectMetadataField.CatalogNumbers, "Metadata.Field.CatalogNumbers", "Catalog Numbers"),
            [ProjectMetadataField.MusicBrainzReleaseId] = Field(ProjectMetadataField.MusicBrainzReleaseId, "Metadata.Field.MusicBrainzReleaseId", "MusicBrainz Release ID"),
            [ProjectMetadataField.SortTitle] = Field(ProjectMetadataField.SortTitle, "Metadata.Field.SortTitle", "Sort Title"),
            [ProjectMetadataField.SortAuthors] = Field(ProjectMetadataField.SortAuthors, "Metadata.Field.SortAuthors", "Sort Authors"),
            [ProjectMetadataField.SortNarrators] = Field(ProjectMetadataField.SortNarrators, "Metadata.Field.SortNarrators", "Sort Narrators"),
            [ProjectMetadataField.SortAlbum] = Field(ProjectMetadataField.SortAlbum, "Metadata.Field.SortAlbum", "Sort Album"),
            [ProjectMetadataField.SortSeries] = Field(ProjectMetadataField.SortSeries, "Metadata.Field.SortSeries", "Sort Series"),
            [ProjectMetadataField.Website] = Field(ProjectMetadataField.Website, "Metadata.Field.Website", "Website"),
            [ProjectMetadataField.Source] = Field(ProjectMetadataField.Source, "Metadata.Field.Source", "Source"),
            [ProjectMetadataField.OutputTrackNumber] = Field(ProjectMetadataField.OutputTrackNumber, "Metadata.Field.OutputTrackNumber", "Output Track Number") with { MaxInputLength = null },
            [ProjectMetadataField.OutputTrackTotal] = Field(ProjectMetadataField.OutputTrackTotal, "Metadata.Field.OutputTrackTotal", "Output Track Count") with { MaxInputLength = null }
        };

    private static readonly IReadOnlyDictionary<string, ConceptMetadataFieldDescriptor> SpecialFields =
        new Dictionary<string, ConceptMetadataFieldDescriptor>(StringComparer.Ordinal)
        {
            [SeriesPartId] = new(SeriesPartId, null, "Metadata.Field.SeriesPart", "Series Part")
        };

    public static ConceptMetadataFieldDescriptor Get(ProjectMetadataField field) =>
        Fields.TryGetValue(field, out var descriptor)
            ? descriptor
            : Field(field, field.ToString(), field.ToString());

    public static ConceptMetadataFieldDescriptor Get(string id) =>
        SpecialFields.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown Concept metadata field identity.");


    private static ConceptMetadataFieldDescriptor PassThroughField(
        ProjectMetadataField field,
        string localizedName,
        string technicalName) =>
        Field(field, localizedName, technicalName) with
        {
            InteractionMode = ConceptFieldInteractionMode.PassThrough,
            Info = PassThroughInfo
        };

    private static ConceptMetadataFieldDescriptor Field(
        ProjectMetadataField field,
        string localizedName,
        string technicalName) =>
        new(field.ToString(), field, localizedName, technicalName);

    private static ConceptMetadataFieldDescriptor Field(
        ProjectMetadataField field,
        string localizedName,
        string technicalName,
        ConceptFieldLockKind lockKind) =>
        new(field.ToString(), field, localizedName, technicalName, lockKind);
}
