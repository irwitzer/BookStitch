using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectMetadataOutputProjectionOptions(
    string CoverPath,
    bool CompatibilityDescriptionEnabled,
    string OutputPresentationCulture,
    TimeSpan? Duration = null,
    int? ChapterCount = null);

public sealed class ProjectMetadataOutputProjectionService
{
    public FinalAudioTagData Create(
        ProjectMetadata metadata,
        ProjectMetadataOutputProjectionOptions options,
        IReadOnlyDictionary<ProjectMetadataField, OutputFieldPolicy> policies,
        IReadOnlyDictionary<string, bool> bindingSnapshot,
        IEnumerable<ProjectMetadataFieldBindingDefinition>? bindingDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(bindingSnapshot);

        var disabled = ProjectMetadataFields.OutputFields
            .Where(field => policies.TryGetValue(field, out var policy) && policy == OutputFieldPolicy.Disabled)
            .ToHashSet();

        var bindingProjection = new ProjectMetadataFieldBindingProjectionService();
        var bindings = (bindingDefinitions ?? ProjectMetadataFieldBindingCatalog.All).ToArray();
        IReadOnlyList<string> TextValues(ProjectMetadataField field) =>
            bindingProjection.ResolveTargetTextValues(metadata, field, bindingSnapshot, bindings);
        string String(ProjectMetadataField field) => TextValues(field).FirstOrDefault() ?? string.Empty;
        IReadOnlyList<string> List(ProjectMetadataField field) => TextValues(field);

        var authors = List(ProjectMetadataField.Authors);
        var narrators = List(ProjectMetadataField.Narrators);
        var genres = List(ProjectMetadataField.Genres);

        var coverPath = string.IsNullOrWhiteSpace(options.CoverPath)
            ? metadata.Cover.ProcessedPath
            : options.CoverPath;

        return new FinalAudioTagData
        {
            ProjectMetadataRevision = metadata.Revision,
            Title = String(ProjectMetadataField.Title),
            Subtitle = String(ProjectMetadataField.Subtitle),
            OriginalTitle = String(ProjectMetadataField.OriginalTitle),
            Album = String(ProjectMetadataField.Album),
            Author = authors.FirstOrDefault() ?? "",
            Authors = authors,
            Narrator = narrators.FirstOrDefault() ?? "",
            Narrators = narrators,
            Translators = List(ProjectMetadataField.Translators),
            Composers = List(ProjectMetadataField.Composers),
            Genre = genres.FirstOrDefault() ?? "",
            Genres = genres,
            Description = String(ProjectMetadataField.Description),
            Comment = String(ProjectMetadataField.Comment),
            Keywords = List(ProjectMetadataField.Keywords),
            ContentType = String(ProjectMetadataField.ContentType),
            Series = Series(metadata, ProjectMetadataField.Series),
            Volume = String(ProjectMetadataField.Volume),
            Edition = String(ProjectMetadataField.Edition),
            AbridgedStatus = metadata.GetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus).Value,
            AudioWorkType = metadata.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value,
            Languages = List(ProjectMetadataField.Languages),
            OriginalLanguages = List(ProjectMetadataField.OriginalLanguages),
            TargetLanguages = List(ProjectMetadataField.TargetLanguages),
            Script = String(ProjectMetadataField.Script),
            Publisher = String(ProjectMetadataField.Publisher),
            ReleaseDate = metadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Value,
            Year = metadata.GetValue<int?>(ProjectMetadataField.Year).Value,
            Copyright = String(ProjectMetadataField.Copyright),
            PhonogramCopyright = String(ProjectMetadataField.PhonogramCopyright),
            License = String(ProjectMetadataField.License),
            Categories = List(ProjectMetadataField.Categories),
            ISBN = String(ProjectMetadataField.ISBN),
            Barcode = String(ProjectMetadataField.Barcode),
            ASIN = String(ProjectMetadataField.ASIN),
            CatalogNumbers = List(ProjectMetadataField.CatalogNumbers),
            MusicBrainzReleaseId = String(ProjectMetadataField.MusicBrainzReleaseId),
            SortTitle = String(ProjectMetadataField.SortTitle),
            SortAuthors = List(ProjectMetadataField.SortAuthors),
            SortNarrators = List(ProjectMetadataField.SortNarrators),
            SortAlbum = String(ProjectMetadataField.SortAlbum),
            SortSeries = String(ProjectMetadataField.SortSeries),
            Website = String(ProjectMetadataField.Website),
            Source = String(ProjectMetadataField.Source),
            OutputTrackNumber = metadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value,
            OutputTrackTotal = metadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value,
            Duration = options.Duration,
            ChapterCount = options.ChapterCount,
            CoverPath = coverPath,
            CompatibilityDescriptionEnabled = options.CompatibilityDescriptionEnabled,
            OutputPresentationCulture = options.OutputPresentationCulture,
            DisabledFields = disabled
        };
    }

    private static IReadOnlyList<SeriesEntry> Series(ProjectMetadata metadata, ProjectMetadataField field) =>
        metadata.GetValue<IReadOnlyList<SeriesEntry>>(field).Value?
            .Select(item => new SeriesEntry(item.Name.Trim(), item.Part.Trim()))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Part))
            .ToArray() ?? [];
}
