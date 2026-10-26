using System.IO;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BookStitch.Controls;

public partial class SettingsProjectManagementControl : UserControl, INotifyPropertyChanged
{
    private GlobalSettingsManager? _settingsManager;
    private Func<int, ProjectCleanupResult>? _deleteOldProjects;
    private ProjectBrowserPresentationService? _projectBrowser;
    private ProjectCatalog? _catalog;
    private bool _closed;
    private string _workingRootFolder = string.Empty;
    private AppearanceMode _appearanceMode;
    private ProjectIndexItem? _selectedProject;
    private string _projectHeaderTitle = LocalizationText.Get("Settings.Projects.NoneSelected");
    private string _projectDetails = LocalizationText.Get("Settings.Projects.NoneFound");
    private string _projectLocationDetails = string.Empty;
    private ImageSource? _projectCoverSource;
    private bool _isLoading = true;
    private int _loadVersion;
    private long _displayedCatalogGeneration = -1;

    public SettingsProjectManagementControl()
    {
        InitializeComponent();
        Loaded += (_, _) => WeakEventManager<DeveloperAppearancePreview, EventArgs>.AddHandler(
            DeveloperToolsCommands.AppearancePreview, nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        Unloaded += (_, _) => WeakEventManager<DeveloperAppearancePreview, EventArgs>.RemoveHandler(
            DeveloperToolsCommands.AppearancePreview, nameof(DeveloperAppearancePreview.Changed), PreviewAppearanceChanged);
        DataContext = this;
    }

    public ObservableCollection<ProjectIndexItem> Projects { get; } = [];
    public bool HasProjects => Projects.Count > 0;
    public bool HasSelectedProject => SelectedProject is not null;

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
            UpdateProjectPresentation();
        }
    }

    public string ProjectHeaderTitle { get => _projectHeaderTitle; private set => SetField(ref _projectHeaderTitle, value); }
    public string ProjectDetails { get => _projectDetails; private set => SetField(ref _projectDetails, value); }
    public string ProjectLocationDetails { get => _projectLocationDetails; private set => SetField(ref _projectLocationDetails, value); }
    public ImageSource? ProjectCoverSource { get => _projectCoverSource; private set => SetField(ref _projectCoverSource, value); }

    public void Initialize(
        GlobalSettingsManager settingsManager,
        Func<int, ProjectCleanupResult> deleteOldProjects,
        string workingRootFolder,
        ProjectIndexService projectIndexService,
        ProjectResumePlanService projectResumePlanService)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _deleteOldProjects = deleteOldProjects ?? throw new ArgumentNullException(nameof(deleteOldProjects));
        _workingRootFolder = workingRootFolder ?? string.Empty;
        _projectBrowser = new ProjectBrowserPresentationService(projectIndexService);
        _catalog = projectIndexService.Catalog;
        _catalog.SnapshotChanged += Catalog_SnapshotChanged;
        Unloaded += (_, _) => { _closed = true; ++_loadVersion; _catalog.SnapshotChanged -= Catalog_SnapshotChanged; };
        Loaded += (_, _) => { _closed = false; _catalog.SnapshotChanged -= Catalog_SnapshotChanged; _catalog.SnapshotChanged += Catalog_SnapshotChanged; };
        _appearanceMode = AppearanceSettingsService.GetEffectiveMode(Settings);
        ApplyRetentionSelectionToUi();
        UpdateProjectRetentionHint();
        _isLoading = false;
        _ = LoadProjectsAsync(preferredProjectFolder: null);
    }

    private Func<Window,string,bool,Task<ProjectOperationOutcome>>? _rebuild;
    private Action<Window, bool>? _checkIntegrity;
    public void ConfigureIntegrity(Action<Window, bool>? action,Func<Window,string,bool,Task<ProjectOperationOutcome>>? rebuild=null)
    {
        _rebuild=rebuild;
        _checkIntegrity = action;
        IntegrityCheckButton.IsEnabled = action is not null;
        RefreshIntegritySummary();
    }
    private void RefreshIntegritySummary()
    {
        try { IntegritySummaryText.Text = ProjectIntegrityPresentation.Format(new ProjectFormatSweepService(
            Path.Combine(SettingsService.GetSettingsFolder(_workingRootFolder), ProjectFormatSweepService.StateFileName),
            ProjectIndexService.DiscoverProjectFolders).ReadState()?.LastRun); }
        catch (Exception) { IntegritySummaryText.Text = LocalizationText.Get("Integrity.NotChecked"); }
    }
    private async void RebuildProject_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedProject?.CanRebuild!=true || _rebuild is null || Window.GetWindow(this) is not { } owner)return;
        var folder=SelectedProject.ProjectFolder;
        if(await _rebuild(owner,folder,FullContentHashesCheckBox.IsChecked==true)!=ProjectOperationOutcome.Completed || _closed)return;
        _catalog?.MarkDirty();await LoadProjectsAsync(folder);
    }
    private void OpenSelectedProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project) return;
        if(Window.GetWindow(this) is { } owner)ProjectFolderOpenService.Open(owner,project.ProjectFolder,_settingsManager);
    }
    private void IntegrityCheck_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } owner || _checkIntegrity is null) return;
        _checkIntegrity(owner, FullContentHashesCheckBox.IsChecked == true);
        RefreshIntegritySummary();
        _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
    }

    private AppSettings Settings => _settingsManager?.Current
        ?? throw new InvalidOperationException("The project management settings control is not initialized.");

    public void CommitPendingEdits()
    {
        if (_settingsManager is not null)
            SaveProjectRetention(SettingsComboBoxSelection.GetText(ProjectRetentionDaysComboBox), updateText: true);
    }

    private void ProjectRetentionDaysComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            SaveProjectRetention(SettingsComboBoxSelection.GetText(ProjectRetentionDaysComboBox), updateText: false);
    }

    private void ProjectRetentionDaysComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
            SaveProjectRetention(SettingsComboBoxSelection.GetText(ProjectRetentionDaysComboBox), updateText: true);
    }

    private void SaveProjectRetention(string? text, bool updateText)
    {
        var normalizedText = (text ?? string.Empty).Trim();
        var isNever = string.Equals(normalizedText, "Nie", StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(normalizedText, LocalizationText.Get("UiNie"), StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(normalizedText, ProjectRetentionModes.Never, StringComparison.OrdinalIgnoreCase);

        var changed = false;
        if (isNever)
        {
            if (!ProjectRetentionPolicyService.IsNever(Settings.ProjectRetentionMode))
            {
                Settings.ProjectRetentionMode = ProjectRetentionModes.Never;
                changed = true;
            }
        }
        else
        {
            if (!int.TryParse(normalizedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                value = ProjectIndexService.DefaultRetentionDays;
            var retentionDays = ProjectIndexService.NormalizeRetentionDays(value);
            if (Settings.ProjectRetentionDays != retentionDays)
            {
                Settings.ProjectRetentionDays = retentionDays;
                changed = true;
            }
            if (ProjectRetentionPolicyService.IsNever(Settings.ProjectRetentionMode))
            {
                Settings.ProjectRetentionMode = ProjectRetentionModes.Days;
                changed = true;
            }
        }

        if (changed && _settingsManager!.SaveImmediate())
            _catalog?.MarkDirty();
        if (updateText)
            ApplyRetentionSelectionToUi();
        UpdateProjectRetentionHint();
        if (changed)
            _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
        else
            UpdateProjectPresentation();
    }

    private void ApplyRetentionSelectionToUi()
    {
        var isNever = ProjectRetentionPolicyService.IsNever(Settings.ProjectRetentionMode);
        ProjectRetentionDaysComboBox.Text = isNever
            ? LocalizationText.Get("UiNie")
            : ProjectIndexService.NormalizeRetentionDays(Settings.ProjectRetentionDays).ToString(CultureInfo.InvariantCulture);
        RetentionUnitTextBlock.Visibility = isNever ? Visibility.Collapsed : Visibility.Visible;
        DeleteOldProjectsButton.IsEnabled = !isNever;
    }

    private void UpdateProjectRetentionHint()
    {
        if (ProjectRetentionPolicyService.IsNever(Settings.ProjectRetentionMode))
        {
            ProjectRetentionHintTextBlock.Text = LocalizationText.Get("Settings.Projects.Retention.Never");
            RetentionUnitTextBlock.Visibility = Visibility.Collapsed;
            DeleteOldProjectsButton.IsEnabled = false;
            return;
        }

        var days = ProjectIndexService.NormalizeRetentionDays(Settings.ProjectRetentionDays);
        ProjectRetentionHintTextBlock.Text = days == 0
            ? LocalizationText.Get("Settings.Projects.Retention.All")
            : LocalizationText.Format("Settings.Projects.Retention.OlderThan", days);
        RetentionUnitTextBlock.Visibility = Visibility.Visible;
        DeleteOldProjectsButton.IsEnabled = true;
    }

    private void DeleteOldProjectsNow_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        if (ProjectRetentionPolicyService.IsNever(Settings.ProjectRetentionMode))
            return;
        ShowProjectCleanupResult(_deleteOldProjects!(Settings.ProjectRetentionDays), LocalizationText.Get("Settings.Projects.Delete.Title"), oldProjectsOnly: true);
        _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
    }

    private void DeleteAllProjects_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = AppDialogService.Confirm(
            Window.GetWindow(this),
            LocalizationText.Get("Settings.Projects.DeleteAll.Heading"),
            LocalizationText.Get("Settings.Projects.DeleteAll.Message"),
            [LocalizationText.Get("Settings.Projects.DeleteAll.Remains"), LocalizationText.Get("Settings.Projects.Delete.Irreversible")],
            LocalizationText.Get("Settings.Projects.DeleteAll.Title"));
        if (!confirmed)
            return;
        ShowProjectCleanupResult(_deleteOldProjects!(0), LocalizationText.Get("Settings.Projects.DeleteAll.Title"), oldProjectsOnly: false);
        _ = LoadProjectsAsync(preferredProjectFolder: null);
    }

    private void DeleteSelectedProject_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null || _projectBrowser is null)
            return;
        var confirmed = AppDialogService.Confirm(
            Window.GetWindow(this),
            LocalizationText.Get("Settings.Projects.DeleteOne.Heading"),
            project.DisplayName,
            [project.ProjectFolder, LocalizationText.Get("Settings.Projects.Delete.Irreversible")],
            LocalizationText.Get("Settings.Projects.DeleteOne.Title"));
        if (!confirmed)
            return;

        var previousIndex = Projects.IndexOf(project);
        var result = _projectBrowser.DeleteProject(_workingRootFolder, project.ProjectFolder);
        if (!result.Deleted)
        {
            AppDialogService.Error(
                Window.GetWindow(this),
                LocalizationText.Get("Settings.Projects.DeleteOne.FailedTitle"),
                result.ErrorMessage ?? LocalizationText.Get("Settings.Projects.DeleteOne.FailedMessage"),
                [project.ProjectFolder],
                LocalizationText.Get("Settings.Projects.DeleteOne.Title"));
            return;
        }
        _ = LoadProjectsAsync(preferredProjectFolder: null, preferredIndex: previousIndex);
    }

    private void RefreshProjects_Click(object sender, RoutedEventArgs e) =>
        _ = LoadProjectsAsync(SelectedProject?.ProjectFolder, forceRefresh: true);

    private void Catalog_SnapshotChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var snapshot = _catalog?.Snapshot;
            if (_closed || snapshot is null || snapshot.IsDirty ||
                snapshot.Generation == _displayedCatalogGeneration ||
                !string.Equals(snapshot.WorkingRoot, System.IO.Path.GetFullPath(_workingRootFolder), StringComparison.OrdinalIgnoreCase)) return;
            _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
        }));
    }

    private async Task LoadProjectsAsync(string? preferredProjectFolder, int? preferredIndex = null, bool forceRefresh = false)
    {
        if (_projectBrowser is null)
            return;
        var version = ++_loadVersion;
        IReadOnlyList<ProjectIndexItem> projects;
        try
        {
            var snapshot = await _catalog!.RefreshAsync(_workingRootFolder, Settings.ProjectRetentionDays, forceRefresh);
            while (!_closed && (_catalog.Snapshot?.Generation != snapshot.Generation || _catalog.Snapshot.IsDirty))
                snapshot = await _catalog.RefreshAsync(_workingRootFolder, Settings.ProjectRetentionDays);
            _displayedCatalogGeneration = snapshot.Generation;
            projects = snapshot.ProjectIndexItems.Where(project => project.IsVisibleInProjectBrowser).ToArray();
        }
        catch (Exception ex)
        {
            if (_closed || version != _loadVersion)
                return;
            Projects.Clear();
            OnPropertyChanged(nameof(HasProjects));
            SelectedProject = null;
            ProjectHeaderTitle = LocalizationText.Get("Settings.Projects.LoadFailed.Title");
            ProjectDetails = LocalizationText.Get("Project.Browser.ProjectUnreadable");
            (Application.Current as App)?.DiagnosticLogService?.WriteError("PROJECT PREVIEW",ex);
            return;
        }

        if (_closed || version != _loadVersion)
            return;
        Projects.Clear();
        foreach (var project in projects)
            Projects.Add(project);
        OnPropertyChanged(nameof(HasProjects));

        SelectedProject = Projects.FirstOrDefault(project =>
            !string.IsNullOrWhiteSpace(preferredProjectFolder) &&
            string.Equals(project.ProjectFolder, preferredProjectFolder, StringComparison.OrdinalIgnoreCase));
        if (SelectedProject is null && Projects.Count > 0)
        {
            var index = preferredIndex.HasValue ? Math.Clamp(preferredIndex.Value, 0, Projects.Count - 1) : 0;
            SelectedProject = Projects[index];
        }
        if (SelectedProject is null)
            UpdateProjectPresentation();
    }

    private void PreviewAppearanceChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess() || !IsVisible || _projectBrowser is null) return;
        _appearanceMode = AppearanceSettingsService.GetEffectiveMode(Settings);
        UpdateProjectPresentation();
    }

    private void UpdateProjectPresentation()
    {
        if (_projectBrowser is null)
            return;
        var presentation = _projectBrowser.Create(SelectedProject, Settings.ProjectRetentionMode, _appearanceMode);
        ProjectHeaderTitle = presentation.HeaderTitle;
        ProjectDetails = presentation.Details;
        ProjectLocationDetails = presentation.LocationDetails;
        try
        {
            ProjectCoverSource = !presentation.CoverUri.IsFile ?
                TryFindResource(AppearanceSettingsService.CoverPlaceholderImageSourceKey) as ImageSource ?? LoadImage(presentation.CoverUri) :
                LoadImage(presentation.CoverUri);
        }
        catch (Exception ex) when (presentation.CoverUri.IsFile)
        {
            System.Diagnostics.Trace.TraceWarning("Project cover preview rejected: {0}", ex.Message);
            ProjectCoverSource = LoadImage(new Uri(CoverPreviewPresentationService.GetPlaceholderPackUri(_appearanceMode)));
        }
    }

    private static ImageSource LoadImage(Uri uri) =>
        ProjectCoverThumbnailCache.Shared.Load(uri);

    private void ShowProjectCleanupResult(ProjectCleanupResult result, string title, bool oldProjectsOnly)
    {
        var message = new StringBuilder();
        var countKey = result.DeletedCount switch
        {
            0 => oldProjectsOnly ? "Settings.Projects.Cleanup.Old.None" : "Settings.Projects.Cleanup.All.None",
            1 => oldProjectsOnly ? "Settings.Projects.Cleanup.Old.One" : "Settings.Projects.Cleanup.All.One",
            _ => oldProjectsOnly ? "Settings.Projects.Cleanup.Old.Many" : "Settings.Projects.Cleanup.All.Many"
        };
        message.AppendLine(LocalizationText.Format(countKey, result.DeletedCount));

        if (result.HasFailures)
        {
            message.AppendLine();
            message.AppendLine(LocalizationText.Get("Settings.Projects.Cleanup.Failures"));
            foreach (var failure in result.Failures.Take(8))
                message.AppendLine("• " + failure);
            if (oldProjectsOnly && result.Failures.Count > 8)
                message.AppendLine(LocalizationText.Format("Settings.Projects.Cleanup.MoreFailures", result.Failures.Count - 8));
        }

        AppDialogService.Show(
            Window.GetWindow(this), title,
            LocalizationText.Get(result.HasFailures ? "Settings.Projects.Cleanup.Partial" : "Settings.Projects.Cleanup.Completed"),
            message.ToString().TrimEnd(),
            result.HasFailures ? AppDialogKind.Warning : AppDialogKind.Information,
            null,
            [new AppDialogButton(LocalizationText.Get("Common.OK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}
