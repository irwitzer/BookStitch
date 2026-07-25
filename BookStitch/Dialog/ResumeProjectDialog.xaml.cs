using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    private readonly ProjectIndexService _projectIndexService;
    private readonly ProjectResumePlanService _projectResumePlanService;
    private readonly int _projectRetentionDays;
    private ProjectIndexItem? _selectedProject;
    private ProjectResumePlan? _selectedResumePlan;
    private readonly HashSet<string> _reportedDamagedProjectFolders = new(StringComparer.OrdinalIgnoreCase);
    private bool _isShowingDamagedProjectWarning;
    private int _damagedProjectScanVersion;
    private int _projectLoadVersion;
    private string _projectDetails = "Noch kein Projekt ausgewählt.";
    private static readonly Uri DefaultProjectCoverUri = new("pack://application:,,,/Assets/Icons/BookStitchLogo-Round.png", UriKind.Absolute);

    private ImageSource _projectCoverSource = LoadProjectCoverImage();
    private Visibility _projectCoverVisibility = Visibility.Visible;
    private string _projectHeaderTitle = "Kein Projekt ausgewählt";

    public ResumeProjectDialog(
        string workingRootFolder,
        ProjectIndexService projectIndexService,
        ProjectResumePlanService projectResumePlanService,
        int projectRetentionDays,
        ProjectIndexItem? selectedProject = null)
    {
        InitializeComponent();

        _workingRootFolder = workingRootFolder;
        _projectIndexService = projectIndexService;
        _projectResumePlanService = projectResumePlanService;
        _projectRetentionDays = ProjectIndexService.NormalizeRetentionDays(projectRetentionDays);

        DataContext = this;
        ProjectHeaderTitle = "Projekte werden geladen";
        ProjectDetails = "Projektliste wird geladen …";

        var preferredProjectFolder = selectedProject?.ProjectFolder;
        Loaded += (_, _) =>
        {
            LoadProjectButton.Focus();
            Dispatcher.BeginInvoke(
                new Action(() => _ = LoadProjectsAsync(preferredProjectFolder)),
                DispatcherPriority.Background);
        };
    }

    public ObservableCollection<ProjectIndexItem> Projects { get; } = [];

    public bool HasProjects => Projects.Count > 0;

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
            UpdateProjectDetails();
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

    public bool HasSelectedProject => SelectedProject is not null;

    public bool CanLoadSelectedProject => SelectedResumePlan is not null && (SelectedResumePlan.Tracks.Count > 0 || SelectedResumePlan.CanContinueDiscImport);

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
        if (Projects.Count == 0)
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


    private void ProjectActionButton_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Button button)
            return;

        button.FocusVisualStyle = null;

        if (ReferenceEquals(button, LoadProjectButton))
        {
            button.Background = (Brush)FindResource("AccentHoverBrush");
            button.BorderBrush = (Brush)FindResource("AccentHoverBrush");
            button.Foreground = Brushes.White;
            return;
        }

        if (ReferenceEquals(button, DeleteProjectButton))
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x1A, 0x1D));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA3, 0x54, 0x5D));
            button.Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0xA0, 0xA6));
            return;
        }

        button.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x45));
        button.BorderBrush = (Brush)FindResource("AccentSoftBrush");
        button.Foreground = Brushes.White;
    }

    private void ProjectActionButton_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not Button button || button.IsMouseOver)
            return;

        if (ReferenceEquals(button, LoadProjectButton))
        {
            button.Background = (Brush)FindResource("AccentBrush");
            button.BorderBrush = (Brush)FindResource("AccentBrush");
            button.Foreground = Brushes.White;
            return;
        }

        if (ReferenceEquals(button, DeleteProjectButton))
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x17, 0x19));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0x3D, 0x45));
            button.Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0x8B, 0x91));
            return;
        }

        button.Background = (Brush)FindResource("DisabledButtonBackgroundBrush");
        button.BorderBrush = (Brush)FindResource("InputBorderBrush");
        button.Foreground = (Brush)FindResource("MainTextBrush");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadProjectsAsync(SelectedProject?.ProjectFolder);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedResumePlan is null)
            return;

        DialogResult = true;
        Close();
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        var project = SelectedProject;
        if (project is null)
            return;

        var confirmed = AppDialogService.Confirm(
            this,
            "Projekt endgültig löschen?",
            $"Das Projekt „{project.DisplayName}“ wird einschließlich aller Originaldateien, Konvertierungen und Ausgaben im Projektordner gelöscht.",
            [
                "Dieser Vorgang kann nicht rückgängig gemacht werden.",
                "Projektordner: " + project.ProjectFolder
            ],
            "Projekt löschen");

        if (!confirmed)
            return;

        var result = _projectIndexService.DeleteProject(_workingRootFolder, project.ProjectFolder);
        if (!result.Deleted)
        {
            AppDialogService.Error(
                this,
                "Projekt konnte nicht gelöscht werden",
                result.ErrorMessage ?? "Der Projektordner konnte nicht gelöscht werden.",
                [project.ProjectFolder],
                "Projekt löschen");
            return;
        }

        _ = LoadProjectsAsync(preferredProjectFolder: null);
    }

    private async Task LoadProjectsAsync(string? preferredProjectFolder)
    {
        var loadVersion = ++_projectLoadVersion;
        SelectedProject = null;
        SelectedResumePlan = null;
        Projects.Clear();
        OnPropertyChanged(nameof(HasProjects));
        ProjectHeaderTitle = "Projekte werden geladen";
        ProjectDetails = "Projektliste wird geladen …";

        IReadOnlyList<ProjectIndexItem> projects;
        try
        {
            projects = await Task.Run(() => _projectIndexService.ScanSelectableProjects(
                _workingRootFolder,
                _projectRetentionDays));
        }
        catch (Exception ex)
        {
            if (loadVersion != _projectLoadVersion)
                return;

            ProjectHeaderTitle = "Projektliste nicht geladen";
            ProjectDetails = "Die Projektliste konnte nicht geladen werden: " + ex.Message;
            return;
        }

        if (loadVersion != _projectLoadVersion)
            return;

        foreach (var project in projects)
            Projects.Add(project);

        OnPropertyChanged(nameof(HasProjects));

        SelectedProject = Projects.FirstOrDefault(project =>
            !string.IsNullOrWhiteSpace(preferredProjectFolder) &&
            string.Equals(project.ProjectFolder, preferredProjectFolder, StringComparison.OrdinalIgnoreCase))
            ?? Projects.FirstOrDefault();

        if (SelectedProject is null)
        {
            ProjectHeaderTitle = "Kein Projekt ausgewählt";
            ProjectDetails = "Keine vollständigen Projekte gefunden.";
        }

        QueueDamagedProjectScan();
    }

    private void QueueDamagedProjectScan()
    {
        var scanVersion = ++_damagedProjectScanVersion;
        var alreadyReported = _reportedDamagedProjectFolders
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Task.Run(() =>
        {
            try
            {
                return _projectIndexService
                    .ScanDamagedProjects(_workingRootFolder)
                    .Where(project => !alreadyReported.Contains(project.ProjectFolder))
                    .ToList();
            }
            catch
            {
                return new List<DamagedProjectInfo>();
            }
        }).ContinueWith(task =>
        {
            if (scanVersion != _damagedProjectScanVersion ||
                task.Status != TaskStatus.RanToCompletion ||
                task.Result.Count == 0)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (scanVersion != _damagedProjectScanVersion)
                    return;

                var damagedProjects = task.Result
                    .Where(project => !_reportedDamagedProjectFolders.Contains(project.ProjectFolder))
                    .ToList();

                if (damagedProjects.Count > 0)
                    ShowDamagedProjectWarnings(damagedProjects);
            }));
        });
    }

    private void ShowDamagedProjectWarnings(IReadOnlyList<DamagedProjectInfo> damagedProjects)
    {
        if (_isShowingDamagedProjectWarning || damagedProjects.Count == 0)
            return;

        _isShowingDamagedProjectWarning = true;
        try
        {
            foreach (var damagedProject in damagedProjects)
            {
                if (!Directory.Exists(damagedProject.ProjectFolder))
                    continue;

                _reportedDamagedProjectFolders.Add(damagedProject.ProjectFolder);

                var result = AppDialogService.Show(
                    this,
                    "Beschädigtes Projekt",
                    "Unvollständiges BookStitch-Projekt erkannt",
                    "Eine erforderliche Projektdatei fehlt oder ist nicht lesbar. BookStitch verändert die vorhandenen Audiodateien nicht und versucht keine automatische Reparatur.",
                    AppDialogKind.Warning,
                    [
                        "Projekt: " + damagedProject.DisplayName,
                        damagedProject.Reason,
                        "Projektordner: " + damagedProject.ProjectFolder
                    ],
                    [
                        new AppDialogButton("Projektordner öffnen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                        new AppDialogButton("Projekt löschen", AppDialogResult.No, IsDanger: true),
                        new AppDialogButton("Schließen", AppDialogResult.Cancel, IsCancel: true)
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
                    "Beschädigtes Projekt endgültig löschen?",
                    $"Das Projekt „{damagedProject.DisplayName}“ wird einschließlich aller noch vorhandenen Dateien gelöscht.",
                    [
                        "Dieser Vorgang kann nicht rückgängig gemacht werden.",
                        "Projektordner: " + damagedProject.ProjectFolder
                    ],
                    "Projekt löschen");

                if (!confirmed)
                    continue;

                var deletionResult = _projectIndexService.DeleteProject(_workingRootFolder, damagedProject.ProjectFolder);
                if (!deletionResult.Deleted)
                {
                    AppDialogService.Error(
                        this,
                        "Projekt konnte nicht gelöscht werden",
                        deletionResult.ErrorMessage ?? "Der Projektordner konnte nicht gelöscht werden.",
                        [damagedProject.ProjectFolder],
                        "Projekt löschen");
                }
            }
        }
        finally
        {
            _isShowingDamagedProjectWarning = false;
        }
    }

    private void OpenProjectFolder(string projectFolder)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = projectFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "Projektordner konnte nicht geöffnet werden",
                ex.Message,
                [projectFolder],
                "Projektordner öffnen");
        }
    }

    private void UpdateProjectDetails()
    {
        if (SelectedProject is null)
        {
            SelectedResumePlan = null;
            ProjectCoverSource = LoadProjectCoverImage();
            ProjectCoverVisibility = Visibility.Visible;
            ProjectHeaderTitle = "Kein Projekt ausgewählt";
            ProjectDetails = "Keine vollständigen Projekte gefunden.";
            return;
        }

        ProjectHeaderTitle = BuildProjectHeaderTitle(SelectedProject);

        var plan = _projectResumePlanService.BuildFromProjectFolder(SelectedProject.ProjectFolder);
        SelectedResumePlan = plan;
        UpdateProjectCover(plan);
        var builder = new StringBuilder();

        builder.AppendLine("Auswahl");
        builder.AppendLine("• Name: " + ValueOrPlaceholder(SelectedProject.DisplayName, "Unbenannt"));
        builder.AppendLine("• Erstellt: " + FormatDate(SelectedProject.CreatedUtc));
        builder.AppendLine("• Zuletzt geändert: " + FormatDate(SelectedProject.UpdatedUtc));
        builder.AppendLine("• Status: " + FormatStatus(SelectedProject.Status));
        builder.AppendLine("• Kategorie: Vollständig");
        builder.AppendLine("• Typ: " + FormatProjectType(SelectedProject.ProjectType));
        builder.AppendLine("• Ablauf: " + FormatExpiration(SelectedProject.ExpiresUtc));
        builder.AppendLine();

        builder.AppendLine("Metadaten");
        var displayedTitle = plan?.BookTitle ?? SelectedProject.Title;
        var displayedAlbum = plan?.Album ?? SelectedProject.Album;
        builder.AppendLine("• Autor: " + ValueOrPlaceholder(plan?.Author ?? SelectedProject.Author, "Unbenannt"));
        builder.AppendLine("• Titel: " + ValueOrPlaceholder(displayedTitle, "Unbenannt"));
        if (!string.IsNullOrWhiteSpace(displayedAlbum) &&
            !string.Equals(displayedAlbum.Trim(), displayedTitle?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("• Album: " + displayedAlbum.Trim());
        }
        builder.AppendLine("• Sprecher: " + ValueOrPlaceholder(plan?.Narrator ?? SelectedProject.Narrator, "nicht gesetzt"));
        builder.AppendLine("• Genre: " + ValueOrPlaceholder(plan?.Genre ?? SelectedProject.Genre, "nicht gesetzt"));
        builder.AppendLine();

        if (SelectedProject.TotalDiscs > 0)
        {
            builder.AppendLine("Discs");
            builder.AppendLine($"• Importiert: {SelectedProject.ImportedDiscCount}/{SelectedProject.TotalDiscs}");

            builder.AppendLine();
        }

        if (plan is not null)
        {
            builder.AppendLine("Projektinhalt");
            if (plan.Tracks.Count > 0)
            {
                builder.AppendLine("• Trackliste vorhanden: Ja");
                builder.AppendLine("• Tracks im Plan: " + plan.Tracks.Count.ToString(CultureInfo.InvariantCulture));
            }
            else if (plan.ImportedDiscCount > 0 && plan.ProjectType == ProjectManifestTypes.Mp3DiscProject)
            {
                builder.AppendLine("• Trackliste: noch nicht rekonstruiert");
                builder.AppendLine("• Tracks im Plan: 0");
            }
            else
            {
                builder.AppendLine("• Trackliste vorhanden: Nein");
                builder.AppendLine("• Tracks im Plan: 0");
            }

            if (!string.IsNullOrWhiteSpace(plan.SelectedPreset))
            {
                builder.AppendLine("• Aktuelles Preset:");
                builder.AppendLine("  ◦ " + FormatPresetWithAvailability(plan.ProjectFolder, plan.SelectedPreset, plan.Tracks.Count));
            }

            var preparedPresetLines = BuildPreparedPresetLines(plan.ProjectFolder, plan.SelectedPreset, plan.Tracks.Count).ToList();
            if (preparedPresetLines.Count > 0)
            {
                builder.AppendLine("• Weitere Presets:");
                foreach (var preparedPreset in preparedPresetLines)
                    builder.AppendLine("  ◦ " + preparedPreset);
            }

            builder.AppendLine();
        }
        else
        {
            builder.AppendLine("Projektinhalt");
            builder.AppendLine("• Dieses Projekt konnte gerade nicht vollständig gelesen werden.");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(SelectedProject.SourceFolder))
            builder.AppendLine("Quelle: " + SelectedProject.SourceFolder);

        if (!string.IsNullOrWhiteSpace(SelectedProject.OutputFolder))
            builder.AppendLine("Ausgabe: " + SelectedProject.OutputFolder);

        builder.AppendLine("Projektordner: " + SelectedProject.ProjectFolder);

        ProjectDetails = builder.ToString().TrimEnd();
    }

    private void UpdateProjectCover(ProjectResumePlan? plan)
    {
        var coverPath = FirstExistingFile(
            plan?.ProcessedCoverPath,
            plan?.CoverSourcePath);

        ProjectCoverSource = LoadProjectCoverImage(coverPath);
        ProjectCoverVisibility = Visibility.Visible;
    }


    private static ImageSource LoadProjectCoverImage(string? filePath = null)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? new Uri(filePath, UriKind.Absolute)
            : DefaultProjectCoverUri;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string BuildProjectHeaderTitle(ProjectIndexItem project)
    {
        var title = ValueOrPlaceholder(project.Title, project.DisplayName);
        var author = ValueOrPlaceholder(project.Author, "Autor unbekannt");
        return title + " - " + author;
    }

    private static string FirstExistingFile(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }

        return string.Empty;
    }

    private static string FormatDate(DateTime value)
    {
        if (value == default)
            return "unbekannt";

        var local = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    private static string FormatExpiration(DateTime expiresUtc)
    {
        if (expiresUtc == default)
            return "unbekannt";

        var localExpiration = expiresUtc.Kind == DateTimeKind.Local ? expiresUtc : expiresUtc.ToLocalTime();
        var today = DateTime.Now.Date;
        var expirationDate = localExpiration.Date;
        var remainingDays = (expirationDate - today).Days;

        if (remainingDays < 0)
            return "abgelaufen";

        if (remainingDays == 0)
            return "heute";

        if (remainingDays == 1)
            return "in 1 Tag";

        return "in " + remainingDays.ToString(CultureInfo.CurrentCulture) + " Tagen";
    }

    private static string ValueOrPlaceholder(string? value, string placeholder)
    {
        return string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();
    }

    private static string FormatProjectType(string? projectType)
    {
        return projectType switch
        {
            ProjectManifestTypes.Mp3DiscProject => "MP3-CD-Projekt",
            ProjectManifestTypes.AudioCdProject => "Audio-CD-Projekt",
            ProjectManifestTypes.FolderProject => "Lokales Ordnerprojekt",
            "Mp3Disc" => "MP3-CD-Import",
            _ => string.IsNullOrWhiteSpace(projectType) ? "Unbekannt" : projectType
        };
    }

    private static string FormatStatus(string? status)
    {
        return status switch
        {
            ProjectPipelineStateNames.Preparing => "Vorbereitung",
            ProjectPipelineStateNames.AcquiringSources => "Quellen werden übernommen",
            ProjectPipelineStateNames.Converting => "Konvertierung",
            ProjectPipelineStateNames.ReviewBeforeMerge => "Prüfung vor dem Zusammenfügen",
            ProjectPipelineStateNames.Merging => "Zusammenfügen",
            ProjectPipelineStateNames.Completed => "Export abgeschlossen",
            ProjectManifestStatuses.LegacyCreated => "Vorbereitung",
            ProjectManifestStatuses.LegacyImporting => "Quellen werden übernommen",
            ProjectManifestStatuses.LegacyReady => "Prüfung vor dem Zusammenfügen",
            ProjectManifestStatuses.LegacyExporting => "Konvertierung wurde unterbrochen",
            ProjectManifestStatuses.LegacyCanceled => "Abgebrochen",
            ProjectManifestStatuses.LegacyFailed => "Fehlgeschlagen",
            AudioDiscProjectStatus.AwaitingRip => "Vorbereitung",
            AudioDiscProjectStatus.Ripping => "Quellen werden übernommen",
            AudioDiscProjectStatus.WaitingForDisc => "Wartet auf nächste Audio-CD",
            AudioDiscProjectStatus.RippingCompleted => "Prüfung vor dem Zusammenfügen",
            AudioDiscExportStatus.PausedBeforeMerge => "Prüfung vor dem Zusammenfügen",
            _ => string.IsNullOrWhiteSpace(status) ? "Unbekannt" : status
        };
    }

    private static string FormatPresetWithAvailability(string projectFolder, string presetDisplayName, int expectedTrackCount)
    {
        var folderName = ExportPreset.Parse(presetDisplayName).GetFolderName();
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, folderName);
        var availability = GetConvertedPresetAvailability(convertedFolder, expectedTrackCount);

        return string.IsNullOrWhiteSpace(availability)
            ? presetDisplayName
            : presetDisplayName + ": " + availability;
    }

    private static IEnumerable<string> BuildPreparedPresetLines(string projectFolder, string selectedPreset, int expectedTrackCount)
    {
        var convertedRoot = ProjectFolderLayout.GetConvertedFolder(projectFolder);
        if (!Directory.Exists(convertedRoot))
            yield break;

        var selectedFolderName = string.IsNullOrWhiteSpace(selectedPreset)
            ? ""
            : ExportPreset.Parse(selectedPreset).GetFolderName();

        foreach (var folder in Directory.EnumerateDirectories(convertedRoot).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
        {
            var folderName = Path.GetFileName(folder);
            if (string.Equals(folderName, selectedFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            var availability = GetConvertedPresetAvailability(folder, expectedTrackCount);
            if (string.IsNullOrWhiteSpace(availability))
                continue;

            yield return FormatPresetFolderName(folderName) + ": " + availability;
        }
    }

    private static string GetConvertedPresetAvailability(string convertedFolder, int expectedTrackCount)
    {
        if (!Directory.Exists(convertedFolder))
            return "";

        var convertedFiles = Directory
            .EnumerateFiles(convertedFolder, "*.m4a", SearchOption.TopDirectoryOnly)
            .Where(file =>
                !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                !file.Contains(".part.", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (convertedFiles.Count == 0)
            return "";

        if (expectedTrackCount > 0 && convertedFiles.Count >= expectedTrackCount)
            return "vollständig vorhanden";

        if (expectedTrackCount > 0)
            return $"teilweise vorhanden ({convertedFiles.Count}/{expectedTrackCount})";

        return "teilweise vorhanden";
    }

    private static string FormatPresetFolderName(string folderName)
    {
        var match = Regex.Match(folderName ?? "", @"^aac_(mono|stereo)_(\d+)k$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return string.IsNullOrWhiteSpace(folderName) ? "Unbekanntes Preset" : folderName;

        var channel = match.Groups[1].Value.Equals("mono", StringComparison.OrdinalIgnoreCase)
            ? "Mono"
            : "Stereo";

        return "AAC " + channel + " " + match.Groups[2].Value + " kbps";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
