using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DebugSettingsServiceTests
{
    [Theory]
    [InlineData("Status")]
    [InlineData("Appearance")]
    [InlineData("Images")]
    [InlineData("Projects")]
    [InlineData("Tools")]
    [InlineData("Sounds")]
    [InlineData("Chapters")]
    [InlineData("Performance")]
    public void Last_category_round_trips_in_separate_developer_settings(string tab)
    {
        using var folder = new TemporaryFolder();
        var service = new DebugSettingsService(new SettingsService(Path.Combine(folder.Path, "data"), Path.Combine(folder.Path, "projects")));
        Assert.Equal("Status", service.Load().LastTab);
        service.Save(new DebugSettings { LastTab = tab });
        Assert.Equal(tab, service.Load().LastTab);
    }

    [Fact]
    public void SaveAndLoad_UsesSeparateDebugSettingsFileAndPreservesWindowState()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var settingsService = new SettingsService(appData, projectRoot);
        var service = new DebugSettingsService(settingsService);
        var settings = new DebugSettings
        {
            WindowLeft = 141,
            WindowTop = 82,
            WindowWidth = 1234,
            WindowHeight = 777,
            LastTab = "Performance",
            PerformanceDiagnosticsEnabled = true,
            ForceShowFfmpegSetupButton = true,
            ForceShowCircleDriveButton = true,
            UseBoxedDiscWaitDialog = true,
            TrackGridRowHeightLevel = 4,
            MetadataPanelAnimationMillisecondsOverride = 875,
            ProjectSetupPageAnimationMillisecondsOverride = 315,
            RememberDeveloperToolsWindowPlacement = false,
            RememberConceptDialogPlacement = true,
            ConceptDialogPlacement = new DebugWindowPlacement
            {
                Left = 201,
                Top = 102,
                Width = 1180,
                Height = 810
            }
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(Path.Combine(settingsService.SettingsFolder, DebugSettingsService.FileName), service.FilePath);
        Assert.True(File.Exists(service.FilePath));
        Assert.NotEqual(settingsService.SettingsFilePath, service.FilePath);
        Assert.Equal(141d, loaded.WindowLeft);
        Assert.Equal(82d, loaded.WindowTop);
        Assert.Equal(1234d, loaded.WindowWidth);
        Assert.Equal(777d, loaded.WindowHeight);
        Assert.Equal("Performance", loaded.LastTab);
        Assert.True(loaded.PerformanceDiagnosticsEnabled);
        Assert.True(loaded.ForceShowFfmpegSetupButton);
        Assert.True(loaded.ForceShowCircleDriveButton);
        Assert.True(loaded.UseBoxedDiscWaitDialog);
        Assert.Equal(4, loaded.TrackGridRowHeightLevel);
        Assert.Equal(875, loaded.MetadataPanelAnimationMillisecondsOverride);
        Assert.Equal(315, loaded.ProjectSetupPageAnimationMillisecondsOverride);
        Assert.False(loaded.RememberDeveloperToolsWindowPlacement);
        Assert.True(loaded.RememberConceptDialogPlacement);
        Assert.Equal(201d, loaded.ConceptDialogPlacement.Left);
        Assert.Equal(102d, loaded.ConceptDialogPlacement.Top);
        Assert.Equal(1180d, loaded.ConceptDialogPlacement.Width);
        Assert.Equal(810d, loaded.ConceptDialogPlacement.Height);

        var placementService = new DebugWindowPlacementService(loaded, service);
        placementService.SetRememberEnabled(DebugWindowPlacementTarget.MetadataDialog, enabled: false);
        var afterDisable = service.Load();

        Assert.False(afterDisable.RememberConceptDialogPlacement);
        Assert.Null(afterDisable.ConceptDialogPlacement.Left);
        Assert.Null(afterDisable.ConceptDialogPlacement.Top);
        Assert.Null(afterDisable.ConceptDialogPlacement.Width);
        Assert.Null(afterDisable.ConceptDialogPlacement.Height);
    }

    [Fact]
    public void GlobalSchemaUpgrade_AddsOnlyMissingAnimationOverridesToExistingDebugSettings()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var settingsService = new SettingsService(appData, projectRoot);
        var debugService = new DebugSettingsService(settingsService);
        debugService.Save(new DebugSettings
        {
            PerformanceDiagnosticsEnabled = true,
            TrackGridRowHeightLevel = 4,
            MetadataPanelAnimationMillisecondsOverride = 900
        });
        Directory.CreateDirectory(settingsService.SettingsFolder);
        File.WriteAllText(
            settingsService.SettingsFilePath,
            $$"""
            {
              "schemaVersion": 1,
              "WorkingFolder": "{{projectRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "MetadataPanelAnimationMilliseconds": 775,
              "ProjectSetupPageAnimationMilliseconds": 220
            }
            """);

        settingsService.Load();
        var migrated = debugService.Load();

        Assert.True(migrated.PerformanceDiagnosticsEnabled);
        Assert.Equal(4, migrated.TrackGridRowHeightLevel);
        Assert.Equal(900, migrated.MetadataPanelAnimationMillisecondsOverride);
        Assert.Null(migrated.ProjectSetupPageAnimationMillisecondsOverride);

        settingsService.Load();
        var loadedAgain = debugService.Load();
        Assert.Equal(900, loadedAgain.MetadataPanelAnimationMillisecondsOverride);
        Assert.Null(loadedAgain.ProjectSetupPageAnimationMillisecondsOverride);
    }

    [Fact]
    public void Load_MigratesLegacyDebugValuesFromGlobalSettingsOnce()
    {
        using var folder = new TemporaryFolder();
        var appData = Path.Combine(folder.Path, "AppData", "BookStitch");
        var projectRoot = Path.Combine(folder.Path, "Projects");
        var settingsService = new SettingsService(appData, projectRoot);
        Directory.CreateDirectory(settingsService.SettingsFolder);
        File.WriteAllText(
            settingsService.SettingsFilePath,
            """
            {
              "PerformanceDiagnosticsEnabled": true,
              "ForceShowFfmpegSetupButton": true,
              "UseBoxedDiscWaitDialog": true,
              "LastDeveloperDiscTestType": "Mp3Disc",
              "LastDeveloperAudioDiscTestFolder": "  C:\\AudioTest  "
            }
            """);

        var service = new DebugSettingsService(settingsService);
        var loaded = service.Load();

        Assert.True(loaded.PerformanceDiagnosticsEnabled);
        Assert.True(loaded.ForceShowFfmpegSetupButton);
        Assert.False(loaded.ForceShowCircleDriveButton);
        Assert.True(loaded.UseBoxedDiscWaitDialog);
        Assert.True(File.Exists(service.FilePath));
    }

    [Fact]
    public void Normalize_RepairsInvalidGeometryTabAndDiscType()
    {
        var settings = new DebugSettings
        {
            WindowLeft = double.NaN,
            WindowTop = double.PositiveInfinity,
            WindowWidth = 10,
            WindowHeight = 99999,
            LastTab = "Unknown",
            TrackGridRowHeightLevel = 99,
            ConceptDialogPlacement = new DebugWindowPlacement
            {
                Left = double.NaN,
                Top = double.PositiveInfinity,
                Width = -1,
                Height = 7000
            }
        };

        DebugSettingsService.Normalize(settings);

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.Equal(820d, settings.WindowWidth);
        Assert.Equal(3000d, settings.WindowHeight);
        Assert.Equal("Status", settings.LastTab);
        Assert.Equal(TrackGridRowHeightService.MaximumLevel, settings.TrackGridRowHeightLevel);
        Assert.Null(settings.ConceptDialogPlacement.Left);
        Assert.Null(settings.ConceptDialogPlacement.Top);
        Assert.Null(settings.ConceptDialogPlacement.Width);
        Assert.Equal(5000d, settings.ConceptDialogPlacement.Height);
    }

    [Fact]
    public void Normalize_PreservesProjectsTab()
    {
        var settings = new DebugSettings { LastTab = "Projects" };

        DebugSettingsService.Normalize(settings);

        Assert.Equal("Projects", settings.LastTab);
    }

    [Fact]
    public void Normalize_PreservesChaptersTab()
    {
        var settings = new DebugSettings { LastTab = "Chapters" };

        DebugSettingsService.Normalize(settings);

        Assert.Equal("Chapters", settings.LastTab);
        Assert.True(settings.RememberDeveloperToolsWindowPlacement);
        Assert.False(settings.RememberGlobalSettingsWindowPlacement);
        Assert.False(settings.RememberProjectSetupDialogPlacement);
        Assert.False(settings.RememberConceptDialogPlacement);
        Assert.False(settings.RememberResumeProjectDialogPlacement);
    }
}
