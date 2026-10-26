using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class PipelineControlWiringTests
{
    [Fact]
    public void Export_entry_does_not_simulate_conversion_before_shared_workflow_decides_preparation()
    {
        var export = SourceContract.ExtractMember(LoadMainWindowSource(), "private async Task ExportToAacWithWorkFolderAsync(");
        var entry = export[..export.IndexOf("SetPipelineState = state =>", StringComparison.Ordinal)];
        Assert.DoesNotContain("SetPipelineState(ProjectPipelineState.Converting);", entry);
        SourceContract.AssertInOrder(export, "SetPipelineState = state => Dispatcher.Invoke(() =>", "SetPipelineState(state);", "ClearProjectExtensionPauseContext();");
    }

    [Fact]
    public void Output_and_chapter_panel_permission_and_collapse_follow_ui_state_notifications()
    {
        var source = LoadMainWindowSource();
        var notify = SourceContract.ExtractMember(source, "private void NotifyExportUiStateChanged()");
        SourceContract.AssertInOrder(notify, "ExportUiStateService.IsPreparationProcessing",
            "!_wasPreparationProcessing && processing", "SetMetadataPanelExpanded(false, animate: true)");
        var xaml = System.Xml.Linq.XDocument.Parse(RepositorySource.Read(["BookStitch", "MainWindow.xaml"]));
        var content = xaml.Descendants().Single(element =>
            (string?)element.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MetadataDetailsContent");
        Assert.Equal("{Binding CanChangeExportOptions}", (string?)content.Attribute("IsEnabled"));
        Assert.Contains(content.Descendants(), element => element.Name.LocalName == "MainOutputSettingsControl");
        Assert.Contains(content.Descendants(), element =>
            (string?)element.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MainChapterModeComboBox");
        var folder = SourceContract.ExtractMember(source, "private async void SelectFolder_Click");
        var disc = SourceContract.ExtractMember(source, "private async void SelectDiscSource_Click");
        Assert.DoesNotContain("SetMetadataPanelExpanded", folder);
        Assert.DoesNotContain("SetMetadataPanelExpanded", disc);
    }

    [Fact]
    public void Local_project_file_extension_uses_the_shared_audio_input_filter()
    {
        var addSources = SourceContract.ExtractMember(
            LoadMainWindowSource(),
            "private async Task AddMoreLocalSourcesAsync()");

        Assert.Contains(
            "Filter = AudioInputFormatCatalog.OpenFileDialogFilter",
            addSources,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_folder_import_stops_at_preparing_without_opening_setup_or_export()
    {
        var selectFolder = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async void SelectFolder_Click",
            "private async void SelectDiscSource_Click");

        SourceContract.AssertInOrder(
            selectFolder,
            "await LoadFolderAsync(",
            "SetMetadataEditingAvailable(true);",
            "NotifyExportUiStateChanged();");
        Assert.DoesNotContain("CreateProjectSetupDialog", selectFolder, StringComparison.Ordinal);
        Assert.DoesNotContain("ModalDialogService.ShowDialog", selectFolder, StringComparison.Ordinal);
        Assert.DoesNotContain("RunCurrentExportPlanAsync", selectFolder, StringComparison.Ordinal);
    }

    [Fact]
    public void Preparing_project_primary_action_opens_setup_before_any_export_work()
    {
        var exportClick = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async void Export_Click",
            "private bool IsPipelinePauseCommandActive");

        SourceContract.AssertInOrder(
            exportClick,
            "ProjectReviewState.Phase == ProjectPipelineState.Preparing",
            "Tracks.Count > 0",
            "!LoadedProjectState.NeedsDiscImport",
            "await ShowCurrentProjectSetupDialogAsync();",
            "return;");
    }

    [Fact]
    public void Mp3_import_failure_enters_stable_failure_state_before_notifying_the_user()
    {
        var failure = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private void MarkMp3DiscImportFailed",
            "private void QueueLiveConversionForCopiedFile");

        SourceContract.AssertInOrder(
            failure,
            "PipelinePauseTransitionService.CreateFailure();",
            "ApplyPipelineTransition(failureTransition);",
            "NotificationService.Notify(NotificationEvent.Error);",
            "AppDialogService.Error(",
            "Mp3DiscUiStatusService.CreateImportFailed()");
    }

    [Fact]
    public void Export_blocking_errors_are_classified_and_notified_before_the_dialog()
    {
        var failure = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private void ShowExportBlockingErrors",
            "private async Task ExportToAacWithWorkFolderAsync");

        Assert.Contains("containsInvalidAudio", failure, StringComparison.Ordinal);
        Assert.Contains(
            "Reason: containsInvalidAudio ? \"InvalidAudio\" : \"BlockingErrors\"",
            failure,
            StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            failure,
            "NotificationService.Notify(",
            "AppDialogService.Show(");
    }

    private static string LoadMainWindowSource() =>
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
}
