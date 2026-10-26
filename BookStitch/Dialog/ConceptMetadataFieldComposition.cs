using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Dialog;

/// <summary>
/// Reusable composition for the ordinary quick-access metadata fields that may be
/// presented by more than one window while sharing one canonical metadata state.
/// </summary>
public sealed class ConceptMetadataFieldComposition
{
    private readonly MetadataPreferences _preferences;
    private readonly Action<MetadataPreferences> _savePreferences;

    public ConceptMetadataFieldComposition(
        Func<ProjectMetadata> currentMetadata,
        MetadataPreferences preferences,
        MetadataNormalizationService normalization,
        Action<ProjectMetadata> saveMetadata,
        Action<MetadataPreferences> savePreferences,
        Action<ProjectMetadataField>? candidateRefreshRequested = null,
        Action<ProjectMetadataField, string>? candidateValueCommitted = null,
        Action? titleAlbumBindingChanged = null)
    {
        ArgumentNullException.ThrowIfNull(currentMetadata);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(normalization);
        ArgumentNullException.ThrowIfNull(saveMetadata);
        ArgumentNullException.ThrowIfNull(savePreferences);

        _preferences = preferences;
        _savePreferences = savePreferences;

        ConceptMetadataFieldViewModel Create(ProjectMetadataField field) =>
            new(
                currentMetadata,
                preferences,
                normalization,
                field,
                ConceptMetadataFieldCatalog.Get(field),
                saveMetadata,
                savePreferences,
                candidateRefreshRequested: candidateRefreshRequested,
                candidateValueCommitted: candidateValueCommitted);

        Title = Create(ProjectMetadataField.Title);
        Album = Create(ProjectMetadataField.Album);
        Authors = Create(ProjectMetadataField.Authors);
        Narrators = Create(ProjectMetadataField.Narrators);
        Genres = Create(ProjectMetadataField.Genres);

        Fields = [Title, Album, Authors, Narrators, Genres];
        TitleAlbumBinding = new ConceptMetadataFieldBindingState(
            ProjectMetadataFieldBindingCatalog.TitleToAlbum,
            Title,
            Album,
            () => MetadataFieldBindingPreferencesService.GetIsEnabled(
                _preferences,
                ProjectMetadataFieldBindingCatalog.TitleToAlbum),
            value =>
            {
                if (MetadataFieldBindingPreferencesService.SetIsEnabled(
                        _preferences,
                        ProjectMetadataFieldBindingCatalog.TitleToAlbum,
                        value))
                {
                    _savePreferences(_preferences);
                }
            },
            titleAlbumBindingChanged);
        TitleAlbumBinding.SynchronizeActiveBinding();
    }

    public ConceptMetadataFieldViewModel Title { get; }
    public ConceptMetadataFieldViewModel Album { get; }
    public ConceptMetadataFieldViewModel Authors { get; }
    public ConceptMetadataFieldViewModel Narrators { get; }
    public ConceptMetadataFieldViewModel Genres { get; }
    public IReadOnlyList<ConceptMetadataFieldViewModel> Fields { get; }
    public ConceptMetadataFieldBindingState TitleAlbumBinding { get; }

    public void Refresh()
    {
        foreach (var field in Fields)
            field.Refresh();

        TitleAlbumBinding.SynchronizeActiveBinding();
    }
}
