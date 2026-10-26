using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ProjectCatalogWiringTests
{
    [Fact]
    public void Startup_only_initializes_empty_inventory_and_rebuild_outcome_precedes_reload_side_effects()
    {
        var main=RepositorySource.Read(["BookStitch","MainWindow.xaml.cs"]);
        var startup=SourceContract.ExtractMember(main,"private void InitializeEmptyProjectSweepState(");
        Assert.Contains("TryInitializeEmptyRoot", startup);
        Assert.Contains("InitializeEmptyProjectSweepState", SourceContract.ExtractMember(main,"private void MainWindow_Loaded("));
        Assert.DoesNotContain("ShouldStartAutomaticSweep", main);
        Assert.DoesNotContain("ConfirmProjectUpdate", main);
        Assert.DoesNotContain("RunProjectIntegritySweep", startup);
        Assert.DoesNotContain("Save",startup);
        var operation=SourceContract.ExtractMember(main,"private async Task<ProjectOperationOutcome> RunProjectIntegrityOperation(");
        SourceContract.AssertInOrder(operation,"ModalDialogService.ShowDialog","if(dialog.Outcome!=ProjectOperationOutcome.Completed)return dialog.Outcome;","LoadResumeProjectIntoWorkspaceAsync","Catalog.MarkDirty()");
        foreach(var path in new[]{"Dialog/ResumeProjectDialog.xaml.cs","Controls/SettingsProjectManagementControl.xaml.cs"})
        {
            var source=RepositorySource.Read(("BookStitch/"+path).Split('/'));
            var rebuild=SourceContract.ExtractMember(source,"private async void RebuildProject_Click(");
            SourceContract.AssertInOrder(rebuild,"await _rebuild(","!=ProjectOperationOutcome.Completed","return;","MarkDirty()","LoadProjectsAsync");
        }
    }

    [Fact]
    public void Project_folder_entry_points_share_warning_and_both_rebuild_entry_points_share_the_main_action()
    {
        var main=RepositorySource.Read(["BookStitch","MainWindow.xaml.cs"]);
        Assert.Contains("ProjectFolderOpenService.Open",SourceContract.ExtractMember(main,"private void OpenFolderInExplorer("));
        Assert.Contains("warnForProjectFolder: true", SourceContract.ExtractMember(main,"private void OpenWorkFolder_Click("));
        foreach(var path in new[]{"Dialog/ResumeProjectDialog.xaml.cs","Controls/SettingsProjectManagementControl.xaml.cs"})
        {
            var source=RepositorySource.Read(("BookStitch/"+path).Split('/'));
            Assert.Contains("ProjectFolderOpenService.Open",source);
            Assert.DoesNotContain("Process.Start",source);
            Assert.Contains("_rebuild(",SourceContract.ExtractMember(source,"private async void RebuildProject_Click("));
        }
        var setup=RepositorySource.Read(["BookStitch","Dialog","DiscProjectSetupDialog.xaml.cs"]);
        Assert.Contains("ProjectFolderOpenService.Open",SourceContract.ExtractMember(setup,"private void ProjectFolderButton_Click("));
        Assert.Contains("RunProjectRebuild",SourceContract.ExtractMember(main,"private async void OpenResumeProjects_Click("));
        Assert.Contains("rebuildProject: RunProjectRebuild",main);
    }

    [Fact]
    public void Startup_defers_catalog_until_loaded_and_browser_and_settings_share_the_same_catalog()
    {
        var main = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var constructor = SourceContract.ExtractMember(main, "public MainWindow()");
        Assert.DoesNotContain("RefreshResumeProjects(showStatus: false)", constructor);
        var loaded = SourceContract.ExtractMember(main, "private void MainWindow_Loaded(");
        Assert.Contains("Dispatcher.BeginInvoke", loaded);
        Assert.Contains("RefreshProjectCatalogAsync(showStatus: false)", loaded);
        Assert.DoesNotContain("await", loaded);
        foreach (var path in new[] { "Dialog/ResumeProjectDialog.xaml.cs", "Controls/SettingsProjectManagementControl.xaml.cs" })
        {
            var source = RepositorySource.Read(("BookStitch/" + path).Split('/'));
            Assert.Contains("_catalog = projectIndexService.Catalog;", source);
            Assert.DoesNotContain("ScanSelectableProjects(", source);
            Assert.DoesNotContain("ScanDamagedProjects(", source);
        }
        var refresh = SourceContract.ExtractMember(main, "private void RefreshResumeProjects(bool showStatus)");
        Assert.Contains("Catalog.MarkDirty()", refresh);
        Assert.DoesNotContain("ScanSelectableProjects", refresh);
    }

    [Fact]
    public void Browser_keeps_fresh_load_validation_and_stale_preview_guards_with_catalog_damage()
    {
        var dialog = RepositorySource.Read(["BookStitch", "Dialog", "ResumeProjectDialog.xaml.cs"]);
        var load = SourceContract.ExtractMember(dialog, "private async Task LoadSelectedProjectAsync()");
        Assert.Contains("_projectResumePlanService.BuildFromProjectFolder(project.ProjectFolder)", load);
        var preview = SourceContract.ExtractMember(dialog, "private async Task UpdateProjectDetailsAsync()");
        Assert.Contains("_projectDetailsGate.WaitAsync(token)", preview);
        Assert.Contains("version != _projectDetailsVersion", preview);
        Assert.Contains("token.IsCancellationRequested", preview);
        var damage = SourceContract.ExtractMember(dialog, "private async void QueueDamagedProjectScan(");
        Assert.Contains("WaitForDamageAsync(generation, token)", damage);
        Assert.Contains("_catalog.Snapshot?.Generation != generation", damage);
        Assert.Contains("scanVersion != _damagedProjectScanVersion", damage);
        Assert.Contains("ShowDamagedProjectWarnings(damaged)", damage);
    }
    [Fact]
    public void Maintenance_uses_initialized_safe_contexts_and_cancels_on_workflow_state_changes()
    {
        var main = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        Assert.Contains("settingsWindow.Loaded +=", main);
        Assert.Contains("dialog.Loaded += (_, _) => { ++_openMetadataContexts; CheckAutomaticMaintenance(); }", main);
        var check = SourceContract.ExtractMember(main, "private void CheckAutomaticMaintenance()");
        foreach (var guard in new[] { "MaintenanceSafeSpace.GlobalSettings", "MaintenanceSafeSpace.Metadata", "MaintenanceSafeSpace.Complete",
            "IsBusy", "IsExporting", "IsDiscImporting", "_isWorkspaceTransitioning", "_isResumeLoading", "_isDiscStopSaveInProgress", "_isProjectCloseInProgress", "_isProjectBrowserOpen" })
            Assert.Contains(guard, check);
        Assert.Contains("_maintenanceCoordinator.SetEligibility(eligibility)", check);
        Assert.Contains("CheckAutomaticMaintenance()", SourceContract.ExtractMember(main, "private void NotifyExportUiStateChanged()"));
        Assert.DoesNotContain("Task.Delay", check);
        var browser = SourceContract.ExtractMember(main, "private async void OpenResumeProjects_Click(");
        SourceContract.AssertInOrder(browser, "_isProjectBrowserOpen = true;", "StopAutomaticMaintenance();", "new ResumeProjectDialog(");
        Assert.Contains("_isProjectBrowserOpen = false;", browser);
        var run = SourceContract.ExtractMember(main, "private async Task RunAutomaticMaintenanceAsync()");
        Assert.Contains("_settings.ProjectRetentionDays", run);
        Assert.Contains("_settings.ProjectRetentionMode", run);
        Assert.Contains("LastAutomaticMaintenanceUtc", run);
        Assert.Contains("GlobalSettingsManager.SaveImmediate()", run);
    }
    [Fact]
    public void Developer_previews_reuse_the_host_index_instead_of_creating_a_scanner_per_open()
    {
        var main = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var create = SourceContract.ExtractMember(main, "private DeveloperToolsWindow CreateDeveloperToolsWindow()");
        Assert.Contains("projectIndexService: _services.Foundation.ProjectIndexService", create);
        foreach (var relative in new[] { "Windows/DeveloperToolsWindow.xaml.cs", "Dialog/DialogPreviewWindow.xaml.cs" })
        {
            var source = RepositorySource.Read(("BookStitch/" + relative).Split('/'));
            var preview = SourceContract.ExtractMember(source, "private void ResumeProjectPreview_Click(");
            Assert.Contains("_projectIndexService", preview);
            Assert.DoesNotContain("new ProjectIndexService()", preview);
        }
    }
}
