using BookStitch.Models;

namespace BookStitch.Services;

public sealed class MainWindowSettingsSelectionService
{
    public MainWindowSettingsSelection Create(
        AppSettings settings,
        IReadOnlyCollection<string> exportPresets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(exportPresets);

        var genre = GenreListService.IsSelectableGenre(settings.DefaultGenre)
            ? settings.DefaultGenre
            : GenreListService.GetDefaultGenre(settings.UsePrivateGenreList);

        var outputExtension = settings.DefaultOutputExtension is ".m4a" or ".m4b"
            ? settings.DefaultOutputExtension
            : ".m4a";

        var fileNameTemplate = string.IsNullOrWhiteSpace(settings.DefaultFileNameTemplate)
            ? "{Autor} - {Titel}"
            : settings.DefaultFileNameTemplate;

        var selectedExportPreset = exportPresets.Contains(settings.SelectedExportPreset)
            ? settings.SelectedExportPreset
            : ExportPreset.DefaultDisplayName;

        AppSettingsNormalizationService.Normalize(settings);

        return new MainWindowSettingsSelection(
            genre,
            outputExtension,
            fileNameTemplate,
            selectedExportPreset,
            settings.SelectedParallelJobs);
    }
}

public sealed record MainWindowSettingsSelection(
    string Genre,
    string OutputExtension,
    string FileNameTemplate,
    string SelectedExportPreset,
    string SelectedParallelJobs);