using BookStitch.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BookStitch.Dialog;

public partial class DeveloperAudioDiscTestDialog : Window
{
    public sealed record DriveChoice(DiscDriveInfo Drive, string DriveDisplay)
    {
        public override string ToString() => DriveDisplay;
    }

    private readonly DeveloperAudioDiscTestProjectService _audioTestProjectService = new();
    private readonly DeveloperMp3DiscTestProjectService _mp3TestProjectService = new();
    private readonly DiscDriveService _discDriveService = new();
    private readonly DispatcherTimer _driveRefreshTimer;
    private bool _isRefreshingDrives;
    private readonly string _initialAudioFolder;
    private readonly string _initialMp3Folder;

    public string ProjectFolder => ProjectFolderTextBox.Text.Trim();
    public bool IsMp3DiscTest => (ProjectTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Mp3Disc";
    public int DiscNumber { get; private set; } = 1;
    public int ResetTrackCount { get; private set; } = 3;
    public int TotalDiscs { get; private set; } = 2;
    public DiscDriveInfo? SelectedDrive => (DriveComboBox.SelectedItem as DriveChoice)?.Drive;

    public DeveloperAudioDiscTestDialog(IReadOnlyList<DiscDriveInfo> drives, string initialAudioFolder = "", string initialMp3Folder = "", bool initiallySelectMp3Disc = false)
    {
        _initialAudioFolder = initialAudioFolder;
        _initialMp3Folder = initialMp3Folder;
        InitializeComponent();
        DriveComboBox.ItemsSource = drives
            .Select(d => new DriveChoice(d, BuildDriveDisplay(d)))
            .ToList();
        if (DriveComboBox.Items.Count > 0)
            DriveComboBox.SelectedIndex = 0;

        ProjectTypeComboBox.SelectedIndex = initiallySelectMp3Disc ? 1 : 0;
        ProjectFolderTextBox.Text = initiallySelectMp3Disc ? _initialMp3Folder : _initialAudioFolder;
        RefreshProjectMetadata();

        _driveRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _driveRefreshTimer.Tick += DriveRefreshTimer_Tick;
        _driveRefreshTimer.Start();
        Closed += (_, _) => _driveRefreshTimer.Stop();
    }


    private async void DriveRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshDrivesAsync();
    }

    private async Task RefreshDrivesAsync()
    {
        if (_isRefreshingDrives)
            return;

        _isRefreshingDrives = true;
        try
        {
            var selectedRoot = SelectedDrive?.RootPath;
            var drives = await Task.Run(() => _discDriveService.GetCdDrives());
            var choices = drives.Select(d => new DriveChoice(d, BuildDriveDisplay(d))).ToList();
            DriveComboBox.ItemsSource = choices;

            var selectedIndex = !string.IsNullOrWhiteSpace(selectedRoot)
                ? choices.FindIndex(item => string.Equals(item.Drive.RootPath, selectedRoot, StringComparison.OrdinalIgnoreCase))
                : -1;
            DriveComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : choices.Count > 0 ? 0 : -1;
        }
        finally
        {
            _isRefreshingDrives = false;
        }
    }

    private static string BuildDriveDisplay(DiscDriveInfo drive)
    {
        var letter = string.IsNullOrWhiteSpace(drive.DriveLetter) ? drive.RootPath.TrimEnd('\\') : drive.DriveLetter.TrimEnd(':');
        var name = string.IsNullOrWhiteSpace(drive.DiagnosticDriveName) ? drive.DriveName : drive.DiagnosticDriveName;
        var media = drive.StatusText;
        return string.IsNullOrWhiteSpace(name) ? $"{letter}: – {media}" : $"{letter}: – {name} – {media}";
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = IsMp3DiscTest ? "MP3-CD-Testprojekt auswählen" : "Audio-CD-Testprojekt auswählen",
            Multiselect = false,
            InitialDirectory = Directory.Exists(ProjectFolder) ? ProjectFolder : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) == true)
            ProjectFolderTextBox.Text = dialog.FolderName;
    }

    private void ProjectFolderTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshProjectMetadata();

    private void RefreshProjectMetadata()
    {
        if (ProjectTitleTextBlock is null)
            return;

        ProjectTitleTextBlock.Text = "–";
        ProjectAuthorTextBlock.Text = "–";
        ProjectNarratorTextBlock.Text = "–";

        var folder = ProjectFolder;
        if (!Directory.Exists(folder))
            return;

        try
        {
            if (IsMp3DiscTest)
            {
                var manifest = _mp3TestProjectService.TryLoadTemplate(folder);
                if (manifest is null) return;
                ProjectTitleTextBlock.Text = DisplayValue(manifest.Title);
                ProjectAuthorTextBlock.Text = DisplayValue(manifest.Author);
                ProjectNarratorTextBlock.Text = DisplayValue(manifest.Narrator);
            }
            else
            {
                var manifest = _audioTestProjectService.TryLoadTemplate(folder);
                if (manifest is null) return;
                ProjectTitleTextBlock.Text = DisplayValue(manifest.Title);
                ProjectAuthorTextBlock.Text = DisplayValue(manifest.Author);
                ProjectNarratorTextBlock.Text = DisplayValue(manifest.Narrator);
            }
        }
        catch
        {
            // Die eigentliche Validierung erfolgt beim Start. Die Vorschau bleibt bei ungültigen Testdaten leer.
        }
    }

    private void ProjectTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiscNumberLabel is null) return;
        DiscNumberLabel.Text = IsMp3DiscTest ? "Erneut zu importierende CD" : "Erneut zu rippende CD";
        TrackCountLabel.Text = IsMp3DiscTest ? "Letzte Dateien erneut" : "Letzte Tracks erneut";
        ProjectFolderTextBox.Text = IsMp3DiscTest ? _initialMp3Folder : _initialAudioFolder;
        InfoTextBlock.Text = IsMp3DiscTest
            ? "Das ausgewählte Testprojekt bleibt unverändert. BookStitch erstellt eine normale MP3-CD-Projektkopie und kopiert beziehungsweise konvertiert dort nur die gewählten letzten Dateien erneut."
            : "Das ausgewählte Testprojekt bleibt unverändert. BookStitch erstellt eine normale Audio-CD-Projektkopie und setzt nur dort die gewählten letzten FLAC- und AAC-Dateien zurück.";
        RefreshProjectMetadata();
    }

    private static string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "–" : value.Trim();

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(ProjectFolder))
        {
            MessageBox.Show(this, "Der Projektordner wurde nicht gefunden.", IsMp3DiscTest ? "MP3-CD-Kurztest" : "Audio-CD-Kurztest", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SelectedDrive is null)
        {
            MessageBox.Show(this, "Bitte wähle ein CD-Laufwerk.", IsMp3DiscTest ? "MP3-CD-Kurztest" : "Audio-CD-Kurztest", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(DiscNumberTextBox.Text, out var disc) || disc < 1 ||
            !int.TryParse(TrackCountTextBox.Text, out var tracks) || tracks < 1 ||
            !int.TryParse(TotalDiscsTextBox.Text, out var discs) || discs < 2)
        {
            MessageBox.Show(this, "Bitte gültige positive Zahlen eingeben. Die Gesamtzahl der CDs muss mindestens 2 sein.", IsMp3DiscTest ? "MP3-CD-Kurztest" : "Audio-CD-Kurztest", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DiscNumber = disc;
        ResetTrackCount = tracks;
        TotalDiscs = discs;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
