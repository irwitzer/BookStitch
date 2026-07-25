using BookStitch.Models;
using BookStitch.Services;
using Microsoft.Win32;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BookStitch.Dialog;

public sealed record DiscProjectSetupResult(
    int TotalDiscs,
    string SelectedExportPreset,
    string ParallelJobs,
    string OutputExtension,
    string OutputFolder,
    string BookTitle,
    string Album,
    string Author,
    string Narrator,
    string Genre,
    string FileNameTemplate,
    string CoverSourcePath,
    string ProcessedCoverPath,
    bool AutoMergeAfterConversion);

public partial class DiscProjectSetupDialog : Window
{
    private readonly int _minimumDiscs;
    private readonly int _maximumDiscs;
    private readonly IReadOnlyList<string> _exportPresets;
    private readonly int? _maxSourceBitrateKbps;
    private readonly ProjectSetupDialogRequest _request;
    private readonly Func<Window, ProjectSetupDialogGlobalSettings>? _openAdvancedSettings;
    private readonly Action<bool>? _setAlbumLink;
    private readonly Action<bool>? _setAutoMerge;
    private readonly Action<string>? _setOutputExtension;
    private readonly Action<string>? _setFileNameTemplate;
    private readonly Action<string>? _setLastCoverFolder;
    private readonly Action<string, string>? _previewCoverChanged;
    private bool _keepAlbumLinkedToTitle;
    private bool _isSynchronizingTitleAndAlbum;
    private bool _isInitializing;
    private string _lastAcceptedPreset = "";
    private readonly CoverImageService _coverImageService = new();
    private readonly string _coverWorkFolder;
    private string _selectedCoverSourcePath = "";
    private string _processedCoverPath = "";

    public DiscProjectSetupResult Result { get; private set; } = new(
        1, "", "Auto", ".m4a", "", "", "", "", "", "iBook Hörbuch", "{Autor} - {Titel}", "", "", false);

    public DiscProjectSetupDialog(
        ProjectSetupDialogRequest request,
        Func<Window, ProjectSetupDialogGlobalSettings>? openAdvancedSettings = null,
        Action<bool>? setAlbumLink = null,
        Action<bool>? setAutoMerge = null,
        Action<string>? setOutputExtension = null,
        Action<string>? setFileNameTemplate = null,
        Action<string>? setLastCoverFolder = null,
        Action<string, string>? previewCoverChanged = null)
    {
        InitializeComponent();

        _request = request;
        _openAdvancedSettings = openAdvancedSettings;
        _setAlbumLink = setAlbumLink;
        _setAutoMerge = setAutoMerge;
        _setOutputExtension = setOutputExtension;
        _setFileNameTemplate = setFileNameTemplate;
        _setLastCoverFolder = setLastCoverFolder;
        _previewCoverChanged = previewCoverChanged;
        _isInitializing = true;
        _minimumDiscs = request.MinimumDiscs;
        _maximumDiscs = request.MaximumDiscs;
        _exportPresets = request.ExportPresets;
        _maxSourceBitrateKbps = request.MaxSourceBitrateKbps;
        _coverWorkFolder = request.CoverWorkFolder;
        _keepAlbumLinkedToTitle = request.KeepAlbumLinkedToTitle;

        Title = request.WindowTitle;
        TitleText.Text = request.WindowTitle;
        SourceInformationText.Text = request.SourceInformation;
        InstructionText.Text = request.Instruction;

        var isFolderProject = request.SourceKind == ProjectSetupSourceKind.Folder;
        DiscCountLabel.Visibility = isFolderProject ? Visibility.Collapsed : Visibility.Visible;
        DiscCountPanel.Visibility = isFolderProject ? Visibility.Collapsed : Visibility.Visible;
        ProjectFolderLabel.Visibility = isFolderProject ? Visibility.Visible : Visibility.Collapsed;
        ProjectFolderButton.Visibility = isFolderProject ? Visibility.Visible : Visibility.Collapsed;
        ProjectFolderButton.Content = BuildFolderDisplayName(request.SourceFolder);
        ProjectFolderButton.ToolTip = request.SourceFolder;

        DiscCountTextBox.Text = Math.Clamp(request.DefaultDiscCount, request.MinimumDiscs, request.MaximumDiscs)
            .ToString(CultureInfo.InvariantCulture);

        PresetComboBox.ItemsSource = request.ExportPresets;
        if (request.ExportPresets.Contains(request.SelectedExportPreset))
            PresetComboBox.SelectedItem = request.SelectedExportPreset;
        else if (request.ExportPresets.Count > 0)
            PresetComboBox.SelectedIndex = 0;

        _lastAcceptedPreset = PresetComboBox.SelectedItem?.ToString() ?? request.SelectedExportPreset ?? "";
        ParallelJobsTextBox.Text = string.IsNullOrWhiteSpace(request.ParallelJobs) ? "Auto" : request.ParallelJobs;

        SelectOutputExtension(string.IsNullOrWhiteSpace(request.OutputExtension) ? ".m4a" : request.OutputExtension);
        OutputFolderTextBox.Text = request.OutputFolder ?? "";
        _selectedCoverSourcePath = request.CoverSourcePath ?? "";
        _processedCoverPath = !string.IsNullOrWhiteSpace(request.CoverPreviewSource) && File.Exists(request.CoverPreviewSource)
            ? request.CoverPreviewSource
            : "";
        SetCoverPreview(string.IsNullOrWhiteSpace(request.CoverPreviewSource) ? _selectedCoverSourcePath : request.CoverPreviewSource);

        TitleTextBox.Text = request.BookTitle ?? "";
        AlbumTextBox.Text = request.Album ?? "";
        AuthorTextBox.Text = request.Author ?? "";
        NarratorTextBox.Text = request.Narrator ?? "";
        GenreTextBox.Text = string.IsNullOrWhiteSpace(request.Genre) ? "iBook Hörbuch" : request.Genre;
        FileNameTemplateTextBox.Text = string.IsNullOrWhiteSpace(request.FileNameTemplate) ? "{Autor} - {Titel}" : request.FileNameTemplate;
        AutoMergeCheckBox.IsChecked = request.AutoMergeAfterConversion;
        AlbumTextBox.IsTabStop = !_keepAlbumLinkedToTitle;
        ConfigureTabOrder(isFolderProject);
        UpdateAlbumLinkPresentation();

        _isInitializing = false;
        UpdatePreview();

        Loaded += (_, _) =>
        {
            if (isFolderProject)
            {
                TitleTextBox.Focus();
                TitleTextBox.SelectAll();
            }
            else
            {
                DiscCountTextBox.Focus();
                DiscCountTextBox.SelectAll();
            }
        };
    }

    private void ConfigureTabOrder(bool isFolderProject)
    {
        if (isFolderProject)
        {
            TitleTextBox.TabIndex = 0;
            AlbumTextBox.TabIndex = 1;
            AuthorTextBox.TabIndex = 2;
            NarratorTextBox.TabIndex = 3;
            GenreTextBox.TabIndex = 4;
            StartProjectButton.TabIndex = 5;
            SortTracksButton.TabIndex = 6;
            PresetComboBox.TabIndex = 7;
            ParallelJobsTextBox.TabIndex = 8;
            OutputExtensionComboBox.TabIndex = 9;
            ChooseOutputFolderButton.TabIndex = 10;
            FileNameTemplateTextBox.TabIndex = 11;
            AutoMergeCheckBox.TabIndex = 12;
            DiscCountTextBox.TabIndex = 13;
            return;
        }

        DiscCountTextBox.TabIndex = 0;
        PresetComboBox.TabIndex = 1;
        ParallelJobsTextBox.TabIndex = 2;
        OutputExtensionComboBox.TabIndex = 3;
        ChooseOutputFolderButton.TabIndex = 4;
        FileNameTemplateTextBox.TabIndex = 5;
        AutoMergeCheckBox.TabIndex = 6;
        TitleTextBox.TabIndex = 7;
        AlbumTextBox.TabIndex = 8;
        AuthorTextBox.TabIndex = 9;
        NarratorTextBox.TabIndex = 10;
        GenreTextBox.TabIndex = 11;
        StartProjectButton.TabIndex = 12;
        SortTracksButton.TabIndex = 13;
    }

    private static string BuildFolderDisplayName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return "Quellordner";

        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed) is { Length: > 0 } name ? name : folderPath;
    }

    private void SelectOutputExtension(string extension)
    {
        var normalized = extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase)
            ? ".m4b"
            : ".m4a";

        foreach (var item in OutputExtensionComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                OutputExtensionComboBox.SelectedItem = item;
                return;
            }
        }

        OutputExtensionComboBox.SelectedIndex = 0;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CaptureResult(validateForStart: true))
            return;

        DialogResult = true;
        Close();
    }

    private bool CaptureResult(bool validateForStart)
    {
        var discCount = 1;
        if (_request.SourceKind != ProjectSetupSourceKind.Folder &&
            (!int.TryParse(DiscCountTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out discCount) ||
             discCount < _minimumDiscs || discCount > _maximumDiscs))
        {
            if (validateForStart)
            {
                ShowError($"Bitte eine CD-Anzahl von {_minimumDiscs} bis {_maximumDiscs} eingeben.");
                DiscCountTextBox.Focus();
                DiscCountTextBox.SelectAll();
                return false;
            }

            discCount = Math.Clamp(_request.DefaultDiscCount, _minimumDiscs, _maximumDiscs);
        }

        var preset = PresetComboBox.SelectedItem?.ToString() ?? _request.SelectedExportPreset;
        if (validateForStart && string.IsNullOrWhiteSpace(preset))
        {
            ShowError("Bitte ein Export-Preset auswählen.");
            PresetComboBox.Focus();
            return false;
        }

        var parallelJobs = ParallelJobsTextBox.Text.Trim();
        if (!IsValidParallelJobs(parallelJobs))
        {
            if (validateForStart)
            {
                ShowError("Bitte bei Parallel-Jobs „Auto“ oder eine Zahl von 1 bis 40 eingeben.");
                ParallelJobsTextBox.Focus();
                ParallelJobsTextBox.SelectAll();
                return false;
            }

            parallelJobs = _request.ParallelJobs;
        }

        var outputFolder = OutputFolderTextBox.Text.Trim();
        if (validateForStart && string.IsNullOrWhiteSpace(outputFolder))
        {
            ShowError("Bitte einen Ausgabeordner auswählen.");
            OutputFolderTextBox.Focus();
            return false;
        }

        Result = new DiscProjectSetupResult(
            discCount,
            preset,
            NormalizeParallelJobs(parallelJobs),
            GetSelectedOutputExtension(),
            outputFolder,
            TitleTextBox.Text.Trim(),
            AlbumTextBox.Text.Trim(),
            AuthorTextBox.Text.Trim(),
            NarratorTextBox.Text.Trim(),
            GetComboBoxText(GenreTextBox).Trim(),
            GetComboBoxText(FileNameTemplateTextBox).Trim(),
            _selectedCoverSourcePath,
            _processedCoverPath,
            AutoMergeCheckBox.IsChecked == true);

        return true;
    }

    private static bool IsValidParallelJobs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return true;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobs) &&
               jobs >= 1 &&
               jobs <= 40;
    }

    private static string NormalizeParallelJobs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Auto";

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return "Auto";

        return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
    }

    private string GetSelectedOutputExtension()
    {
        if (OutputExtensionComboBox.SelectedItem is ComboBoxItem item &&
            string.Equals(item.Content?.ToString(), ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            return ".m4b";
        }

        return ".m4a";
    }

    private void ChooseOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ausgabeordner auswählen",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(OutputFolderTextBox.Text) &&
            Directory.Exists(OutputFolderTextBox.Text))
        {
            dialog.InitialDirectory = OutputFolderTextBox.Text;
        }
        else
        {
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        if (dialog.ShowDialog(this) != true)
            return;

        OutputFolderTextBox.Text = dialog.FolderName;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void CoverBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ChooseCover();
    }

    private void CoverBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_processedCoverPath))
            e.Handled = true;
    }

    private void RemoveCover_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_processedCoverPath))
            return;

        _selectedCoverSourcePath = "";
        _processedCoverPath = "";
        SetDefaultCoverPreview();
        _previewCoverChanged?.Invoke("", "");
    }

    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        ChooseCover();
    }

    private void ChooseCover()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Cover auswählen",
            Filter = "Bilddateien (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Alle Dateien (*.*)|*.*",
            Multiselect = false
        };

        var currentCoverFolder = !string.IsNullOrWhiteSpace(_selectedCoverSourcePath) &&
                                 File.Exists(_selectedCoverSourcePath)
            ? Path.GetDirectoryName(_selectedCoverSourcePath)
            : null;

        dialog.InitialDirectory = GetExistingFolderOrDesktop(
            currentCoverFolder,
            _request.LastCoverFolder);

        if (dialog.ShowDialog(this) != true)
            return;

        var selectedFolder = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(selectedFolder) && Directory.Exists(selectedFolder))
            _setLastCoverFolder?.Invoke(selectedFolder);

        ProcessSelectedCover(dialog.FileName);
    }

    private static string GetExistingFolderOrDesktop(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                return candidate;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private void ProcessSelectedCover(string sourcePath)
    {
        try
        {
            var result = _coverImageService.CreateProcessedCover(sourcePath, _coverWorkFolder);
            _selectedCoverSourcePath = result.SourcePath;
            _processedCoverPath = result.ProcessedJpegPath;
            SetCoverPreview(result.ProcessedJpegPath);
            _previewCoverChanged?.Invoke(_selectedCoverSourcePath, _processedCoverPath);

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                AppDialogService.Warning(
                    this,
                    "Cover übernommen",
                    result.Warning);
            }
        }
        catch (Exception ex)
        {
            AppDialogService.Error(
                this,
                "Cover konnte nicht übernommen werden",
                "Das Coverbild konnte nicht verarbeitet werden.",
                new[] { ex.Message });
        }
    }

    private void SetCoverPreview(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            SetDefaultCoverPreview();
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = File.Exists(filePath)
                ? new Uri(filePath, UriKind.Absolute)
                : new Uri("pack://application:,,,/Assets/Icons/BookStitchLogo-Round.png", UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            CoverPreviewImage.Source = image;
            CoverHintText.Text = File.Exists(filePath) ? "Cover ändern" : "Cover wählen";
        }
        catch
        {
            SetDefaultCoverPreview();
        }
    }

    private void SetDefaultCoverPreview()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/Assets/Icons/BookStitchLogo-Round.png", UriKind.Absolute));
        image.Freeze();
        CoverPreviewImage.Source = image;
        CoverHintText.Text = "Cover wählen";
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_openAdvancedSettings is not null)
        {
            var settings = _openAdvancedSettings(this);
            AutoMergeCheckBox.IsChecked = settings.AutoMergeAfterConversion;
            _keepAlbumLinkedToTitle = settings.KeepAlbumLinkedToTitle;
            SelectOutputExtension(settings.OutputExtension);
            FileNameTemplateTextBox.Text = settings.FileNameTemplate;
            if (_keepAlbumLinkedToTitle)
                AlbumTextBox.Text = TitleTextBox.Text;
            AlbumTextBox.IsTabStop = !_keepAlbumLinkedToTitle;
            UpdateAlbumLinkPresentation();
            UpdatePreview();
        }
        Activate();
        Focus();
    }

    private void ProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_request.SourceFolder) || !Directory.Exists(_request.SourceFolder))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _request.SourceFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppDialogService.Warning(this, "Quellordner konnte nicht geöffnet werden", ex.Message);
        }
    }

    private void AlbumLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _keepAlbumLinkedToTitle = !_keepAlbumLinkedToTitle;
        _setAlbumLink?.Invoke(_keepAlbumLinkedToTitle);
        AlbumTextBox.IsTabStop = !_keepAlbumLinkedToTitle;
        if (_keepAlbumLinkedToTitle)
            AlbumTextBox.Text = TitleTextBox.Text;
        UpdateAlbumLinkPresentation();
        e.Handled = true;
    }

    private void UpdateAlbumLinkPresentation()
    {
        AlbumLabel.ToolTip = _keepAlbumLinkedToTitle
            ? "Aktuell gekoppelt. Klicken, um Titel und Album getrennt zu bearbeiten."
            : "Aktuell getrennt. Klicken, um Titel und Album automatisch zu koppeln.";
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_keepAlbumLinkedToTitle && !_isSynchronizingTitleAndAlbum)
        {
            _isSynchronizingTitleAndAlbum = true;
            AlbumTextBox.Text = TitleTextBox.Text;
            _isSynchronizingTitleAndAlbum = false;
        }
        UpdatePreview();
    }

    private void AlbumTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_keepAlbumLinkedToTitle && !_isSynchronizingTitleAndAlbum)
        {
            _isSynchronizingTitleAndAlbum = true;
            TitleTextBox.Text = AlbumTextBox.Text;
            _isSynchronizingTitleAndAlbum = false;
        }
        UpdatePreview();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureResult(validateForStart: false);
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureResult(validateForStart: false);
        DialogResult = false;
        Close();
    }

    private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private void ParallelJobsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(ch => char.IsDigit(ch) || char.IsLetter(ch));
    }

    private void DiscCountTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            StepDiscCount(+1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            StepDiscCount(-1);
            e.Handled = true;
        }
        else
        {
            Input_KeyDown(sender, e);
        }
    }

    private void ParallelJobsTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up)
        {
            StepParallelJobs(+1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            StepParallelJobs(-1);
            e.Handled = true;
        }
        else
        {
            Input_KeyDown(sender, e);
        }
    }

    private void DiscCountTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StepDiscCount(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void ParallelJobsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        StepParallelJobs(e.Delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    private void AutoMergeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        _setAutoMerge?.Invoke(AutoMergeCheckBox.IsChecked == true);
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void IncreaseDiscCount_Click(object sender, RoutedEventArgs e)
    {
        StepDiscCount(+1);
    }

    private void DecreaseDiscCount_Click(object sender, RoutedEventArgs e)
    {
        StepDiscCount(-1);
    }

    private void IncreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        StepParallelJobs(+1);
    }

    private void DecreaseParallelJobs_Click(object sender, RoutedEventArgs e)
    {
        StepParallelJobs(-1);
    }

    private void StepDiscCount(int delta)
    {
        if (!int.TryParse(DiscCountTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = _minimumDiscs;

        value = Math.Clamp(value + delta, _minimumDiscs, _maximumDiscs);
        DiscCountTextBox.Text = value.ToString(CultureInfo.InvariantCulture);
    }

    private void StepParallelJobs(int delta)
    {
        var text = ParallelJobsTextBox.Text.Trim();

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = delta > 0 ? 0 : 2;

        value = Math.Clamp(value + delta, 1, 40);
        ParallelJobsTextBox.Text = value.ToString(CultureInfo.InvariantCulture);
        ParallelJobsTextBox.SelectAll();
    }

    private void MetadataTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void MetadataComboBox_KeyUp(object sender, KeyEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void MetadataComboBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void MetadataComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncFileNameTemplateIfNeeded(sender);
        UpdatePreview();
    }

    private void SyncFileNameTemplateIfNeeded(object sender)
    {
        if (_isInitializing || !ReferenceEquals(sender, FileNameTemplateTextBox))
            return;

        var template = GetComboBoxText(FileNameTemplateTextBox).Trim();
        if (!string.IsNullOrWhiteSpace(template))
            _setFileNameTemplate?.Invoke(template);
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        ErrorText.Visibility = Visibility.Collapsed;

        var selectedPreset = PresetComboBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selectedPreset))
            return;

        var confirmedPreset = ConfirmPresetBitrateIfNeeded(selectedPreset);
        if (!string.Equals(confirmedPreset, selectedPreset, StringComparison.OrdinalIgnoreCase))
        {
            _isInitializing = true;
            PresetComboBox.SelectedItem = confirmedPreset;
            _isInitializing = false;
        }

        _lastAcceptedPreset = confirmedPreset;
    }

    private string ConfirmPresetBitrateIfNeeded(string requestedPreset)
    {
        if (_maxSourceBitrateKbps is not > 0)
            return requestedPreset;

        var requested = ExportPreset.Parse(requestedPreset);
        if (requested.BitrateKbps <= _maxSourceBitrateKbps.Value)
            return requestedPreset;

        var recommendedPreset = FindBestPresetForSourceBitrate(_maxSourceBitrateKbps.Value, requested.Channels);
        var recommended = ExportPreset.Parse(recommendedPreset);

        var result = AppDialogService.Show(
            this,
            "Export-Preset prüfen",
            "Gewähltes Preset ist höher als die Quelldateien",
            $"Die höchste erkannte Quell-Bitrate liegt bei {_maxSourceBitrateKbps.Value} kbps.\n" +
            $"Ausgewählt ist {requested.BitrateKbps} kbps. Hochkonvertieren macht die Datei größer, aber nicht hörbar besser.",
            AppDialogKind.Warning,
            details: new[]
            {
                $"Empfohlen: {recommended.DisplayName}",
                $"Gewählt: {requested.DisplayName}"
            },
            buttons: new[]
            {
                new AppDialogButton($"Auf {recommended.DisplayName} stellen", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Trotzdem verwenden", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });

        return result switch
        {
            AppDialogResult.Yes => recommendedPreset,
            AppDialogResult.No => requestedPreset,
            _ => string.IsNullOrWhiteSpace(_lastAcceptedPreset) ? requestedPreset : _lastAcceptedPreset
        };
    }

    private string FindBestPresetForSourceBitrate(int maxSourceBitrateKbps, int preferredChannels)
    {
        var parsedPresets = _exportPresets
            .Select(ExportPreset.Parse)
            .OrderBy(preset => preset.BitrateKbps)
            .ThenBy(preset => preset.Channels)
            .ToList();

        var sameChannels = parsedPresets
            .Where(preset => preset.Channels == preferredChannels && preset.BitrateKbps <= maxSourceBitrateKbps)
            .OrderByDescending(preset => preset.BitrateKbps)
            .FirstOrDefault();

        if (sameChannels is not null)
            return sameChannels.DisplayName;

        var anyChannels = parsedPresets
            .Where(preset => preset.BitrateKbps <= maxSourceBitrateKbps)
            .OrderByDescending(preset => preset.BitrateKbps)
            .FirstOrDefault();

        return (anyChannels ?? parsedPresets.First()).DisplayName;
    }

    private void OutputExtensionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing)
            _setOutputExtension?.Invoke(GetSelectedOutputExtension());

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_isInitializing || PreviewTextBlock is null)
            return;

        PreviewTextBlock.Text = FileNameTemplateService.BuildOutputFileName(
            TitleTextBox.Text,
            AuthorTextBox.Text,
            NarratorTextBox.Text,
            string.IsNullOrWhiteSpace(GetComboBoxText(FileNameTemplateTextBox)) ? "{Autor} - {Titel}" : GetComboBoxText(FileNameTemplateTextBox),
            GetSelectedOutputExtension());
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? "";

        return comboBox.Text ?? "";
    }
}
