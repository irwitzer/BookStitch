using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF: actual completion, layout, bindings and modal warning interaction.
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Project_management_shows_recovery_health_and_refreshes_selected_project_after_rebuild()
    {
        RunOnStaThread(() =>
        {
            using var root = new TemporaryFolder();
            var structure = WorkFolderStructure.FromRoot(root.Path);
            var rebuild = System.IO.Path.Combine(structure.LocalProjectsFolder, "Rebuild");
            var damaged = System.IO.Path.Combine(structure.LocalProjectsFolder, "Damaged");
            var healthy = System.IO.Path.Combine(structure.LocalProjectsFolder, "Healthy");
            var irrelevant = System.IO.Path.Combine(structure.LocalProjectsFolder, "InProgress");
            foreach (var (folder, health) in new[] { (rebuild, ProjectHealthStatus.RebuildRequired), (damaged, ProjectHealthStatus.Damaged) })
            {
                System.IO.Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(folder));
                System.IO.File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(folder), "{broken");
                System.IO.File.WriteAllText(System.IO.Path.Combine(ProjectFolderLayout.GetSettingsFolder(folder), "project-integrity.json"),
                    System.Text.Json.JsonSerializer.Serialize(new ProjectIntegrityResult(folder, health, false, false,
                        [new(IntegrityIssueCode.ManifestConflict, health, RepairActionCode.None, "Invalid legacy work manifest.")])));
            }
            foreach (var folder in new[] { healthy, irrelevant })
            {
                System.IO.Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(folder));
                System.IO.File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(folder), System.Text.Json.JsonSerializer.Serialize(new ExportWorkManifest {
                    ProjectType = ProjectManifestTypes.FolderProject, ProjectWorkFolder = folder,
                    State = new() { Status = folder == healthy ? ProjectManifestStatuses.Completed : ProjectManifestStatuses.Created } }));
            }
            var index = new ProjectIndexService();
            index.Catalog.RefreshAsync(root.Path, 180).GetAwaiter().GetResult();
            using var manager = new GlobalSettingsManager(new SettingsService(System.IO.Path.Combine(root.Path, "app"), root.Path));
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var control = new BookStitch.Controls.SettingsProjectManagementControl();
            var owner = new Window { Content = control, Width = 900, Height = 750 }; owner.Show();
            try
            {
                control.Initialize(manager, _ => new ProjectCleanupResult(0, 0, []), root.Path, index, new ProjectResumePlanService());
                DrainDispatcher();
                Assert.Equal(3, control.Projects.Count);
                Assert.DoesNotContain(control.Projects, item => item.ProjectFolder == irrelevant);
                control.SelectedProject = control.Projects.Single(item => item.ProjectFolder == rebuild);
                var old = control.SelectedProject;
                Assert.True(old.CanRebuild); Assert.Equal(ProjectHealthStatus.RebuildRequired, old.HealthStatus);
                var button = FindVisualDescendant<Button>(control, b => Equals(b.Content, LocalizationText.Get("Integrity.Rebuild")))!;
                control.ConfigureIntegrity(null, (_, folder, _) => {
                    Assert.Equal(rebuild, folder);
                    System.IO.File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(folder), System.Text.Json.JsonSerializer.Serialize(new ExportWorkManifest {
                        ProjectType = ProjectManifestTypes.FolderProject, ProjectWorkFolder = folder, State = new() { Status = ProjectManifestStatuses.Completed } }));
                    return Task.FromResult(ProjectOperationOutcome.Completed);
                });
                DrainDispatcher(); Assert.Equal(Visibility.Visible, button.Visibility);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (ReferenceEquals(old, control.SelectedProject) && DateTime.UtcNow < deadline) { DrainDispatcher(); Thread.Sleep(10); }
                Assert.NotSame(old, control.SelectedProject); Assert.Equal(rebuild, control.SelectedProject!.ProjectFolder);
                Assert.True(control.SelectedProject.IsSelectableProject); Assert.False(control.SelectedProject.CanRebuild);
                Assert.DoesNotContain(control.SelectedProject.HealthStatus, new[] { ProjectHealthStatus.RebuildRequired, ProjectHealthStatus.Damaged });
            }
            finally { owner.Close(); index.Catalog.Dispose(); SynchronizationContext.SetSynchronizationContext(previousContext); }
        });
    }

    [Theory][InlineData(ProjectOperationOutcome.Completed)][InlineData(ProjectOperationOutcome.Canceled)][InlineData(ProjectOperationOutcome.Failed)]
    public void Resume_rebuild_reloads_only_after_completed_and_preserves_preview_on_cancel(ProjectOperationOutcome outcome)
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();var called=0;
            var dialog=new ResumeProjectDialog(root.Path,new ProjectIndexService(),new ProjectResumePlanService(),180,AppearanceMode.Dark,
                rebuildProject:(_,_,_)=>{called++;return Task.FromResult(outcome);});
            try
            {
                dialog.SelectedProject=new ProjectIndexItem {ProjectFolder=root.Path,HealthStatus=ProjectHealthStatus.RebuildRequired};
                dialog.Measure(new Size(1000,700));dialog.Arrange(new Rect(0,0,1000,700));DrainDispatcher();
                var load=typeof(ResumeProjectDialog).GetField("_projectLoadVersion",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var details=typeof(ResumeProjectDialog).GetField("_projectDetailsVersion",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var before=(int)load.GetValue(dialog)!;var previewVersion=details.GetValue(dialog);var selection=dialog.SelectedProject;var preview=dialog.ProjectDetails;
                ((Button)dialog.FindName("RebuildProjectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1,called);
                if(outcome==ProjectOperationOutcome.Completed)Assert.True((int)load.GetValue(dialog)!>before);
                else {Assert.Equal(before,load.GetValue(dialog));Assert.Equal(previewVersion,details.GetValue(dialog));Assert.Same(selection,dialog.SelectedProject);Assert.Equal(preview,dialog.ProjectDetails);}
            }
            finally {dialog.Close();}
        });
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Startup_update_confirmation_has_now_and_later_without_a_suppression_toggle(bool now)
    {
        RunOnStaThread(() =>
        {
            var owner=new Window();owner.Show();Exception? failure=null;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var prompt=owner.OwnedWindows.OfType<AppMessageDialog>().Single();
                try
                {
                    Assert.Equal(LocalizationText.Get("Integrity.Startup.Title"),prompt.Title);
                    Assert.Equal(Visibility.Collapsed,prompt.AdditionalContent.Visibility);
                    var buttons=prompt.ButtonsPanel.Children.OfType<Button>().ToArray();Assert.Equal(2,buttons.Length);
                    buttons.Single(b=>Equals(b.Content,LocalizationText.Get(now?"Integrity.Startup.Now":"Integrity.Startup.Later")))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception ex){failure=ex;prompt.Close();}
            }));
            try {Assert.Equal(now,AppDialogService.ConfirmProjectUpdate(owner));Assert.Null(failure);}
            finally {owner.Close();}
        });
    }

    [Fact]
    public void Coarse_phase_activity_cycles_dots_without_changing_progress_and_stops_on_phase_change_and_cancel()
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var probes=new AudioProbeBatchService(new AudioInfoService(),new FfprobeConcurrencyPolicy(),new FfprobeCoordinator());
            var dialog=new ProjectIntegrityDialog(root.Path,probes,new(""),new DiagnosticLogService(root.Path),settleMaintenance:()=>pending.Task);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var report=typeof(ProjectIntegrityDialog).GetMethod("ShowProgress",flags)!;
            var tick=typeof(ProjectIntegrityDialog).GetMethod("ActivityTick",flags)!;
            var timer=(DispatcherTimer)typeof(ProjectIntegrityDialog).GetField("_activityTimer",flags)!.GetValue(dialog)!;
            try
            {
                dialog.Show();DrainDispatcher();
                report.Invoke(dialog,[new IntegrityProgress("Integrity.Phase.Repair",root.Path,ProcessedProjects:1,TotalProjects:2)]);
                var phase=(TextBlock)dialog.FindName("PhaseText");var bar=(ProgressBar)dialog.FindName("Progress");
                var baseText=LocalizationText.Get("Integrity.Phase.Repair").TrimEnd(' ','.','…');var value=bar.Value;var maximum=bar.Maximum;
                Assert.True(timer.IsEnabled);
                foreach(var dots in new[]{1,2,3,1})
                {Assert.Contains(baseText+new string('.',dots),phase.Text);Assert.Equal(value,bar.Value);Assert.Equal(maximum,bar.Maximum);tick.Invoke(dialog,[null,EventArgs.Empty]);}
                report.Invoke(dialog,[new IntegrityProgress("Integrity.Phase.Probe",root.Path,1,3)]);Assert.False(timer.IsEnabled);
                report.Invoke(dialog,[new IntegrityProgress("Integrity.Phase.Repair",root.Path)]);Assert.True(timer.IsEnabled);
                ((Button)dialog.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.False(timer.IsEnabled);
            }
            finally
            {
                pending.TrySetResult();
                var frame=new DispatcherFrame();var finish=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};var deadline=DateTime.UtcNow.AddSeconds(3);
                finish.Tick+=(_,_)=>{if(dialog.Outcome==ProjectOperationOutcome.Canceled||DateTime.UtcNow>=deadline)frame.Continue=false;};
                finish.Start();try {Dispatcher.PushFrame(frame);}finally {finish.Stop();}
                Assert.Equal(ProjectOperationOutcome.Canceled,dialog.Outcome);Assert.False(timer.IsEnabled);dialog.Close();
            }
        });
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Project_maintenance_failure_and_cancellation_do_not_show_a_success_bar(bool cancel)
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();
            var probes=new AudioProbeBatchService(new AudioInfoService(),new FfprobeConcurrencyPolicy(),new FfprobeCoordinator());
            var dialog=new ProjectIntegrityDialog(root.Path,probes,new(""),new DiagnosticLogService(root.Path),
                settleMaintenance:()=>cancel?Task.FromCanceled(new CancellationToken(true)):Task.FromException(new System.IO.IOException("fixture failure")));
            try
            {
                dialog.Show();DrainDispatcher();
                var bar=(ProgressBar)dialog.FindName("Progress");var result=(TextBlock)dialog.FindName("ResultText");
                Assert.Contains(LocalizationText.Get(cancel?"Integrity.Run.Canceled":"Integrity.Failed"),result.Text);
                Assert.False(bar.IsIndeterminate);Assert.NotEqual(100,bar.Value);
                Assert.NotEqual(Colors.ForestGreen,Assert.IsType<SolidColorBrush>(bar.Foreground).Color);
                Assert.DoesNotContain(LocalizationText.Get("Integrity.Success"),result.Text);
            }
            finally {dialog.Close();}
        });
    }

    [Theory][InlineData(ProjectHealthStatus.OK,false)][InlineData(ProjectHealthStatus.Warning,false)]
    [InlineData(ProjectHealthStatus.Damaged,false)][InlineData(ProjectHealthStatus.RebuildRequired,true)]
    public void Resume_rebuild_button_uses_health_binding_and_the_configured_rebuild_action(ProjectHealthStatus health,bool visible)
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();var invoked=0;Window? dialogOwner=null;
            var dialog=new ResumeProjectDialog(root.Path,new ProjectIndexService(),new ProjectResumePlanService(),180,AppearanceMode.Dark,
                rebuildProject:(owner,path,hashes)=> {Assert.Same(dialogOwner,owner);Assert.Equal(root.Path,path);Assert.False(hashes);invoked++;return Task.FromResult(ProjectOperationOutcome.Completed);});
            dialogOwner=dialog;
            try
            {
                dialog.SelectedProject=new ProjectIndexItem {ProjectFolder=root.Path,HealthStatus=health};
                dialog.Measure(new Size(1000,700));dialog.Arrange(new Rect(0,0,1000,700));DrainDispatcher();
                var button=(Button)dialog.FindName("RebuildProjectButton");
                Assert.Equal(visible?Visibility.Visible:Visibility.Collapsed,button.Visibility);
                if(visible){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(1,invoked);}
            }
            finally {dialog.Close();dialogOwner=null;}
        });
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Project_maintenance_success_keeps_a_green_complete_bar_and_result_below_it(bool rebuild)
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();
            var project=System.IO.Path.Combine(root.Path,"project");
            ProjectFolderLayout.EnsureProjectFolders(project);
            new AtomicJsonStore().Save(ProjectFolderLayout.GetFolderManifestPath(project),new FolderProjectManifest {ProjectId="test",ProjectFolder=project},new());
            new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(project),new ExportWorkManifest {ProjectId="test",ProjectWorkFolder=project},new());
            new ProjectMetadataStore().Save(project,new());
            new TrackListStateService().Save(project,[]);
            var probes=new AudioProbeBatchService(new AudioInfoService(),new FfprobeConcurrencyPolicy(),new FfprobeCoordinator());
            var dialog=new ProjectIntegrityDialog(root.Path,probes,new(""),new DiagnosticLogService(root.Path),rebuildProject:rebuild?project:null);
            try
            {
                dialog.Show();
                var bar=(ProgressBar)dialog.FindName("Progress");
                var result=(TextBlock)dialog.FindName("ResultText");
                var close=(Button)dialog.FindName("CancelButton");
                var frame=new DispatcherFrame();
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};
                var deadline=DateTime.UtcNow.AddSeconds(8);
                timer.Tick+=(_,_)=> {if(Equals(close.Content,LocalizationText.Get("UiSchliessen"))||DateTime.UtcNow>=deadline)frame.Continue=false;};
                timer.Start();try {Dispatcher.PushFrame(frame);}finally {timer.Stop();}
                Assert.Equal(LocalizationText.Get("UiSchliessen"),close.Content);
                Assert.True(bar.IsVisible);Assert.False(bar.IsIndeterminate);Assert.Equal(100,bar.Maximum);Assert.Equal(100,bar.Value);
                Assert.Equal(Colors.ForestGreen,Assert.IsType<SolidColorBrush>(bar.Foreground).Color);
                Assert.Contains(LocalizationText.Get(rebuild?"Rebuild.Success":"Integrity.Success"),result.Text);
                Assert.True(result.TransformToAncestor(dialog).Transform(new Point()).Y>bar.TransformToAncestor(dialog).Transform(new Point()).Y);
                if(!rebuild)Assert.Contains(LocalizationText.Format("Integrity.Count.Checked",1),result.Text);
                Assert.DoesNotContain(LocalizationText.Get("Integrity.Issue.RebuildTrackStateLost"),result.Text);
                DrainDispatcher();Assert.Equal(100,bar.Value);
            }
            finally {dialog.Close();}
        });
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Project_folder_warning_only_opens_on_accept_and_persists_the_warning_choice(bool accept)
    {
        RunOnStaThread(() =>
        {
            using var root=new TemporaryFolder();
            var service=new SettingsService(System.IO.Path.Combine(root.Path,"app"),System.IO.Path.Combine(root.Path,"projects"));
            using var manager=new GlobalSettingsManager(service);
            var owner=new Window();owner.Show();
            Exception? failure=null;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var warning=owner.OwnedWindows.OfType<AppMessageDialog>().Single();
                try
                {
                    var toggle=Assert.IsType<CheckBox>(warning.AdditionalContent.Content);
                    Assert.True(toggle.IsChecked);toggle.IsChecked=false;
                    warning.ButtonsPanel.Children.OfType<Button>().Single(b=>Equals(b.Content,LocalizationText.Get(accept?"Resume.Project.OpenFolder":"Common.Cancel")))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception ex){failure=ex;warning.Close();}
            }));
            var opened=0;
            try {ProjectFolderOpenService.Open(owner,root.Path,manager,_=>opened++);}
            finally {owner.Close();}
            Assert.Null(failure);Assert.Equal(accept?1:0,opened);
            Assert.Equal(accept,WarningSuppressionService.IsSuppressed(service.Load(),WarningSuppressionService.ProjectFolder));
            if(accept)
            {
                ProjectFolderOpenService.Open(owner,root.Path,manager,_=>opened++);
                Assert.Equal(2,opened);
                WarningSuppressionService.ResetAll(manager.Current);manager.SaveImmediate();
                Assert.False(WarningSuppressionService.IsSuppressed(service.Load(),WarningSuppressionService.ProjectFolder));
                var resumedOwner = new Window();
                resumedOwner.Show();
                var warnedAgain = false;
                resumedOwner.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = resumedOwner.OwnedWindows.OfType<AppMessageDialog>().Single();
                    warnedAgain = true;
                    warning.ButtonsPanel.Children.OfType<Button>().Single(button => button.IsCancel)
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                try { ProjectFolderOpenService.Open(resumedOwner, root.Path, manager, _ => opened++); }
                finally { resumedOwner.Close(); }
                Assert.True(warnedAgain);
                Assert.Equal(2, opened);

            }
        });
    }
    [Fact]
    public void Reset_all_warnings_reopens_the_metadata_draft_warning()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings();
            settings.MetadataPreferences.WarnBeforeDiscardingConceptDrafts = false;
            WarningSuppressionService.ResetAll(settings);
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), settings.MetadataPreferences);
            Exception? failure = null; var warned = false;
            try
            {
                dialog.Show(); DrainDispatcher();
                dialog.TitleField.Draft = "Unconfirmed title";
                dialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = dialog.OwnedWindows.OfType<AppMessageDialog>().SingleOrDefault();
                    try
                    {
                        Assert.NotNull(warning); warned = true;
                        warning.ButtonsPanel.Children.OfType<Button>().Single(button => !button.IsCancel)
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; warning?.Close(); }
                }));
                dialog.Close(); DrainDispatcher();
                Assert.Null(failure); Assert.True(warned); Assert.False(dialog.IsVisible);
            }
            finally { dialog.WarnBeforeDiscardingConceptDrafts = false; dialog.Close(); }
        });
    }

    [Fact]
    public void Reset_all_warnings_reopens_the_disc_count_warning()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings();
            WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, true);
            WarningSuppressionService.ResetAll(settings);
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.Mp3Disc, defaultDiscCount: 1,
                confirmDiscCountOnStart: true,
                shouldWarnDiscCount: () => !WarningSuppressionService.IsSuppressed(settings, WarningSuppressionService.DiscCount));
            Exception? failure = null; var warned = false;
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
            {
                setup.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = setup.OwnedWindows.OfType<DiscCountConfirmationDialog>().SingleOrDefault();
                    try { Assert.NotNull(warning); warned = true; warning.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    catch (Exception error) { failure = error; warning?.Close(); }
                }));
                setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                setup.Close();
            }));
            Assert.NotEqual(true, setup.ShowDialog()); DrainDispatcher();
            Assert.Null(failure); Assert.True(warned);
        });
    }

}
