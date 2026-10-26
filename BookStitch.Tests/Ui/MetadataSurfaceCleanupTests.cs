using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MetadataSurfaceCleanupTests
{
    [Fact]
    public void MetadataDialog_is_the_only_large_metadata_surface_and_chapter_resources_are_retained()
    {
        var repositoryRoot = FindRepositoryRoot();
        var removedDialogName = "ProjectMetadata" + "EditorDialog";
        var removedViewModelName = "ProjectMetadata" + "EditorViewModels.cs";

        Assert.False(File.Exists(Path.Combine(repositoryRoot, "BookStitch", "Dialog", removedDialogName + ".xaml")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "BookStitch", "Dialog", removedDialogName + ".xaml.cs")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "BookStitch", "Dialog", removedViewModelName)));

        var mainWindow = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "MainWindow.xaml"));
        var concept = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "Dialog", "MetadataDialog.xaml"));
        var setup = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"));
        var removedHandler = "OpenMetadata" + "Editor_Click";

        Assert.DoesNotContain(removedHandler, mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain(removedHandler, setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Großen Metadaten-Dialog öffnen", concept, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=UiMetadaten}\"", mainWindow, StringComparison.Ordinal);

        var oldRememberProperty = "RememberLast" + "MetadataGroup";
        var oldGroupProperty = "LastMetadata" + "GroupId";
        Assert.Null(typeof(MetadataPreferences).GetProperty(oldRememberProperty));
        Assert.Null(typeof(MetadataPreferences).GetProperty(oldGroupProperty));

        var removedPlacementTarget = "Metadata" + "Editor";
        Assert.DoesNotContain(removedPlacementTarget, Enum.GetNames<DebugWindowPlacementTarget>());
        Assert.Contains(nameof(DebugWindowPlacementTarget.MetadataDialog), Enum.GetNames<DebugWindowPlacementTarget>());

        var chapterSymbols = Path.Combine(repositoryRoot, "BookStitch", "Themes", "ChapterSymbols.xaml");
        Assert.True(File.Exists(chapterSymbols));
        Assert.Contains("ChapterSymbolGeometry", File.ReadAllText(chapterSymbols), StringComparison.Ordinal);
        Assert.Contains("/Themes/ChapterSymbols.xaml", File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "App.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_metadata_details_panel_contains_only_the_compact_chapter_editor_not_legacy_metadata_editors()
    {
        var document = BookStitch.Tests.TestHelpers.RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var details = document.Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "MetadataDetailsBorder"));

        var comboNames = details.Descendants()
            .Where(element => element.Name.LocalName == "ComboBox")
            .Select(element => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name")?.Value)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        Assert.Equal(
            [
                "MainChapterModeComboBox",
                "MainChapterNumberingStyleComboBox",
                "MainChapterSlot1ComboBox",
                "MainChapterSeparator1ComboBox",
                "MainChapterSlot2ComboBox",
                "MainChapterSeparator2ComboBox",
                "MainChapterSlot3ComboBox"
            ],
            comboNames);
        Assert.DoesNotContain(details.Descendants(), element => element.Name.LocalName == "TextBox");
        Assert.DoesNotContain(details.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value is "AlbumTextBox" or "GenreComboBox"));
        Assert.DoesNotContain(details.Descendants(), element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value.Contains("Preview", StringComparison.Ordinal)));
    }

    [Fact]
    public void Removed_metadata_and_debug_preferences_are_tolerated_as_unknown_json_properties()
    {
        var legacyRemember = "RememberLast" + "MetadataGroup";
        var legacyGroup = "LastMetadata" + "GroupId";
        var legacyPlacementRemember = "RememberMetadata" + "EditorDialogPlacement";
        var legacyPlacement = "Metadata" + "Editor" + "DialogPlacement";

        var metadataJson = $$"""
            {
              "{{legacyRemember}}": true,
              "{{legacyGroup}}": "Publication",
              "RememberLastConceptGroup": true,
              "LastConceptGroupKey": "publication"
            }
            """;
        var metadataPreferences = JsonSerializer.Deserialize<MetadataPreferences>(metadataJson);
        Assert.NotNull(metadataPreferences);
        MetadataPreferencesService.Normalize(metadataPreferences!);
        Assert.True(metadataPreferences!.RememberLastConceptGroup);
        Assert.Equal("publication", metadataPreferences.LastConceptGroupKey);

        var debugJson = $$"""
            {
              "{{legacyPlacementRemember}}": true,
              "{{legacyPlacement}}": { "Left": 12, "Top": 34, "Width": 900, "Height": 700 },
              "RememberConceptDialogPlacement": true,
              "ConceptDialogPlacement": { "Left": 20, "Top": 30, "Width": 960, "Height": 720 }
            }
            """;
        var debugSettings = JsonSerializer.Deserialize<DebugSettings>(debugJson);
        Assert.NotNull(debugSettings);
        DebugSettingsService.Normalize(debugSettings!);
        Assert.True(debugSettings!.RememberConceptDialogPlacement);
        Assert.Equal(20d, debugSettings.ConceptDialogPlacement.Left);
        Assert.Equal(30d, debugSettings.ConceptDialogPlacement.Top);
    }

    [Fact]
    public void Compatibility_description_setting_is_moved_from_global_settings_to_metadata_dialog()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settingsRoot = Path.Combine(repositoryRoot, "BookStitch");
        var settingsFiles = new[]
            {
                Path.Combine(settingsRoot, "Windows", "SettingsWindow.xaml"),
                Path.Combine(settingsRoot, "Windows", "SettingsWindow.xaml.cs")
            }
            .Concat(Directory.EnumerateFiles(Path.Combine(settingsRoot, "Controls"), "Settings*Control.xaml*"));
        var settingsSurface = string.Join(Environment.NewLine, settingsFiles.Select(File.ReadAllText));
        var conceptXaml = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "Dialog", "MetadataDialog.xaml"));
        var conceptCode = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "Dialog", "MetadataDialog.xaml.cs"));
        var compatibilityControlXaml = File.ReadAllText(Path.Combine(repositoryRoot, "BookStitch", "Controls", "ConceptCompatibilityFieldControl.xaml"));

        Assert.DoesNotContain("CompatibilityDescriptionCheckBox", settingsSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("CompatibilityDescriptionCheckBox_Changed", settingsSurface, StringComparison.Ordinal);
        Assert.Contains("ConceptCompatibilityFieldControl", conceptXaml, StringComparison.Ordinal);
        Assert.Contains("IsCompatibilityDescriptionEnabled", compatibilityControlXaml, StringComparison.Ordinal);
        Assert.Contains("IsCompatibilityDescriptionEnabled", conceptCode, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BookStitch.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root was not found.");
    }
}
