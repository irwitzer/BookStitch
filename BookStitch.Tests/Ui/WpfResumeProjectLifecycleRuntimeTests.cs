using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Closing_project_browser_during_loading_prevents_late_publication_and_refresh()
    {
        RunOnStaThread(() =>
        {
            using var root = new TemporaryFolder();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var dialog = new ResumeProjectDialog(root.Path, new ProjectIndexService(), new ProjectResumePlanService(), 180, AppearanceMode.Dark);
            try
            {
                dialog.Show();
                var load = typeof(ResumeProjectDialog).GetMethod("LoadProjectsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task? duplicate = null;
                dialog.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(dialog.CanRefreshProjects) && !dialog.CanRefreshProjects && duplicate is null)
                        duplicate = Assert.IsAssignableFrom<Task>(load.Invoke(dialog, [null, false]));
                };
                var task = Assert.IsAssignableFrom<Task>(load.Invoke(dialog, [null, false]));
                Assert.NotNull(duplicate);
                Assert.True(duplicate.IsCompletedSuccessfully);
                dialog.Close();
                var headerAtClose = dialog.ProjectHeaderTitle;
                var countAtClose = dialog.Projects.Count;
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(10) };
                var deadline = DateTime.UtcNow.AddSeconds(5);
                timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
                timer.Start();
                try { Dispatcher.PushFrame(frame); }
                finally { timer.Stop(); }
                Assert.True(task.IsCompleted, "Closed browser load did not finish.");
                task.GetAwaiter().GetResult();
                Assert.Equal(headerAtClose, dialog.ProjectHeaderTitle);
                Assert.Equal(countAtClose, dialog.Projects.Count);
                Assert.False(dialog.CanRefreshProjects);
                Assert.True(Assert.IsAssignableFrom<Task>(load.Invoke(dialog, [null, false])).IsCompletedSuccessfully);
                var showWarnings = typeof(ResumeProjectDialog).GetMethod("ShowDamagedProjectWarnings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                showWarnings.Invoke(dialog, [new List<DamagedProjectInfo> { new(root.Path, "Unknown", "Late failure", "", "Unreadable") }]);
                Assert.Empty(dialog.OwnedWindows.Cast<System.Windows.Window>());
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
    }

    [Fact]
    public void Bad_cover_and_bad_project_selection_do_not_prevent_selecting_a_healthy_project()
    {
        RunOnStaThread(() =>
        {
            using var root = new TemporaryFolder();
            var source = root.CreateFile("track.mp3");
            File.WriteAllText(source, "isolated source fixture");
            var cover = root.CreateFile("bad.jpg");
            File.WriteAllText(cover, "not an image");
            var healthy = Path.Combine(root.Path, "Projects", "LocalProjects", "Healthy");
            var manifest = ProjectFolderLayout.GetWorkManifestPath(healthy);
            Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
            File.WriteAllText(manifest, JsonSerializer.Serialize(new ExportWorkManifest
            {
                ProjectType = ProjectManifestTypes.FolderProject,
                ProjectWorkFolder = healthy,
                State = new ExportManifestState { Status = ProjectManifestStatuses.Completed },
                Metadata = new ExportManifestBookMetadata { Title = "Healthy", ProcessedCoverPath = cover },
                Tracks = [new ExportWorkManifestTrack { TrackIndex = 1, SourcePath = source, DurationTicks = TimeSpan.FromSeconds(1).Ticks }]
            }));
            var broken = Path.Combine(root.Path, "Projects", "LocalProjects", "Broken");
            var brokenManifest = ProjectFolderLayout.GetWorkManifestPath(broken);
            Directory.CreateDirectory(Path.GetDirectoryName(brokenManifest)!);
            File.WriteAllText(brokenManifest, "{\"schemaVersion\":2147483647,\"ProjectType\":\"Folder\"}");
            var dialog = new ResumeProjectDialog(root.Path, new ProjectIndexService(), new ProjectResumePlanService(), 180, AppearanceMode.Dark);
            try
            {
                var healthyItem = Assert.Single(new ProjectIndexService().ScanProjects(healthy));
                dialog.SelectedProject = healthyItem;
                WaitForBrowserDetails(dialog);
                Assert.True(dialog.CanLoadSelectedProject);
                Assert.NotNull(dialog.ProjectCoverSource);
                dialog.SelectedProject = new ProjectIndexItem { ProjectFolder = broken, DisplayName = "Broken" };
                Assert.False(dialog.CanLoadSelectedProject);
                WaitForBrowserDetails(dialog);
                Assert.False(dialog.CanLoadSelectedProject);
                dialog.SelectedProject = healthyItem;
                WaitForBrowserDetails(dialog);
                Assert.True(dialog.CanLoadSelectedProject);
                Assert.Null(dialog.SelectedResumePlan);
                Assert.Contains("Healthy", dialog.ProjectHeaderTitle);
                var gate = (SemaphoreSlim)typeof(ResumeProjectDialog).GetField("_projectDetailsGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Assert.True(gate.Wait(0));
                try
                {
                    dialog.SelectedProject = new ProjectIndexItem { ProjectFolder = broken, DisplayName = "Broken" };
                    dialog.SelectedProject = healthyItem;
                    Assert.False(dialog.CanLoadSelectedProject);
                    Assert.Equal(System.Windows.Visibility.Hidden, dialog.ProjectCoverVisibility);
                    Assert.Empty(dialog.ProjectLocationDetails);
                    var dispatcherResponded = false;
                    dialog.Dispatcher.BeginInvoke(() => dispatcherResponded = true);
                    DrainDispatcher();
                    Assert.True(dispatcherResponded);
                }
                finally { gate.Release(); }
                WaitForBrowserDetails(dialog);
                Assert.Null(dialog.SelectedResumePlan);
                Assert.Contains("Healthy", dialog.ProjectHeaderTitle);
            }
            finally { dialog.Close(); }
        });
    }

    private static void WaitForBrowserDetails(ResumeProjectDialog dialog)
    {
        var task = (Task)typeof(ResumeProjectDialog).GetField("_projectDetailsTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(task.IsCompleted, "Project details did not finish.");
        task.GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_button_builds_a_fresh_plan_and_reports_state_damaged_after_preview(bool damageAfterPreview)
    {
        RunOnStaThread(() =>
        {
            using var root = new TemporaryFolder();
            var source = root.CreateFile("track.mp3");
            var project = Path.Combine(root.Path, "Projects", "LocalProjects", "Book");
            var manifestPath = ProjectFolderLayout.GetWorkManifestPath(project);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new ExportWorkManifest
            {
                ProjectWorkFolder = project,
                State = new ExportManifestState { Status = ProjectManifestStatuses.Completed },
                Metadata = new ExportManifestBookMetadata { Title = "Fresh" },
                Tracks = [new ExportWorkManifestTrack { TrackIndex = 1, SourcePath = source,
                    DurationTicks = TimeSpan.FromSeconds(1).Ticks }]
            }));
            var dialog = new ResumeProjectDialog(root.Path, new ProjectIndexService(), new ProjectResumePlanService(), 180, AppearanceMode.Dark);
            Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    timer.Tick += (_, _) =>
                    {
                        if ((dialog.HasProjects && dialog.CanRefreshProjects) || DateTime.UtcNow >= deadline)
                            frame.Continue = false;
                    };
                    timer.Start();
                    try { Dispatcher.PushFrame(frame); }
                    finally { timer.Stop(); }
                    WaitForBrowserDetails(dialog);
                    Assert.True(dialog.CanLoadSelectedProject);
                    Assert.Null(dialog.SelectedResumePlan);
                    if (damageAfterPreview) File.WriteAllText(manifestPath, "{ damaged after preview");
                    var load = typeof(ResumeProjectDialog).GetMethod("LoadSelectedProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var task = Assert.IsAssignableFrom<Task>(load.Invoke(dialog, null));
                    Assert.False(dialog.CanRefreshProjects);
                    Assert.False(dialog.CanSelectProjects);
                    Assert.False(dialog.HasSelectedProject);
                    await task;
                    if (damageAfterPreview)
                    {
                        Assert.Null(dialog.SelectedResumePlan);
                        Assert.True(dialog.IsVisible);
                        Assert.True(dialog.CanSelectProjects);
                        Assert.Contains(LocalizationText.Get("Project.Browser.ProjectUnreadable"), dialog.ProjectDetails);
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            var result = dialog.ShowDialog();
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (!damageAfterPreview)
            {
                Assert.True(result);
                Assert.Equal(project, dialog.SelectedResumePlan!.ProjectFolder);
                Assert.Single(dialog.SelectedResumePlan.Tracks);
            }
        });
    }
}
