using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Save_WritesGlobalSettingsAndLocationAtomically()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Music", "BookStitchProjects");
        var service = new SettingsService(appData, projectRoot);
        var settings = new AppSettings
        {
            WorkingFolder = projectRoot,
            DefaultGenre = "Testgenre"
        };

        service.Save(settings);

        Assert.True(File.Exists(Path.Combine(projectRoot, "software-settings", "global-settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings-location.json")));
        Assert.Empty(Directory.EnumerateFiles(projectRoot, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(appData, "*.tmp", SearchOption.AllDirectories));

        var loaded = service.Load();
        Assert.Equal("Testgenre", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
    }

    [Fact]
    public void Load_MigratesLegacyAppDataSettingsAndKeepsBackup()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "CustomProjects");
        Directory.CreateDirectory(appData);

        var legacySettings = new AppSettings
        {
            WorkingFolder = projectRoot,
            DefaultGenre = "Legacy-Genre"
        };
        File.WriteAllText(
            Path.Combine(appData, "settings.json"),
            JsonSerializer.Serialize(legacySettings));

        var service = new SettingsService(appData, Path.Combine(folder.Path, "DefaultProjects"));
        var loaded = service.Load();

        Assert.Equal("Legacy-Genre", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.True(File.Exists(Path.Combine(projectRoot, "software-settings", "global-settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings-location.json")));
        Assert.False(File.Exists(Path.Combine(appData, "settings.json")));
        Assert.True(File.Exists(Path.Combine(appData, "settings.migrated-backup.json")));
    }

    [Fact]
    public void Load_UsesLocationFileToFindMovedProjectRoot()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var firstRoot = Path.Combine(folder.Path, "FirstRoot");
        var movedRoot = Path.Combine(folder.Path, "MovedRoot");
        var service = new SettingsService(appData, firstRoot);

        service.Save(new AppSettings
        {
            WorkingFolder = movedRoot,
            DefaultGenre = "Verschoben"
        });

        var reloadedService = new SettingsService(appData, firstRoot);
        var loaded = reloadedService.Load();

        Assert.Equal(Path.GetFullPath(movedRoot), reloadedService.ProjectRootFolder);
        Assert.Equal("Verschoben", loaded.DefaultGenre);
    }

    [Fact]
    public void Load_CorruptGlobalSettingsArchivesFileAndReturnsDefaults()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var service = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(service.SettingsFolder);
        File.WriteAllText(service.SettingsFilePath, "{ not valid json");

        var loaded = service.Load();

        Assert.Equal("Audiobook", loaded.DefaultGenre);
        Assert.Equal(Path.GetFullPath(projectRoot), loaded.WorkingFolder);
        Assert.False(File.Exists(service.SettingsFilePath));
        Assert.Single(Directory.EnumerateFiles(service.SettingsFolder, "global-settings.corrupt-*.json"));
    }
}
