using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Dialog;

public partial class DialogPreviewWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly DeveloperDiscTestService _developerDiscTestService = new();
    private readonly DiscDriveService _discDriveService = new();
    private readonly IDiscWaitDialogService _discWaitDialogService;
    private readonly Mp3DiscImportService _mp3DiscImportService = new();
    private bool _isLoadingSoundSettings;
    private CancellationTokenSource? _delayedFocusTestCancellation;
    private CancellationTokenSource? _discTestCancellation;

    public DialogPreviewWindow(
        AppSettings settings,
        SettingsService settingsService,
        NotificationService notificationService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _discWaitDialogService = new SwitchableDiscWaitDialogService(() => _settings.UseBoxedDiscWaitDialog);
        InitializeComponent();
        Closed += (_, _) =>
        {
            _delayedFocusTestCancellation?.Cancel();
            _delayedFocusTestCancellation?.Dispose();
            _discTestCancellation?.Cancel();
            _discTestCancellation?.Dispose();
        };
        LoadSoundSettings();
    }


    private void LoadSoundSettings()
    {
        _isLoadingSoundSettings = true;
        try
        {
            SelectComboBoxItem(PreviewSoundProfileComboBox, SoundSettingsService.NormalizeProfile(_settings.SoundProfile));
            SelectComboBoxItem(PreviewFocusProfileComboBox, FocusSettingsService.NormalizeProfile(_settings.FocusProfile));
            SelectComboBoxItem(PreviewSoundLibraryComboBox, SoundSettingsService.NormalizeLibrary(_settings.SoundLibrary));
            PreviewSoundVolumeSlider.Value = SoundSettingsService.NormalizeVolumePercent(_settings.SoundVolumePercent);
            UpdatePreviewSoundVolumeText();
        }
        finally
        {
            _isLoadingSoundSettings = false;
        }
    }

    private void PreviewFocusProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSoundSettings || sender is not ComboBox comboBox)
            return;

        _settings.FocusProfile = GetSelectedEnum(comboBox, FocusSettingsService.DefaultProfile).ToString();
        _settingsService.Save(_settings);
    }

    private void PreviewSoundProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSoundSettings || sender is not ComboBox comboBox)
            return;

        _settings.SoundProfile = GetSelectedEnum(comboBox, SoundSettingsService.DefaultProfile).ToString();
        _settingsService.Save(_settings);
    }

    private void PreviewSoundLibraryComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSoundSettings || sender is not ComboBox comboBox)
            return;

        _settings.SoundLibrary = GetSelectedEnum(comboBox, SoundSettingsService.DefaultLibrary).ToString();
        _settingsService.Save(_settings);
        _notificationService.PlaySoundPreview();
    }

    private void PreviewSoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSoundSettings)
            return;

        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent((int)Math.Round(e.NewValue));
        _settingsService.Save(_settings);
        UpdatePreviewSoundVolumeText();
    }

    private void UpdatePreviewSoundVolumeText()
    {
        PreviewSoundVolumeTextBlock.Text = $"{_settings.SoundVolumePercent} %";
    }

    private static void SelectComboBoxItem<TEnum>(ComboBox comboBox, TEnum selectedValue)
        where TEnum : struct, Enum
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Enum.TryParse<TEnum>(item.Tag?.ToString(), out var value)
                && EqualityComparer<TEnum>.Default.Equals(value, selectedValue))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static TEnum GetSelectedEnum<TEnum>(ComboBox comboBox, TEnum fallback)
        where TEnum : struct, Enum
    {
        return comboBox.SelectedItem is ComboBoxItem item
               && Enum.TryParse<TEnum>(item.Tag?.ToString(), out var value)
            ? value
            : fallback;
    }

    private void MessagePreview_Click(object sender, RoutedEventArgs e)
    {
        var kind = sender is Button { Tag: string tag } && Enum.TryParse<AppDialogKind>(tag, out var parsed)
            ? parsed
            : AppDialogKind.Information;

        AppDialogService.Show(
            this,
            "BookStitch – Dialogvorschau",
            kind switch
            {
                AppDialogKind.Warning => "Beispielwarnung",
                AppDialogKind.Error => "Beispielfehler",
                AppDialogKind.Question => "Beispielfrage",
                _ => "Beispielinformation"
            },
            "Dieser Dialog dient ausschließlich der visuellen und mechanischen Prüfung. Es werden keine Projektdaten verändert.",
            kind,
            ["Erste optionale Detailzeile", "Zweite optionale Detailzeile"],
            kind == AppDialogKind.Question
                ? [
                    new AppDialogButton("Ja", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton("Nein", AppDialogResult.No),
                    new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
                  ]
                : [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }


    private async void DelayedFocusPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button
            || !Enum.TryParse<NotificationEvent>(tag, out var notificationEvent))
        {
            return;
        }

        _delayedFocusTestCancellation?.Cancel();
        _delayedFocusTestCancellation?.Dispose();
        _delayedFocusTestCancellation = new CancellationTokenSource();
        var cancellationToken = _delayedFocusTestCancellation.Token;

        button.IsEnabled = false;
        try
        {
            for (var remaining = 4; remaining > 0; remaining--)
            {
                DelayedFocusStatusTextBlock.Text = $"{button.Content}: Auslösung in {remaining} s";
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            DelayedFocusStatusTextBlock.Text = $"Ausgelöst: {button.Content}";
            _notificationService.Notify(notificationEvent);
        }
        catch (OperationCanceledException)
        {
            DelayedFocusStatusTextBlock.Text = "Fokustest abgebrochen";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void NotificationPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<NotificationEvent>(tag, out var notificationEvent))
        {
            return;
        }

        _notificationService.Notify(notificationEvent);
    }

    private async void SimulatedDiscWaitPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }
            || !Enum.TryParse<DeveloperDiscSimulationScenario>(tag, out var scenario))
        {
            return;
        }

        CancelCurrentDiscTest();
        _discTestCancellation = new CancellationTokenSource();
        var cancellationToken = _discTestCancellation.Token;
        var checkNumber = 0;

        DiscTestStatusTextBlock.Text = $"Dialogsimulation gestartet: {GetSimulationDisplayName(scenario)}";
        try
        {
            var completed = await _discWaitDialogService.WaitForDiscAsync(
                this,
                new DiscWaitDialogRequest(
                    DiscNumber: 2,
                    TotalDiscs: 3,
                    MediaName: "Test-CD",
                    InitialInstruction: "Entwicklersimulation wird vorbereitet...",
                    HintText: "Diese Vorschau greift nicht auf ein Laufwerk zu und verändert keine Projektdaten."),
                _ => Task.FromResult(_developerDiscTestService.CreateSimulationResult(scenario, ++checkNumber)),
                status => DiscTestStatusTextBlock.Text = status,
                progress => DiscTestProgressTextBlock.Text = progress,
                cancellationToken);

            DiscTestStatusTextBlock.Text = completed == DiscWaitDialogOutcome.Ready
                ? $"Dialogsimulation abgeschlossen: {GetSimulationDisplayName(scenario)}"
                : $"Dialogsimulation später fortsetzbar beendet: {GetSimulationDisplayName(scenario)}";
        }
        catch (OperationCanceledException)
        {
            DiscTestStatusTextBlock.Text = "Dialogsimulation abgebrochen.";
        }
    }

    private async void HybridDiscTestPreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        var selectedDrive = SelectOpticalDrive();
        if (string.IsNullOrWhiteSpace(selectedDrive))
            return;

        _settings.LastSelectedOpticalDrive = selectedDrive;
        _settingsService.Save(_settings);

        CancelCurrentDiscTest();
        _discTestCancellation = new CancellationTokenSource();
        var cancellationToken = _discTestCancellation.Token;

        try
        {
            var isAudio = tag.StartsWith("Audio", StringComparison.Ordinal);
            var duplicateTest = tag.EndsWith("Duplicate", StringComparison.Ordinal);
            var mediaName = isAudio ? "Audio-CD" : "MP3-CD";

            HashSet<string> knownIdentities = [];
            if (duplicateTest)
            {
                var identity = isAudio
                    ? ReadCurrentAudioDiscIdentity(selectedDrive)
                    : ReadCurrentMp3DiscIdentity(selectedDrive);
                if (string.IsNullOrWhiteSpace(identity))
                    return;

                knownIdentities.Add(identity);
            }

            var ejected = _discDriveService.TryEjectDisc(selectedDrive);
            DiscTestStatusTextBlock.Text = ejected
                ? $"{mediaName}-Hardwaretest: Laufwerk wurde ausgeworfen. Bitte Disc einlegen."
                : $"{mediaName}-Hardwaretest: Bitte Laufwerk öffnen und Disc einlegen.";
            DiscTestProgressTextBlock.Text = duplicateTest
                ? "Für den Duplikattest dieselbe Disc erneut einlegen."
                : "Es wird nur erkannt und geprüft; Import und Ripping bleiben deaktiviert.";

            var completed = isAudio
                ? await RunAudioHybridTestAsync(selectedDrive, knownIdentities, cancellationToken)
                : await RunMp3HybridTestAsync(selectedDrive, knownIdentities, cancellationToken);

            DiscTestStatusTextBlock.Text = completed == DiscWaitDialogOutcome.Ready
                ? $"{mediaName}-Hardwaretest erfolgreich. Es wurden keine Tracks verarbeitet."
                : $"{mediaName}-Hardwaretest wurde für später beendet.";
        }
        catch (OperationCanceledException)
        {
            DiscTestStatusTextBlock.Text = "Hybridtest abgebrochen.";
        }
        catch (Exception ex)
        {
            DiscTestStatusTextBlock.Text = "Hybridtest fehlgeschlagen.";
            AppDialogService.Show(
                this,
                "BookStitch – Hybridtest",
                "Hardwaretest fehlgeschlagen",
                ex.Message,
                AppDialogKind.Error,
                buttons: [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
        }
    }

    private Task<DiscWaitDialogOutcome> RunMp3HybridTestAsync(
        string driveRoot,
        ISet<string> knownSignatures,
        CancellationToken cancellationToken)
    {
        var pollingService = new Mp3DiscPollingService(_mp3DiscImportService, _discDriveService);
        return _discWaitDialogService.WaitForDiscAsync(
            this,
            new DiscWaitDialogRequest(
                DiscNumber: 2,
                TotalDiscs: 3,
                MediaName: "MP3-CD",
                InitialInstruction: "Bitte CD 2 von 3 einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                HintText: "Nach erfolgreicher Erkennung endet der Test ohne Import, Konvertierung oder Projektänderung.",
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDiscTestPollingState),
            token => Task.Run(
                () => pollingService.CheckDiscSourceForNextImport(driveRoot, 2, 3, knownSignatures),
                token),
            status => DiscTestStatusTextBlock.Text = status,
            progress => DiscTestProgressTextBlock.Text = progress,
            cancellationToken);
    }

    private Task<DiscWaitDialogOutcome> RunAudioHybridTestAsync(
        string driveRoot,
        ISet<string> knownIdentities,
        CancellationToken cancellationToken)
    {
        var pollingService = new AudioDiscPollingService(new AudioDiscReaderService(), _discDriveService);
        return _discWaitDialogService.WaitForDiscAsync(
            this,
            new DiscWaitDialogRequest(
                DiscNumber: 2,
                TotalDiscs: 3,
                MediaName: "Audio-CD",
                InitialInstruction: "Bitte Audio-CD 2 von 3 einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                HintText: "Nach erfolgreicher Erkennung endet der Test ohne Ripping, Konvertierung oder Projektänderung.",
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDiscTestPollingState),
            token => Task.Run(
                () => pollingService.CheckNextDisc(driveRoot, 2, 3, knownIdentities).PollingResult,
                token),
            status => DiscTestStatusTextBlock.Text = status,
            progress => DiscTestProgressTextBlock.Text = progress,
            cancellationToken);
    }

    private string? ReadCurrentMp3DiscIdentity(string driveRoot)
    {
        var analysis = _mp3DiscImportService.AnalyzeSource(driveRoot);
        if (!analysis.IsSupportedDataDisc)
        {
            ShowDiscTestInformation("Für den Duplikattest muss zuerst eine unterstützte MP3-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        return _mp3DiscImportService.CreateDiscSignature(driveRoot, analysis);
    }

    private string? ReadCurrentAudioDiscIdentity(string driveRoot)
    {
        var mediaKind = _discDriveService.GetMediaKindForPath(driveRoot);
        if (AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind))
        {
            ShowDiscTestInformation("Für den Audio-CD-Duplikattest muss zuerst eine lesbare Audio-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        var readResult = new AudioDiscReaderService().ReadDisc(driveRoot);
        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            ShowDiscTestInformation("Für den Duplikattest muss zuerst eine lesbare Audio-CD im ausgewählten Laufwerk liegen.");
            return null;
        }

        return readResult.Disc.DiscIdentity;
    }

    private void NotifyDiscTestPollingState(DiscPollingDisplayState state)
    {
        if (state is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate)
            _notificationService.Notify(NotificationEvent.Warning);
    }

    private static string CreateDiscDriveDisplayName(string driveRoot)
    {
        var root = Path.GetPathRoot(driveRoot)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar);
        return string.IsNullOrWhiteSpace(root) ? string.Empty : $"CD-Laufwerk {root}";
    }

    private string? SelectOpticalDrive()
    {
        var drives = _discDriveService.GetCdDriveShells();
        if (drives.Count == 0)
        {
            ShowDiscTestInformation("Windows hat kein optisches Laufwerk gemeldet.");
            return null;
        }

        var dialog = new DiscSourceSelectionDialog(drives, string.Empty, _settings.LastSelectedOpticalDrive)
        {
            Owner = this
        };
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    private void ShowDiscTestInformation(string message)
    {
        AppDialogService.Show(
            this,
            "BookStitch – CD-Test",
            "Test kann nicht gestartet werden",
            message,
            AppDialogKind.Information,
            buttons: [new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
    }

    private void CancelCurrentDiscTest()
    {
        _discTestCancellation?.Cancel();
        _discTestCancellation?.Dispose();
        _discTestCancellation = null;
    }

    private static string GetSimulationDisplayName(DeveloperDiscSimulationScenario scenario) => scenario switch
    {
        DeveloperDiscSimulationScenario.EmptyDrive => "Laufwerk leer",
        DeveloperDiscSimulationScenario.UnsupportedDisc => "falsche oder nicht unterstützte Disc",
        DeveloperDiscSimulationScenario.DuplicateEjected => "Duplikat erkannt und ausgeworfen",
        DeveloperDiscSimulationScenario.DuplicateManualEject => "Duplikat, manuelles Auswerfen nötig",
        DeveloperDiscSimulationScenario.SlowThenReady => "Laufwerk langsam, danach erkannt",
        DeveloperDiscSimulationScenario.Ready => "richtige Disc erkannt",
        _ => scenario.ToString()
    };

    private void InputPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppInputDialog(
            "BookStitch – Dialogvorschau",
            "Beispielwert eingeben",
            "Prüfe Eingabefeld, Fehlermeldung, Enter, Escape und die Fenstermechanik.",
            12,
            1,
            99)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void DiscSourcePreview_Click(object sender, RoutedEventArgs e)
    {
        var drives = new DiscDriveService().GetCdDriveShells();
        var dialog = new DiscSourceSelectionDialog(drives, string.Empty, _settings.LastSelectedOpticalDrive)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void AddDiscsPreview_Click(object sender, RoutedEventArgs e)
    {
        var drives = new DiscDriveService().GetCdDriveShells();
        var dialog = new AddMoreDiscsDialog(1, 2, 2, 2, 99, drives, _settings.LastSelectedOpticalDrive, string.Empty)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ResumeProjectPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResumeProjectDialog(
            _settingsService.ProjectRootFolder,
            new ProjectIndexService(),
            new ProjectResumePlanService(),
            _settings.ProjectRetentionDays)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ProjectSetupPreview_Click(object sender, RoutedEventArgs e)
    {
        var sourceKind = sender is Button { Tag: string tag } && Enum.TryParse<ProjectSetupSourceKind>(tag, out var parsed)
            ? parsed
            : ProjectSetupSourceKind.Folder;

        var sourceLabel = sourceKind switch
        {
            ProjectSetupSourceKind.AudioDisc => "Audio-CD in Laufwerk D:",
            ProjectSetupSourceKind.Mp3Disc => "MP3-CD in Laufwerk D:",
            _ => "C:\\Beispiel\\Hörbuch"
        };
        var windowTitle = sourceKind switch
        {
            ProjectSetupSourceKind.AudioDisc => "Audio-CD-Projekt vorbereiten",
            ProjectSetupSourceKind.Mp3Disc => "MP3-CD-Projekt vorbereiten",
            _ => "Lokales Projekt vorbereiten"
        };

        var request = new ProjectSetupDialogRequest(
            sourceKind,
            windowTitle,
            sourceLabel,
            "Diese Vorschau verändert keine Projektdaten. Prüfe Größe, Buttons, Eingabefelder, Ziehen und Schließen.",
            sourceKind == ProjectSetupSourceKind.Folder ? 1 : 2,
            1,
            99,
            ["AAC Stereo 128 kbps", "AAC Mono 64 kbps"],
            "AAC Stereo 128 kbps",
            _settings.SelectedParallelJobs,
            _settings.DefaultOutputExtension,
            _settings.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Beispielhörbuch",
            "Beispielhörbuch",
            "Beispielautor",
            "Beispielsprecher",
            _settings.DefaultGenre,
            _settings.DefaultFileNameTemplate,
            string.Empty,
            string.Empty,
            Path.Combine(_settingsService.ProjectRootFolder, "Covers"),
            _settings.MergeAutomaticallyAfterConversion,
            _settings.KeepAlbumLinkedToTitle,
            SourceFolder: sourceLabel,
            MaxSourceBitrateKbps: 320,
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty);

        var dialog = new DiscProjectSetupDialog(request) { Owner = this };
        dialog.ShowDialog();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
