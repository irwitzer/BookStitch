using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Packaging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public partial class ResumeProjectDialog : Window, INotifyPropertyChanged
{
    private readonly string _workingRootFolder;
    private readonly GlobalSettingsManager? _settingsManager;
    private readonly Func<Window,string,bool,Task<ProjectOperationOutcome>>? _rebuild;
    private readonly ProjectIndexService _projectIndexService;
    private readonly ProjectCatalog _catalog;
    private readonly ProjectResumePlanService _projectResumePlanService;
    private readonly ProjectBrowserPresentationService _projectBrowserPresentationService;
    private readonly int _projectRetentionDays;
    private readonly string _projectRetentionMode;
    private readonly AppearanceMode _appearanceMode;
    private ProjectIndexItem? _selectedProject;
    private ProjectResumePlan? _selectedResumePlan;
    private readonly HashSet<string> _reportedDamagedProjectFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _isShowingDamagedProjectWarning;
    private int _damagedProjectScanVersion;
    private int _projectLoadVersion;
    private long _displayedCatalogGeneration = -1;
    private int _projectDetailsVersion;
    private Task _projectDetailsTask = Task.CompletedTask;
    private readonly SemaphoreSlim _projectDetailsGate = new(1, 1);
    private volatile bool _closed;
    private bool _isLoadingProjects;
    private bool _isOpeningProject;
    private bool _previewCanLoad;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _initialProjectLoadMeasured;
    private bool _initialSelectionPreviewMeasured;
    private string _projectDetails = LocalizationText.Get("Resume.Project.NoneDetails");
    private string _projectLocationDetails = string.Empty;
    private bool _coverIsPlaceholder = true;
    private ImageSource _projectCoverSource = null!;
    private Visibility _projectCoverVisibility = Visibility.Visible;
    private string _projectHeaderTitle = LocalizationText.Get("Project.Browser.NoProjectSelected");

    public ResumeProjectDialog(
        string workingRootFolder,
        ProjectIndexService projectIndexService,
        ProjectResumePlanService projectResumePlanService,
        int projectRetentionDays,
        AppearanceMode appearanceMode,
        ProjectIndexItem? selectedProject = null,
        string? projectRetentionMode = null,
        GlobalSettingsManager? settingsManager = null,
        Func<Window,string,bool,Task<ProjectOperationOutcome>>? rebuildProject = null)
    {
        InitializeComponent();
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        Loaded += (_, _) => PreviewAppearanceChanged(this, EventArgs.Empty);
        _settingsManager=settingsManager;_rebuild=rebuildProject;

        _workingRootFolder = workingRootFolder;
        _projectIndexService = projectIndexService;
        _catalog = projectIndexService.Catalog;
        _catalog.SnapshotChanged += Catalog_SnapshotChanged;
        _projectResumePlanService = projectResumePlanService;
        _projectBrowserPresentationService = new ProjectBrowserPresentationService(projectIndexService);
        _projectRetentionDays = ProjectIndexService.NormalizeRetentionDays(projectRetentionDays);
        _projectRetentionMode = ProjectRetentionPolicyService.NormalizeMode(projectRetentionMode);
        _appearanceMode = appearanceMode;
        _projectCoverSource = LoadProjectCoverImage();

        DataContext = this;
        ProjectHeaderTitle = LocalizationText.Get("Resume.Project.LoadingTitle");
        ProjectDetails = LocalizationText.Get("Resume.Project.LoadingDetails");
        ProjectLocationDetails = string.Empty;

        var preferredProjectFolder = selectedProject?.ProjectFolder;
        Loaded += (_, _) =>
        {
            if (_closed) return;
            LoadProjectButton.Focus();
            Dispatcher.BeginInvoke(
                new Action(() => _ = LoadProjectsAsync(preferredProjectFolder)),
                DispatcherPriority.Background);
        };
    }

    public ObservableCollection<ProjectIndexItem> Projects { get; } = [];

    public TimeSpan InitialProjectLoadElapsed { get; private set; }

    public TimeSpan InitialProjectScanElapsed { get; private set; }

    public TimeSpan InitialSelectionPreviewElapsed { get; private set; }

    public int InitialProjectCount { get; private set; }

    public bool HasProjects => Projects.Count > 0;
    public bool CanRefreshProjects => !_closed && !_isLoadingProjects && !_isOpeningProject;
    public bool CanSelectProjects => !_closed && HasProjects && !_isOpeningProject;

    protected override void OnClosed(EventArgs e)
    {
        WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(DeveloperToolsCommands.AppearancePreview,
            nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        _closed = true;
        _catalog.SnapshotChanged -= Catalog_SnapshotChanged;
        ++_projectLoadVersion;
        ++_projectDetailsVersion;
        ++_damagedProjectScanVersion;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }

    public ProjectIndexItem? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (ReferenceEquals(_selectedProject, value))
                return;

            _selectedProject = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProject));
            OnPropertyChanged(nameof(ProjectHeaderTitle));
            _projectDetailsTask = UpdateProjectDetailsAsync();
        }
    }

    public ProjectResumePlan? SelectedResumePlan
    {
        get => _selectedResumePlan;
        private set
        {
            if (ReferenceEquals(_selectedResumePlan, value))
                return;

            _selectedResumePlan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanLoadSelectedProject));
        }
    }

    public bool HasSelectedProject => SelectedProject is not null && !_isOpeningProject;

    public bool CanLoadSelectedProject => !_closed && !_isOpeningProject && _previewCanLoad;

    public ImageSource ProjectCoverSource
    {
        get => _projectCoverSource;
        private set
        {
            if (ReferenceEquals(_projectCoverSource, value))
                return;

            _projectCoverSource = value;
            OnPropertyChanged();
        }
    }

    public Visibility ProjectCoverVisibility
    {
        get => _projectCoverVisibility;
        private set
        {
            if (_projectCoverVisibility == value)
                return;

            _projectCoverVisibility = value;
            OnPropertyChanged();
        }
    }


    public string ProjectHeaderTitle
    {
        get => _projectHeaderTitle;
        private set
        {
            if (string.Equals(_projectHeaderTitle, value, StringComparison.Ordinal))
                return;

            _projectHeaderTitle = value;
            OnPropertyChanged();
        }
    }

    public string ProjectDetails
    {
        get => _projectDetails;
        set
        {
            if (_projectDetails == value)
                return;

            _projectDetails = value;
            OnPropertyChanged();
        }
    }

    public string ProjectLocationDetails
    {
        get => _projectLocationDetails;
        private set
        {
            if (_projectLocationDetails == value)
                return;

            _projectLocationDetails = value;
            OnPropertyChanged();
        }
    }


    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ProjectsComboBox.IsDropDownOpen)
            return;

        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown))
            return;

        var delta = e.Key is Key.Up or Key.PageUp ? -1 : 1;
        MoveProjectSelection(delta);
        e.Handled = true;
    }

    private void MoveProjectSelection(int delta)
    {
        if (!CanSelectProjects)
            return;

        var currentIndex = ProjectsComboBox.SelectedIndex;
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = Math.Clamp(currentIndex + delta, 0, Projects.Count - 1);
        ProjectsComboBox.SelectedIndex = nextIndex;
    }

    private void ProjectsComboBox_DropDownOpened(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ProjectsComboBox.ApplyTemplate();
            if (ProjectsComboBox.Template.FindName("PART_Popup", ProjectsComboBox) is not Popup popup || popup.Child is null)
                return;

            if (FindVisualChild<ScrollViewer>(popup.Child) is { } scrollViewer)
                scrollViewer.ScrollToTop();
        }));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadProjectsAsync(SelectedProject?.ProjectFolder, forceRefresh: true);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e) => await LoadSelectedProjectAsync();

    private async Task LoadSelectedProjectAsync()
    {
        if (!CanLoadSelectedProject || SelectedProject is not { } project)
            return;
        if(project.HealthStatus is ProjectHealthStatus.RebuildRequired or ProjectHealthStatus.Damaged)
        {
            var result=AppDialogService.Show(this,LocalizationText.Get("Integrity.Title"),project.HealthMessage,
                LocalizationText.Get(project.HealthStatus==ProjectHealthStatus.Damaged?"Integrity.LoadDamagedWarning":"Integrity.LoadRebuildWarning"),AppDialogKind.Warning,
                buttons:[new AppDialogButton(LocalizationText.Get("Integrity.LoadAnyway"),AppDialogResult.Yes,IsPrimary:true),
                    new AppDialogButton(LocalizationText.Get("Common.Cancel"),AppDialogResult.Cancel)]);
            if(result!=AppDialogResult.Yes)return;
        }
        var version = _projectDetailsVersion;
        var token = _lifetimeCancellation.Token;
        _isOpeningProject = true;
        NotifyProjectActions();
        try
        {
            var plan = await Task.Run(() => _projectResumePlanService.BuildFromProjectFolder(project.ProjectFolder), token);
            if (_closed || token.IsCancellationRequested || version != _projectDetailsVersion)
                return;
            if (plan is null || (plan.Tracks.Count == 0 && !plan.CanContinueDiscImport))
                throw new InvalidDataException(LocalizationText.Get("Project.Browser.ProjectUnreadable"));
            SelectedResumePlan = plan;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            if (!_closed && !token.IsCancellationRequested && version == _projectDetailsVersion)
            {
                ProjectHeaderTitle = LocalizationText.Get("Settings.Projects.LoadFailed.Title");
                ProjectDetails = LocalizationText.Get("Project.Browser.ProjectUnreadable");
            (Application.Current as App)?.DiagnosticLogService?.WriteError("PROJECT PREVIEW",ex);
            }
        }
        finally
        {
            _isOpeningProject = false;
            if (!_closed) NotifyProjectActions();
        }
    }

    private void NotifyProjectActions()
    {
        OnPropertyChanged(nameof(CanLoadSelectedProject));
        OnPropertyChanged(nameof(CanRefreshProjects));
        OnPropertyChanged(nameof(CanSelectProjects));
        OnPropertyChanged(nameof(HasSelectedProject));
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
            return;

        var confirmed = AppDialogService.Confirm(
            this,
            LocalizationText.Get("Resume.Project.DeleteHeading"),
            LocalizationText.Format("Resume.Project.DeleteMessage", project.DisplayName),
            [
                LocalizationText.Get("Settings.Projects.Delete.Irreversible"),
                LocalizationText.Format("Resume.Project.Folder", project.ProjectFolder)
            ],
            LocalizationText.Get("Settings.Projects.DeleteOne.Title"));

        if (!confirmed)
            return;

        var result = _projectIndexService.DeleteProject(_workingRootFolder, project.ProjectFolder);
        if (!result.Deleted)
        {
            AppDialogService.Error(
                this,
                LocalizationText.Get("Settings.Projects.DeleteOne.FailedTitle"),
                result.ErrorMessage ?? LocalizationText.Get("Settings.Projects.DeleteOne.FailedMessage"),
                [project.ProjectFolder],
                LocalizationText.Get("Settings.Projects.DeleteOne.Title"));
            return;
        }

        _ = LoadProjectsAsync(preferredProjectFolder: null);
    }

    private void Catalog_SnapshotChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var snapshot = _catalog?.Snapshot;
            if (_closed || _isLoadingProjects || _isOpeningProject || snapshot is null || snapshot.IsDirty ||
                snapshot.Generation == _displayedCatalogGeneration ||
                !string.Equals(snapshot.WorkingRoot, System.IO.Path.GetFullPath(_workingRootFolder), StringComparison.OrdinalIgnoreCase)) return;
            _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
        }));
    }

    private async Task LoadProjectsAsync(string? preferredProjectFolder, bool forceRefresh = false)
    {
        if (_closed || _isLoadingProjects || _isOpeningProject)
            return;
        _isLoadingProjects = true;
        OnPropertyChanged(nameof(CanRefreshProjects));
        var token = _lifetimeCancellation.Token;
        try
        {
            var measureInitialLoad = !_initialProjectLoadMeasured;
            var initialLoadTimer = measureInitialLoad ? Stopwatch.StartNew() : null;
            var scanTimer = Stopwatch.StartNew();
            var loadVersion = ++_projectLoadVersion;
            ++_damagedProjectScanVersion;
            ProjectCatalogSnapshot? catalogSnapshot = null;
            SelectedProject = null;
            SelectedResumePlan = null;
            Projects.Clear();
            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(CanSelectProjects));
            ProjectHeaderTitle = LocalizationText.Get("Resume.Project.LoadingTitle");
            ProjectDetails = LocalizationText.Get("Resume.Project.LoadingDetails");
            ProjectLocationDetails = string.Empty;

            IReadOnlyList<ProjectIndexItem> projects;
            try
            {
                catalogSnapshot = await _catalog.RefreshAsync(_workingRootFolder, _projectRetentionDays, forceRefresh).WaitAsync(token);
                while (!token.IsCancellationRequested &&
                    (_catalog.Snapshot?.Generation != catalogSnapshot.Generation || _catalog.Snapshot.IsDirty))
                    catalogSnapshot = await _catalog.RefreshAsync(_workingRootFolder, _projectRetentionDays).WaitAsync(token);
                projects = catalogSnapshot.ProjectIndexItems.Where(project => project.IsVisibleInProjectBrowser).ToArray();
                scanTimer.Stop();

                if (measureInitialLoad)
                    InitialProjectScanElapsed = scanTimer.Elapsed;
            }
            catch (Exception ex)
            {
                if (_closed || token.IsCancellationRequested || loadVersion != _projectLoadVersion)
                    return;

                ProjectHeaderTitle = LocalizationText.Get("Settings.Projects.LoadFailed.Title");
                ProjectDetails = LocalizationText.Format("Settings.Projects.LoadFailed.Message", ex.Message);
                ProjectLocationDetails = string.Empty;
                return;
            }

            if (_closed || token.IsCancellationRequested || loadVersion != _projectLoadVersion)
                return;

            _displayedCatalogGeneration = catalogSnapshot!.Generation;
            foreach (var project in projects)
                Projects.Add(project);

            OnPropertyChanged(nameof(HasProjects));
            OnPropertyChanged(nameof(CanSelectProjects));

            SelectedProject = Projects.FirstOrDefault(project =>
                !string.IsNullOrWhiteSpace(preferredProjectFolder) &&
                string.Equals(project.ProjectFolder, preferredProjectFolder, StringComparison.OrdinalIgnoreCase))
                ?? Projects.FirstOrDefault();

            await _projectDetailsTask;
            if (_closed || token.IsCancellationRequested || loadVersion != _projectLoadVersion)
                return;

            if (SelectedProject is null)
            {
                ProjectHeaderTitle = LocalizationText.Get("Project.Browser.NoProjectSelected");
                ProjectDetails = LocalizationText.Get("Project.Browser.NoCompleteProjects");
                ProjectLocationDetails = string.Empty;
            }

            if (measureInitialLoad && initialLoadTimer is not null)
            {
                initialLoadTimer.Stop();
                InitialProjectLoadElapsed = initialLoadTimer.Elapsed;
                InitialProjectCount = Projects.Count;
                _initialProjectLoadMeasured = true;
            }

            QueueDamagedProjectScan(catalogSnapshot!.Generation);
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                SelectedResumePlan = null;
                ProjectHeaderTitle = LocalizationText.Get("Settings.Projects.LoadFailed.Title");
                ProjectDetails = LocalizationText.Format("Settings.Projects.LoadFailed.Message", ex.Message);
            }
        }
        finally
        {
            _isLoadingProjects = false;
            if (!_closed)
                OnPropertyChanged(nameof(CanRefreshProjects));
        }
    }

    private async void QueueDamagedProjectScan(long generation)
    {
        if (_closed) return;
        var token = _lifetimeCancellation.Token;
        var scanVersion = ++_damagedProjectScanVersion;
        try
        {
            var snapshot = await _catalog.WaitForDamageAsync(generation, token);
            if (_closed || token.IsCancellationRequested || !IsVisible ||
                scanVersion != _damagedProjectScanVersion || snapshot is null ||
                _catalog.Snapshot?.Generation != generation) return;
            var damaged = snapshot.DamageInformation
                .Where(project => !_reportedDamagedProjectFolders.Contains(project.ProjectFolder) &&
                    !snapshot.ProjectIndexItems.Any(item => item.ProjectFolder == project.ProjectFolder && item.HealthStatus == ProjectHealthStatus.Damaged)).ToArray();
            if (damaged.Length > 0) ShowDamagedProjectWarnings(damaged);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Catalog damage preview failed: {0}", error.Message); }
    }

    private void ShowDamagedProjectWarnings(IReadOnlyList<DamagedProjectInfo> damagedProjects)
    {
        if (_closed || !IsVisible || _isShowingDamagedProjectWarning || damagedProjects.Count == 0)
            return;

        _isShowingDamagedProjectWarning = true;
        try
        {
            foreach (var damagedProject in damagedProjects)
            {
                if (_closed) break;
                if (!Directory.Exists(damagedProject.ProjectFolder))
                    continue;

                _reportedDamagedProjectFolders.Add(damagedProject.ProjectFolder);

                var result = AppDialogService.Show(
                    this,
                    LocalizationText.Get("Resume.Project.DamagedTitle"),
                    LocalizationText.Get("Resume.Project.DamagedHeading"),
                    LocalizationText.Get("Resume.Project.DamagedMessage"),
                    AppDialogKind.Warning,
                    [
                        LocalizationText.Format("Resume.Project.Name", damagedProject.DisplayName),
                        damagedProject.Reason,
                        LocalizationText.Format("Resume.Project.Folder", damagedProject.ProjectFolder)
                    ],
                    [
                        new AppDialogButton(LocalizationText.Get("Resume.Project.OpenFolder"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                        new AppDialogButton(LocalizationText.Get("Settings.Projects.DeleteOne.Title"), AppDialogResult.No, IsDanger: true),
                        new AppDialogButton(LocalizationText.Get("Common.Close"), AppDialogResult.Cancel, IsCancel: true)
                    ],
                    width: 660);

                if (result == AppDialogResult.Yes)
                {
                    OpenProjectFolder(damagedProject.ProjectFolder);
                    break;
                }

                if (result != AppDialogResult.No)
                    break;

                var confirmed = AppDialogService.Confirm(
                    this,
                    LocalizationText.Get("Resume.Project.DeleteDamagedHeading"),
                    LocalizationText.Format("Resume.Project.DeleteDamagedMessage", damagedProject.DisplayName),
                    [
                        LocalizationText.Get("Settings.Projects.Delete.Irreversible"),
                        LocalizationText.Format("Resume.Project.Folder", damagedProject.ProjectFolder)
                    ],
                    LocalizationText.Get("Settings.Projects.DeleteOne.Title"));

                if (!confirmed)
                    continue;

                var deletionResult = _projectIndexService.DeleteProject(_workingRootFolder, damagedProject.ProjectFolder);
                if (!deletionResult.Deleted)
                {
                    AppDialogService.Error(
                        this,
                        LocalizationText.Get("Settings.Projects.DeleteOne.FailedTitle"),
                        deletionResult.ErrorMessage ?? LocalizationText.Get("Settings.Projects.DeleteOne.FailedMessage"),
                        [damagedProject.ProjectFolder],
                        LocalizationText.Get("Settings.Projects.DeleteOne.Title"));
                }
            }
        }
        finally
        {
            _isShowingDamagedProjectWarning = false;
        }
    }

    private void OpenSelectedProjectFolder_Click(object sender, RoutedEventArgs e)
    { if (SelectedProject is { } project) OpenProjectFolder(project.ProjectFolder); }

    private void OpenProjectFolder(string projectFolder) => ProjectFolderOpenService.Open(this,projectFolder,_settingsManager);

    private async void RebuildProject_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedProject?.CanRebuild!=true || _rebuild is null)return;
        var folder=SelectedProject.ProjectFolder;
        if(await _rebuild(this,folder,false)!=ProjectOperationOutcome.Completed || _closed)return;
        _catalog.MarkDirty();
        await LoadProjectsAsync(folder);
    }

    private async Task UpdateProjectDetailsAsync()
    {
        if (_closed) return;
        var project = SelectedProject;
        var version = ++_projectDetailsVersion;
        var token = _lifetimeCancellation.Token;
        SelectedResumePlan = null;
        _previewCanLoad = false;
        OnPropertyChanged(nameof(CanLoadSelectedProject));
        ProjectHeaderTitle = LocalizationText.Get("Resume.Project.LoadingTitle");
        ProjectDetails = LocalizationText.Get("Resume.Project.LoadingDetails");
        ProjectLocationDetails = string.Empty;
        ProjectCoverVisibility = Visibility.Hidden;
        try
        {
            var measureInitialPreview = project is not null && !_initialSelectionPreviewMeasured && !_initialProjectLoadMeasured;
            var previewTimer = measureInitialPreview ? Stopwatch.StartNew() : null;
            var presentation = await Task.Run<(ProjectBrowserPresentation Presentation, ImageSource Cover)?>(async () =>
            {
                await _projectDetailsGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (_closed || version != Volatile.Read(ref _projectDetailsVersion))
                        return null;
                    var presentation = _projectBrowserPresentationService.Create(project, _projectRetentionMode, _appearanceMode);
                    return (Presentation: presentation, Cover: LoadProjectCoverImage(presentation.CoverUri));
                }
                finally { _projectDetailsGate.Release(); }
            }, token);
            if (_closed || token.IsCancellationRequested || version != _projectDetailsVersion || presentation is null)
                return;
            if (measureInitialPreview && previewTimer is not null)
            {
                previewTimer.Stop();
                InitialSelectionPreviewElapsed = previewTimer.Elapsed;
                _initialSelectionPreviewMeasured = true;
            }

            ProjectHeaderTitle = presentation.Value.Presentation.HeaderTitle;
            ProjectDetails = presentation.Value.Presentation.Details;
            ProjectLocationDetails = presentation.Value.Presentation.LocationDetails;
            _coverIsPlaceholder = !presentation.Value.Presentation.CoverUri.IsFile;
            ProjectCoverSource = presentation.Value.Cover;
            PreviewAppearanceChanged(this, EventArgs.Empty);
            ProjectCoverVisibility = Visibility.Visible;
            _previewCanLoad = presentation.Value.Presentation.CanLoad;
            OnPropertyChanged(nameof(CanLoadSelectedProject));
        }
        catch (Exception ex)
        {
            if (_closed || token.IsCancellationRequested || version != _projectDetailsVersion)
                return;
            SelectedResumePlan = null;
            ProjectHeaderTitle = LocalizationText.Get("Settings.Projects.LoadFailed.Title");
            ProjectDetails = LocalizationText.Get("Project.Browser.ProjectUnreadable");
            (Application.Current as App)?.DiagnosticLogService?.WriteError("PROJECT PREVIEW",ex);
            ProjectLocationDetails = SelectedProject?.ProjectFolder ?? string.Empty;
        }
    }

    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess() || _closed || !_coverIsPlaceholder) return;
        if (TryFindResource(AppearanceSettingsService.CoverPlaceholderImageSourceKey) is ImageSource placeholder)
            ProjectCoverSource = placeholder;
    }

    private ImageSource LoadProjectCoverImage(string? filePath = null) =>
        LoadProjectCoverImage(ResolveProjectCoverImageUri(_appearanceMode, filePath));

    private ImageSource LoadProjectCoverImage(Uri uri)
    {
        try { return LoadProjectCoverImageCore(uri); }
        catch (Exception) when (uri.IsFile)
        {
            return LoadProjectCoverImageCore(ResolveProjectCoverImageUri(_appearanceMode));
        }
    }

    private static ImageSource LoadProjectCoverImageCore(Uri uri) =>
        ProjectCoverThumbnailCache.Shared.Load(uri);

    internal static Uri ResolveProjectCoverImageUri(
        AppearanceMode appearanceMode,
        string? filePath = null) =>
        ProjectBrowserPresentationService.ResolveProjectCoverImageUri(appearanceMode, filePath);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
