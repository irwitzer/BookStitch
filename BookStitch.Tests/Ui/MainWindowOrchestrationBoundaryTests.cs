using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MainWindowOrchestrationBoundaryTests
{
    [Fact]
    public void MainWindow_uses_extracted_state_and_availability_composition()
    {
        var text = LoadMainWindowText();

        Assert.Contains(
            "ExportUiStateInputService.Create(",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExportUiState.CanStartExport ||",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "AudioDiscExportAvailabilityService.CanStart(",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Export_click_handles_active_pipeline_pause_before_resume_or_start_paths()
    {
        var text = LoadMainWindowText();

        var pauseGuard = text.IndexOf(
            "if (IsPipelinePauseCommandActive())",
            StringComparison.Ordinal);
        var loadedMp3Resume = text.IndexOf(
            "await ContinueLoadedMp3DiscProjectAsync();",
            pauseGuard,
            StringComparison.Ordinal);
        var pendingMp3Start = text.IndexOf(
            "await StartConfirmedMp3DiscProjectAsync(",
            pauseGuard,
            StringComparison.Ordinal);

        Assert.True(pauseGuard >= 0);
        Assert.True(loadedMp3Resume > pauseGuard);
        Assert.True(pendingMp3Start > pauseGuard);
    }

    [Fact]
    public void MainWindow_uses_shared_change_decisions()
    {
        var text = LoadMainWindowText();

        Assert.Contains(
            "MainWindowSelectionStateService.CreateBooleanChange(",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "MainWindowSelectionStateService.CreateReferenceChange(",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "MainWindowSelectionStateService.CreateOrdinalStringChange(",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_refreshes_system_sleep_prevention_from_pipeline_activity()
    {
        var text = LoadMainWindowText();

        Assert.Contains(
            "private void RefreshSystemSleepPrevention()",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "_settings.KeepComputerAwakeDuringLongOperations",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "_services.Foundation.SystemSleepPreventionActivityService.ShouldKeepAwake(",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProjectReviewState.Phase",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_settings.KeepComputerAwakeDuringLongOperations &&\r\n            (IsBusy || IsExporting || IsDiscImporting)",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "_services.Foundation.SystemSleepPreventionService.SetActive(shouldKeepAwake);",
            text,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(text, "RefreshSystemSleepPrevention();") >= 4,
            "MainWindow should refresh sleep prevention after activity state changes and settings updates.");
        Assert.Contains(
            "_services.Foundation.SystemSleepPreventionService.Dispose();",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_does_not_restore_removed_duplicate_change_services()
    {
        var text = LoadMainWindowText();

        Assert.DoesNotContain(
            "MetadataTextChangeService",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CreateFolderChange(",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_uses_presentation_state_directly_without_proxy_members()
    {
        var text = LoadMainWindowText();

        string[] stateAccesses =
        [
            "_presentationState.IsBusy",
            "_presentationState.IsExporting",
            "_presentationState.IsDiscImporting",
            "_presentationState.IsPipelinePaused",
            "_presentationState.IsPipelineFailed",
            "_presentationState.IsProjectExtensionRun",
            "_presentationState.PauseRequested"
        ];

        foreach (var stateAccess in stateAccesses)
        {
            Assert.Contains(
                stateAccess,
                text,
                StringComparison.Ordinal);
        }

        string[] removedProxyMembers =
        [
            "private bool _isBusy",
            "private bool _isExporting",
            "private bool _isDiscImporting",
            "private bool _isPipelinePaused",
            "private bool _isPipelineFailed",
            "private bool _isProjectExtensionRun",
            "private bool _pauseRequested"
        ];

        foreach (var removedProxyMember in removedProxyMembers)
        {
            Assert.DoesNotContain(
                removedProxyMember,
                text,
                StringComparison.Ordinal);
        }
    }

    private static string LoadMainWindowText([CallerFilePath] string testFilePath = "")
    {
        var sourceRoot = Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName;
        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), sourceRoot })
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var text = TryLoadMainWindowText(root);
            if (text is not null)
                return text;
        }

        throw new FileNotFoundException(
            "MainWindow.xaml.cs wurde im Repository nicht gefunden.");
    }

    private static string? TryLoadMainWindowText(string root)
    {
        var directory = new DirectoryInfo(root);

        while (directory is { Exists: true })
        {
            var candidate = Path.Combine(
                directory.FullName,
                "BookStitch",
                "MainWindow.xaml.cs");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        return null;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
