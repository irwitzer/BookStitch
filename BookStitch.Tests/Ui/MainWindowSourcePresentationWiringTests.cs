using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MainWindowSourcePresentationWiringTests
{
    private static readonly string MainWindowSource =
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Theory]
    [InlineData("Click", "SelectFolder_Click", "FolderDiscActionVisualState")]
    [InlineData("Click", "SelectDiscSource_Click", "FolderDiscActionVisualState")]
    [InlineData("x:Name", "ProjectsButton", "ProjectsActionVisualState")]
    [InlineData("Click", "OpenMetadataDialog_Click", "MetadataActionVisualState")]
    public void Main_actions_share_the_compact_primary_family_and_bind_the_central_visual_state(
        string attribute,
        string value,
        string stateProperty)
    {
        var button = Element("Button", attribute, value);

        Assert.Equal("{StaticResource PrimaryCompactButtonStyle}", Attribute(button, "Style"));
        Assert.Equal($"{{Binding {stateProperty}}}", Attribute(button, "PrimaryButtonVisualStateBehavior.State"));
    }

    [Fact]
    public void Disc_read_source_uses_the_shared_header_and_open_path_with_operation_and_workspace_guards()
    {
        var publish = SourceContract.ExtractMember(MainWindowSource, "private void PublishDiscReadSource(");
        SourceContract.AssertInOrder(publish, "Dispatcher.Invoke",
            "_discImportCancellation?.Token != operationToken", "TrackPathService.PathEquals",
            "LoadedProjectState.IsAudioDisc || LoadedProjectState.IsMp3Disc",
            "SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(sourceRoot), sourceRoot)");
        Assert.DoesNotContain("operationToken.IsCancellationRequested", publish);
        var selection = SourceContract.ExtractMember(MainWindowSource, "private string ShowDiscSourceSelectionDialog(");
        SourceContract.AssertInOrder(selection, "if (updateWorkspace &&", "if (!LoadedProjectState.IsLocal)",
            "SetSelectedSourceDisplayOverride(CreateDiscDriveDisplayName(dialog.SelectedPath), dialog.SelectedPath)");
        var audio = SourceContract.ExtractMember(MainWindowSource, "private async Task RunPreparedAudioDiscRipAsync(");
        Assert.Contains("sourceReadStarted: source => PublishDiscReadSource(source, manifest.ProjectFolder, token)", audio);
        Assert.DoesNotContain("SetAudioDiscSourceDisplayOverride(manifest)", audio);
        var mp3 = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
        Assert.Contains("sourceReadStarted: source => PublishDiscReadSource(source, projectFolder, token)", mp3);
        var audioProducer = RepositorySource.Read(["BookStitch", "Services", "AudioDiscRipService.cs"]);
        var rip = SourceContract.ExtractMember(audioProducer, "private static async Task RipTrackAsync(");
        SourceContract.AssertInOrder(rip, "await sectorReadRetryService.ReadAsync", "sourceReadStarted?.Invoke()", "sectorsRead += count");
        foreach (var method in new[] { "CheckMp3DriveRoundForWaitDialogAsync", "CheckAudioDriveRoundForWaitDialogAsync" })
        {
            var probe = SourceContract.ExtractMember(MainWindowSource, method == "CheckMp3DriveRoundForWaitDialogAsync"
                ? "private async Task<Mp3DriveRoundWaitResult> " + method : "private async Task<AudioDriveRoundWaitResult> " + method);
            Assert.DoesNotContain("PublishDiscReadSource", probe);
            Assert.DoesNotContain("SetSelectedSourceDisplayOverride", probe);
        }
    }

    [Fact]
    public void Projects_action_keeps_its_resume_availability_binding()
    {
        var button = Element("Button", "x:Name", "ProjectsButton");
        Assert.Equal("{Binding CanRefreshResumeProjects}", Attribute(button, "IsEnabled"));
    }

    [Fact]
    public void Selected_source_action_binds_to_resolved_path_availability()
    {
        var source = Element("Button", "x:Name", "SelectedSourceTextButton");

        Assert.Equal("{Binding CanOpenSelectedFolder}", Attribute(source, "IsEnabled"));
    }

    [Fact]
    public void Environment_brand_artwork_keeps_the_central_accessible_environment_name()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var brand = document.Descendants().Single(element =>
            element.Name.LocalName == "Image" &&
            Attribute(element, "Source") == "{DynamicResource HeaderBrandImageSource}");
        Assert.Equal("{Binding EnvironmentBrandText}", Attribute(brand, "AutomationProperties.Name"));
        Assert.Equal("False", Attribute(brand, "IsHitTestVisible"));
        Assert.DoesNotContain("EnvironmentMarker", RepositorySource.Read(["BookStitch", "MainWindow.xaml"]), StringComparison.Ordinal);
        Assert.DoesNotContain("EnvironmentMarker", MainWindowSource, StringComparison.Ordinal);
        Assert.Contains(
            "_services.Foundation.EnvironmentContext.BrandText",
            SourceContract.ExtractMember(MainWindowSource, "public string EnvironmentBrandText"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_quick_menu_uses_central_restart_and_temporary_preview_owner()
    {
        var menu = SourceContract.ExtractMember(MainWindowSource, "private ContextMenu CreateDeveloperQuickMenu");
        Assert.Contains("Developer Tools öffnen", menu, StringComparison.Ordinal);
        Assert.Contains("Neustarten", menu, StringComparison.Ordinal);
        Assert.Contains("RequestApplicationRestart", menu, StringComparison.Ordinal);
        Assert.Contains("CreateBackgroundQuickMenu()", menu, StringComparison.Ordinal);
        Assert.Contains("CreateTextColorQuickMenu()", menu, StringComparison.Ordinal);
        Assert.Contains("CreateDeveloperAccessQuickMenu()", menu, StringComparison.Ordinal);
        var accessMenu = SourceContract.ExtractMember(MainWindowSource, "private MenuItem CreateDeveloperAccessQuickMenu");
        Assert.Contains("access.VisibilityChoice = choice", accessMenu);
        foreach (var key in new[] { "DeveloperAccess.Show", "DeveloperAccess.Hide", "DeveloperAccess.Conceal" })
            Assert.Contains(key, accessMenu);
        Assert.DoesNotContain("Audio-CD-Kurztest starten", menu, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeveloperAudioDiscTestAsync", MainWindowSource, StringComparison.Ordinal);

        var background = SourceContract.ExtractMember(MainWindowSource, "private MenuItem CreateBackgroundQuickMenu");
        Assert.Contains("AppearanceSettingsService.BackgroundPresets", background, StringComparison.Ordinal);
        Assert.Contains("preset.DisplayName", background, StringComparison.Ordinal);
        Assert.Contains("SetBackgroundPreset(presetName)", background, StringComparison.Ordinal);

        var textColor = SourceContract.ExtractMember(MainWindowSource, "private MenuItem CreateTextColorQuickMenu");
        Assert.Contains("AppearanceSettingsService.TextColorPresets", textColor, StringComparison.Ordinal);
        Assert.Contains("preset.DisplayName", textColor, StringComparison.Ordinal);
        Assert.Contains("preset.IsCompatibleWith(appearanceMode)", textColor, StringComparison.Ordinal);
        Assert.Contains("SetTextColorPreset(presetName)", textColor, StringComparison.Ordinal);

        Assert.Contains("private void SetBackgroundPreset(string preset) => DeveloperToolsCommands.AppearancePreview.SetBackground(preset);", MainWindowSource, StringComparison.Ordinal);
        Assert.Contains("private void SetTextColorPreset(string preset) => DeveloperToolsCommands.AppearancePreview.SetTextColor(preset);", MainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistNormalizeAndApplyAppearanceSettings", MainWindowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_quick_menu_requires_enabled_access_and_logo_has_no_developer_action()
    {
        Assert.DoesNotContain("MainWindow_MouseRightButtonUp", MainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IsBookStitchLogoSource", MainWindowSource, StringComparison.Ordinal);
        var open = SourceContract.ExtractMember(MainWindowSource, "internal void OpenDeveloperQuickMenu");

        Assert.Contains("DeveloperToolsCommands.AccessState.IsEnabled", open, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowVisibilityRequested", MainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportWindowVisibility", MainWindowSource, StringComparison.Ordinal);
        Assert.Contains("CreateDeveloperQuickMenu()", open, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBookStitchLogoContextMenu", MainWindowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_a_new_source_resets_pending_disc_mode_state_but_reopening_setup_does_not()
    {
        var reset = SourceContract.ExtractMember(MainWindowSource, "private void ResetProjectSourceModeStateForNewSource()");
        var selectFolder = SourceContract.ExtractMember(MainWindowSource, "private async void SelectFolder_Click");
        var startDisc = SourceContract.ExtractMember(MainWindowSource, "private async Task StartMp3DiscProjectAsync");
        var reopen = SourceContract.ExtractMember(MainWindowSource, "private async Task ShowCurrentProjectSetupDialogAsync()");

        foreach (var requiredReset in new[]
                 {
                     "ClearPendingMp3DiscPreparation();",
                     "_transientMp3DiscChapterPreview = null;",
                     "_activeMp3DiscManifest = null;",
                     "_pendingAudioDisc = null;",
                     "_activeAudioDiscManifest = null;",
                     "_isAudioDiscProjectAwaitingRip = false;",
                     "ApplyLoadedProjectState(LoadedProjectStateSnapshot.None);"
                 })
        {
            Assert.Contains(requiredReset, reset, StringComparison.Ordinal);
        }

        SourceContract.AssertInOrder(selectFolder, "ResetProjectSourceModeStateForNewSource();", "ResetMetadataForNewFolderProject();", "Tracks.Clear();");
        SourceContract.AssertInOrder(startDisc, "SelectedFolder = sourceFolder;", "ResetProjectSourceModeStateForNewSource();", "ResetMetadataForNewDiscProject();");
        Assert.DoesNotContain("ResetProjectSourceModeStateForNewSource", reopen, StringComparison.Ordinal);
        Assert.Contains("ResolveCurrentProjectSetupSourceKind()", reopen, StringComparison.Ordinal);
    }

    [Fact]
    public void Disc_count_preview_updates_immediately_but_expensive_indexes_are_deferred_and_flushed_before_use()
    {
        var factory = SourceContract.ExtractMember(MainWindowSource, "private DiscProjectSetupDialog CreateProjectSetupDialog");
        var queue = SourceContract.ExtractMember(MainWindowSource, "private void QueueProjectSetupDiscCountPreviewRefresh()");
        var flush = SourceContract.ExtractMember(MainWindowSource, "private void FlushProjectSetupDiscCountPreviewRefresh()");

        SourceContract.AssertInOrder(
            factory,
            "discCountChanged: totalDiscs =>",
            "_previewEffectiveTotalDiscs = totalDiscs;",
            "QueueProjectSetupDiscCountPreviewRefresh();");
        var callbackStart = factory.IndexOf("discCountChanged: totalDiscs =>", StringComparison.Ordinal);
        var callbackEnd = factory.IndexOf("metadataState:", callbackStart, StringComparison.Ordinal);
        Assert.True(callbackStart >= 0 && callbackEnd > callbackStart);
        Assert.DoesNotContain("UpdateIndexes();", factory[callbackStart..callbackEnd], StringComparison.Ordinal);
        Assert.Contains("_projectSetupDiscCountRefreshTimer.Stop();", queue, StringComparison.Ordinal);
        Assert.Contains("_projectSetupDiscCountRefreshTimer.Start();", queue, StringComparison.Ordinal);
        Assert.Contains("UpdateIndexes();", flush, StringComparison.Ordinal);
        Assert.True(CountOccurrences(MainWindowSource, "FlushProjectSetupDiscCountPreviewRefresh();") >= 5);
    }

    [Fact]
    public void Project_actions_use_their_semantic_button_families()
    {
        Assert.Equal(
            "{StaticResource SecondaryButtonStyle}",
            Attribute(Element("Button", "Content", "{Binding AddProjectSourcesButtonText}"), "Style"));
        Assert.Equal(
            "{StaticResource SecondaryButtonStyle}",
            Attribute(Element("Button", "Content", "{Binding SecondaryButtonText}"), "Style"));
        Assert.DoesNotContain("UiProjektAbbrechen", RepositorySource.Read(["BookStitch", "MainWindow.xaml"]));
        Assert.DoesNotContain("DeletePausedProjectAsync", MainWindowSource);
    }

    [Fact]
    public void Projects_action_binds_to_resume_refresh_availability()
    {
        Assert.Equal(
            "{Binding CanRefreshResumeProjects}",
            Attribute(Element("Button", "x:Name", "ProjectsButton"), "IsEnabled"));
    }

    [Fact]
    public void Source_presentation_delegates_to_the_service_and_refreshes_when_tracks_change()
    {
        var presentation = SourceContract.ExtractMember(
            MainWindowSource,
            "private MainWindowSourcePresentation SourcePresentation");
        Assert.Contains("MainWindowSourcePresentationService.Create(", presentation, StringComparison.Ordinal);
        Assert.Contains(
            "Tracks.Count > 0",
            SourceContract.ExtractMember(MainWindowSource, "public bool HasProjectTracks"),
            StringComparison.Ordinal);

        Assert.Contains(
            "SourcePresentation.DisplayText",
            SourceContract.ExtractMember(MainWindowSource, "public string SelectedSourceDisplayText"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SourcePresentation.OpenPath",
            SourceContract.ExtractMember(MainWindowSource, "public string SelectedSourceOpenPath"),
            StringComparison.Ordinal);

        var trackChange = SourceContract.ExtractMember(MainWindowSource, "private void Tracks_CollectionChanged");
        foreach (var property in new[]
                 {
                     "HasProjectTracks",
                     "SelectedSourceDisplayText",
                     "SelectedSourceOpenPath",
                     "CanOpenSelectedFolder"
                 })
        {
            Assert.Contains($"OnPropertyChanged(nameof({property}));", trackChange, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Output_folder_chip_retains_change_capability_without_redundant_open_button()
    {
        var view = Element("MainOutputSettingsControl", "x:Name", "MainOutputSettingsView");
        Assert.Equal("OutputFolderStateChip_Click", Attribute(view, "OpenFolderRequested"));
        Assert.Equal("ChooseOutputFolderButton_Click", Attribute(view, "ChangeFolderRequested"));
        var outputXaml = RepositorySource.LoadXaml(["BookStitch", "Controls", "MainOutputSettingsControl.xaml"]);
        var open = outputXaml.Descendants().Single(element => Attribute(element, "x:Name") == "OutputFolderStateChipButton");
        Assert.Equal("{Binding CanChooseOutputFolder}", Attribute(open, "IsEnabled"));
        Assert.Equal("ChangeFolder_Click", Attribute(open, "Click"));

        Assert.DoesNotContain(outputXaml.Descendants(), element => Attribute(element, "x:Name") == "OpenOutputFolderButton");
    }

    [Fact]
    public void Effective_output_folder_delegates_to_the_fallback_service()
    {
        var property = SourceContract.ExtractMember(MainWindowSource, "private string EffectiveOutputFolder");

        Assert.Contains("OutputFolderFallbackService.Resolve(OutputFolder)", property, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ComboBox", "ExportPresetComboBox", "AppearanceCompactComboBoxStyle")]
    [InlineData("TextBox", "ParallelJobsTextBox", "AppearanceCompactTextBoxStyle")]
    public void Compact_export_inputs_use_the_central_input_styles(
        string elementName,
        string name,
        string styleKey)
    {
        Assert.Equal(
            $"{{StaticResource {styleKey}}}",
            Attribute(Element(elementName, "x:Name", name), "Style"));
    }

    [Fact]
    public void Primary_export_action_binds_to_the_export_ui_state()
    {
        var export = Element("Button", "Click", "Export_Click");

        Assert.Equal("{Binding ExportButtonText}", Attribute(export, "Content"));
        Assert.Equal("{Binding CanStartExport}", Attribute(export, "IsEnabled"));
        Assert.Equal("{Binding ExportButtonVisibility}", Attribute(export, "Visibility"));
    }

    [Fact]
    public void Project_action_bar_separates_existing_project_actions_sort_group_and_explorer_button()
    {
        var bar = Element("Grid", "x:Name", "ProjectActionBar");
        var projects = bar.Elements().Single(e => Attribute(e, "x:Name") == "ProjectActionButtons");
        var arrows = bar.Elements().Single(e => Attribute(e, "x:Name") == "TrackOrderButtons");
        var folder = bar.Elements().Single(e => Attribute(e, "x:Name") == "ActionBarOpenOutputFolderButton");
        Assert.Equal("4", Attribute(bar, "Grid.Row"));
        Assert.Equal("0", Attribute(bar, "Grid.Column"));
        Assert.All(new[] { projects, arrows, folder }, group => Assert.Same(bar, group.Parent));
        Assert.Equal("0", Attribute(projects, "Grid.Column"));
        Assert.Equal("Left", Attribute(projects, "HorizontalAlignment"));
        Assert.Equal("1", Attribute(arrows, "Grid.Column"));
        Assert.Equal("Center", Attribute(arrows, "HorizontalAlignment"));
        Assert.Equal(new[] { "MoveSelectedUp_Click", "MoveSelectedDown_Click", "MoveSelectedToTop_Click", "MoveSelectedToBottom_Click" },
            arrows.Elements().Select(button => Attribute(button, "Click")));
        Assert.All(arrows.Elements(), button => Assert.Equal("{Binding CanEditTrackOrder}", Attribute(button, "IsEnabled")));
        foreach (var (click, enabled, visible) in new[] {
            ("Export_Click", "CanStartExport", "ExportButtonVisibility"),
            ("AddProjectSources_Click", "CanAddProjectSources", "AddProjectSourcesVisibility"),
            ("StopAndSaveDiscProject_Click", "CanStopAndSaveDiscProject", "StopAndSaveDiscProjectVisibility"),
            ("DeleteNewProject_Click", "CanDeletePausedNewProject", "DeleteNewProjectVisibility"),
            ("CancelExport_Click", "CanCancelExport", "SecondaryButtonVisibility") })
        {
            var button = projects.Descendants().Single(e => Attribute(e, "Click") == click);
            Assert.Equal($"{{Binding {enabled}}}", Attribute(button, "IsEnabled"));
            Assert.Equal($"{{Binding {visible}}}", Attribute(button, "Visibility"));
        }
        Assert.Single(bar.Descendants(), element => Attribute(element, "Style") == "{StaticResource AccentOpenOutputFolderButtonStyle}");
        Assert.Equal("2", Attribute(folder, "Grid.Column"));
        Assert.Equal("Right", Attribute(folder, "HorizontalAlignment"));
        Assert.Equal("{Binding CanOpenOutputFolder}", Attribute(folder, "IsEnabled"));
        Assert.Equal("{services:LocalizedText Key=MainWindow.Export.OpenFolder}", Attribute(folder, "AutomationProperties.Name"));
        Assert.Equal("OutputFolderStateChip_Click", Attribute(folder, "Click"));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = source.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static XElement Element(string name, string attribute, string value) =>
        RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"])
            .Descendants()
            .Single(element => element.Name.LocalName == name && Attribute(element, attribute) == value);

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name.Split(':')[^1])?.Value ?? "";
}
