using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainWindowSettingsSelectionServiceTests
{
    private static readonly string[] ExportPresets =
    [
        "AAC Mono 64 kbps",
        "AAC Stereo 128 kbps"
    ];

    [Fact]
    public void Create_UsesValidSettingsValues()
    {
        var settings = new AppSettings
        {
            DefaultGenre = "Fantasy",
            DefaultOutputExtension = ".m4b",
            DefaultFileNameTemplate = "{Titel}",
            SelectedExportPreset = "AAC Mono 64 kbps",
            SelectedParallelJobs = "4"
        };

        var result = new MainWindowSettingsSelectionService().Create(settings, ExportPresets);

        Assert.Equal("Fantasy", result.Genre);
        Assert.Equal(".m4b", result.OutputExtension);
        Assert.Equal("{Titel}", result.FileNameTemplate);
        Assert.Equal("AAC Mono 64 kbps", result.SelectedExportPreset);
        Assert.Equal("4", result.SelectedParallelJobs);
    }

    [Fact]
    public void Create_FallsBackForInvalidSelectableValues()
    {
        var settings = new AppSettings
        {
            DefaultGenre = GenreListService.GenresSeparator,
            DefaultOutputExtension = ".mp3",
            DefaultFileNameTemplate = " ",
            SelectedExportPreset = "Unbekannt"
        };

        var result = new MainWindowSettingsSelectionService().Create(settings, ExportPresets);

        Assert.Equal(GenreListService.GetDefaultGenre(false), result.Genre);
        Assert.Equal(".m4a", result.OutputExtension);
        Assert.Equal("{Autor} - {Titel}", result.FileNameTemplate);
        Assert.Equal("AAC Stereo 128 kbps", result.SelectedExportPreset);
    }

    [Fact]
    public void Create_PreservesCustomGenre()
    {
        var settings = new AppSettings
        {
            DefaultGenre = "Nicht vorhanden"
        };

        var result = new MainWindowSettingsSelectionService().Create(settings, ExportPresets);

        Assert.Equal("Nicht vorhanden", result.Genre);
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        var service = new MainWindowSettingsSelectionService();

        Assert.Throws<ArgumentNullException>(() => service.Create(null!, ExportPresets));
        Assert.Throws<ArgumentNullException>(() => service.Create(new AppSettings(), null!));
    }
}