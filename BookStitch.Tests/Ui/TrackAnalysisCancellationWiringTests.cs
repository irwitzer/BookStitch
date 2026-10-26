using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class TrackAnalysisCancellationWiringTests
{
    [Fact]
    public void Folder_import_and_resume_opt_into_cancellable_analysis_while_other_loads_default_to_none()
    {
        var mainWindow = LoadMainWindowSource();
        var selectFolder = SourceContract.ExtractBetween(
            mainWindow,
            "private async void SelectFolder_Click",
            "private async void SelectDiscSource_Click");
        Assert.Contains(
            "analysisCancellationScope: TrackAnalysisCancellationScope.LocalFolderImport",
            selectFolder,
            StringComparison.Ordinal);

        var resume = SourceContract.ExtractBetween(
            mainWindow,
            "private async Task LoadResumeProjectIntoWorkspaceAsync",
            "private void PublishLoadedProjectStatus");
        Assert.Contains(
            "_trackAnalysisCancellationScope = TrackAnalysisCancellationScope.ResumeLoad;",
            resume,
            StringComparison.Ordinal);

        var loadFolder = SourceContract.ExtractBetween(
            mainWindow,
            "private async Task LoadFolderAsync",
            "private async Task<bool> EnrichTracksWithFfprobeAsync");
        Assert.Contains(
            "TrackAnalysisCancellationScope analysisCancellationScope = TrackAnalysisCancellationScope.None",
            loadFolder,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Active_analysis_uses_the_existing_cancel_action_and_publishes_pending_feedback()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var cancelButtons = document.Descendants()
            .Where(element =>
                element.Name.LocalName == "Button" &&
                Attr(element, "Click") == "CancelExport_Click")
            .ToArray();
        Assert.NotEmpty(cancelButtons);
        Assert.All(
            cancelButtons,
            button => Assert.Equal("{Binding CanCancelExport}", Attr(button, "IsEnabled")));

        var cancelAction = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async void CancelExport_Click",
            "private void PauseAudioDiscElapsedTimer");
        var analysisBranch = SourceContract.ExtractBetween(
            cancelAction,
            "if (_trackAnalysisCancellation is not null",
            "if (IsDiscImporting");
        SourceContract.AssertInOrder(
            analysisBranch,
            "StatusText =",
            "ExportProgressText =",
            "await RequestCancellationWithFeedbackAsync(_trackAnalysisCancellation);",
            "NotifyExportUiStateChanged();",
            "return;");
    }

    [Fact]
    public void Canceled_analysis_resets_the_workspace_and_clears_its_cancellation_scope()
    {
        var reset = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private void ResetWorkspaceAfterCanceledTrackAnalysis",
            "private ExportCheckResult ValidateExportPlan");

        SourceContract.AssertInOrder(
            reset,
            "_trackAnalysisCancellation = null;",
            "_trackAnalysisCancellationScope = TrackAnalysisCancellationScope.None;",
            "ClearSelectedSourceDisplayOverride();",
            "ClearPendingMp3DiscPreparation();",
            "_activeMp3DiscManifest = null;",
            "ResetLoadedResumeProjectState();",
            "ResetMetadataForNewFolderProject();",
            "_currentFolderPath = string.Empty;",
            "_currentProjectWorkFolder = string.Empty;",
            "SelectedFolder =",
            "Tracks.Clear();",
            "IsProgressIndeterminate = false;",
            "NotifyExportUiStateChanged();",
            "OnExportPreviewChanged();");
    }

    private static string LoadMainWindowSource() =>
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    private static string Attr(XElement element, string attributeName) =>
        element.Attribute(attributeName)?.Value ?? string.Empty;
}
