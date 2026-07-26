using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MainWindowStructureTests
{
    [Fact]
    public void MainWindow_contains_auto_merge_control_in_export_options_row()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");

        var label = SingleElement(document, "TextBlock", "Text", "Auto-Zusammenfügen:");
        Assert.Equal("AutoMergeLabel_MouseLeftButtonUp", Attr(label, "MouseLeftButtonUp"));

        var checkBox = SingleElementContaining(document, "CheckBox", "IsChecked", "MergeAutomaticallyAfterConversion");
        Assert.Contains("Mode=TwoWay", Attr(checkBox, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", Attr(checkBox, "IsChecked"), StringComparison.Ordinal);

        var labelGrid = NearestAncestor(label, "Grid");
        var checkBoxGrid = NearestAncestor(checkBox, "Grid");
        Assert.Same(labelGrid, checkBoxGrid);
    }

    [Fact]
    public void MainWindow_contains_top_open_output_folder_button_and_no_legacy_inline_button()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");

        var button = SingleElement(document, "Button", "Content", "Ausgabeordner öffnen");
        Assert.Equal("OpenOutputFolder_Click", Attr(button, "Click"));
        Assert.Contains("CanOpenOutputFolder", Attr(button, "IsEnabled"), StringComparison.Ordinal);
        Assert.NotNull(button.Ancestors().FirstOrDefault(e => IsElement(e, "StackPanel") && Attr(e, "Grid.Column") == "5"));

        var legacyButtons = Elements(document, "Button")
            .Where(e => Attr(e, "Content") == "Öffnen" && Attr(e, "Click") == "OpenOutputFolder_Click")
            .ToArray();

        Assert.Empty(legacyButtons);
    }

    [Fact]
    public void MainWindow_contains_core_export_controls()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");

        SingleElement(document, "TextBlock", "Text", "Export-Preset:");
        SingleElement(document, "ComboBox", "x:Name", "ExportPresetComboBox");
        SingleElement(document, "TextBlock", "Text", "Parallel:");
        SingleElement(document, "TextBox", "x:Name", "ParallelJobsTextBox");
        SingleElement(document, "TextBlock", "Text", "Ausgabeordner:");
        SingleElement(document, "Border", "MouseLeftButtonUp", "OutputFolderPath_Click");

        var genreComboBox = SingleElement(document, "ComboBox", "x:Name", "GenreComboBox");
        Assert.Equal("True", Attr(genreComboBox, "IsEditable"));
        Assert.Equal("GenreComboBox_SelectionChanged", Attr(genreComboBox, "SelectionChanged"));
    }

    [Fact]
    public void Disc_project_setup_dialog_mentions_auto_merge_option()
    {
        var document = LoadXaml("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml");
        Assert.Contains("zusammenfüg", document.ToString(SaveOptions.DisableFormatting), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Disc_project_setup_dialog_keeps_editable_genre_dropdown()
    {
        var document = LoadXaml("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml");

        var genreComboBox = SingleElement(document, "ComboBox", "x:Name", "GenreTextBox");
        Assert.Equal("True", Attr(genreComboBox, "IsEditable"));
        Assert.Equal("MetadataComboBox_SelectionChanged", Attr(genreComboBox, "SelectionChanged"));
    }

    [Fact]
    public void Settings_window_mentions_global_auto_merge_option()
    {
        var document = LoadXaml("BookStitch", "Windows", "SettingsWindow.xaml");
        Assert.Contains("zusammenfüg", document.ToString(SaveOptions.DisableFormatting), StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Settings_window_live_updates_main_window_auto_merge_option()
    {
        var mainWindowText = LoadText("BookStitch", "MainWindow.xaml.cs");
        var settingsWindowText = LoadText("BookStitch", "Windows", "SettingsWindow.xaml.cs");

        Assert.Contains("enabled => MergeAutomaticallyAfterConversion = enabled", mainWindowText, StringComparison.Ordinal);
        Assert.Contains("Action<bool> setMergeAutomaticallyAfterConversion", settingsWindowText, StringComparison.Ordinal);
        Assert.Contains("_setMergeAutomaticallyAfterConversion(mergeAutomaticallyCheckBox.IsChecked == true);", settingsWindowText, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_keeps_audio_disc_pause_action_enabled_through_ui_state()
    {
        var text = LoadText("BookStitch", "MainWindow.xaml.cs");

        Assert.Contains("public bool CanStartExport => ExportUiState.CanStartExport ||", text, StringComparison.Ordinal);
        Assert.Contains("_isAudioDiscProjectAwaitingRip &&", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_shutdown_abort_and_close_button_is_danger()
    {
        var text = LoadText("BookStitch", "MainWindow.xaml.cs");

        Assert.Contains("new AppDialogButton(\"Abbrechen und schließen\", AppDialogResult.Yes, IsDanger: true)", text, StringComparison.Ordinal);
    }


    [Fact]
    public void Settings_window_contains_info_tab_with_release_links_and_logo()
    {
        var document = LoadXaml("BookStitch", "Windows", "SettingsWindow.xaml");
        var text = document.ToString(SaveOptions.DisableFormatting);

        SingleElement(document, "TabItem", "Header", "Info");
        SingleElement(document, "Image", "Source", "/Assets/Icons/BookStitchLogo-Round.png");
        Assert.Contains("Entwickelt von ", text, StringComparison.Ordinal);
        Assert.Contains("irwitzer", text, StringComparison.Ordinal);
        Assert.Contains("AccentSoftBrush", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Hinweise\"", text, StringComparison.Ordinal);
        Assert.Contains("GPL-3.0-only", text, StringComparison.Ordinal);
        Assert.Contains("GitHub-Projekt öffnen", text, StringComparison.Ordinal);
        Assert.Contains("Drittanbieterhinweise öffnen", text, StringComparison.Ordinal);
        Assert.Contains("keine aktiven Social-Media-Kanäle", text, StringComparison.Ordinal);

        var settingsWindowText = LoadText("BookStitch", "Windows", "SettingsWindow.xaml.cs");
        Assert.Contains("https://github.com/irwitzer/BookStitch", settingsWindowText, StringComparison.Ordinal);
        Assert.Contains("AssemblyInformationalVersionAttribute", settingsWindowText, StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(params string[] relativePathParts)
    {
        var path = FindRepositoryFile(relativePathParts);
        return XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private static string LoadText(params string[] relativePathParts)
    {
        var path = FindRepositoryFile(relativePathParts);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePathParts));
    }

    private static XElement SingleElement(XDocument document, string elementName, string attributeName, string attributeValue)
    {
        return Elements(document, elementName)
            .Single(e => Attr(e, attributeName) == attributeValue);
    }

    private static XElement SingleElementContaining(XDocument document, string elementName, string attributeName, string expectedPart)
    {
        return Elements(document, elementName)
            .Single(e => Attr(e, attributeName).Contains(expectedPart, StringComparison.Ordinal));
    }

    private static XElement NearestAncestor(XElement element, string elementName)
    {
        return element.Ancestors().First(e => IsElement(e, elementName));
    }

    private static XElement[] Elements(XDocument document, string elementName)
    {
        return document.Descendants().Where(e => IsElement(e, elementName)).ToArray();
    }

    private static bool IsElement(XElement element, string elementName)
    {
        return element.Name.LocalName == elementName;
    }

    private static string Attr(XElement element, string attributeName)
    {
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        if (attributeName.StartsWith("x:", StringComparison.Ordinal))
        {
            return element.Attribute(xamlNamespace + attributeName[2..])?.Value ?? string.Empty;
        }

        var direct = element.Attribute(attributeName)?.Value;
        if (direct is not null)
        {
            return direct;
        }

        return element.Attributes().FirstOrDefault(a => a.Name.LocalName == attributeName)?.Value ?? string.Empty;
    }
}
