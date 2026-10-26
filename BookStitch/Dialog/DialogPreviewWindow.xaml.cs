using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Dialog;

public partial class DialogPreviewWindow : Window
{
    private readonly GlobalSettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ProjectIndexService _projectIndexService;
    private readonly NotificationService _notificationService;
    private readonly DeveloperDiscTestService _developerDiscTestService = new();
    private readonly DiscDriveService _discDriveService = new();
    private readonly IDiscWaitDialogService _discWaitDialogService;
    private readonly Mp3DiscImportService _mp3DiscImportService = new();
    private bool _isLoadingSoundSettings;
    private CancellationTokenSource? _delayedFocusTestCancellation;
    private CancellationTokenSource? _discTestCancellation;

    public DialogPreviewWindow(
        GlobalSettingsManager settingsManager,
        SettingsService settingsService,
        NotificationService notificationService,
        ProjectIndexService? projectIndexService = null)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _settings = _settingsManager.Current;
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _projectIndexService = projectIndexService ?? new ProjectIndexService();
        var debugSettings = new DebugSettingsService(settingsService).Load();
        _discWaitDialogService = new SwitchableDiscWaitDialogService(() => debugSettings.UseBoxedDiscWaitDialog);
        InitializeComponent();
        Closed += (_, _) =>
        {
            _settingsManager.Flush();
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
            SelectComboBoxItem(PreviewEventProfileComboBox, EventProfileSettingsService.NormalizeProfile(_settings.EventProfile));
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
        _settingsManager.SaveImmediate();
    }

    private void PreviewEventProfileComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSoundSettings || sender is not ComboBox comboBox)
            return;

        _settings.EventProfile = GetSelectedEnum(comboBox, EventProfileSettingsService.DefaultProfile).ToString();
        _settingsManager.SaveImmediate();
    }

    private void PreviewSoundLibraryComboBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSoundSettings || sender is not ComboBox comboBox)
            return;

        _settings.SoundLibrary = GetSelectedEnum(comboBox, SoundSettingsService.DefaultLibrary).ToString();
        _settingsManager.SaveImmediate();
        _notificationService.PlaySoundPreview();
    }

    private void PreviewSoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSoundSettings)
            return;

        _settings.SoundVolumePercent = SoundSettingsService.NormalizeVolumePercent((int)Math.Round(e.NewValue));
        _settingsManager.SaveDebounced();
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
            LocalizationText.Get("Developer.PreviewTitle"),
            kind switch
            {
                AppDialogKind.Warning => LocalizationText.Get("Developer.SampleWarning"),
                AppDialogKind.Error => LocalizationText.Get("Developer.SampleError"),
                AppDialogKind.Question => LocalizationText.Get("Developer.SampleQuestion"),
                _ => LocalizationText.Get("Developer.SampleInformation")
            },
            LocalizationText.Get("Developer.PreviewPurpose"),
            kind,
            [LocalizationText.Get("Developer.DetailFirst"), LocalizationText.Get("Developer.DetailSecond")],
            kind == AppDialogKind.Question
                ? [
                    new AppDialogButton(LocalizationText.Get("Common.Yes"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                    new AppDialogButton(LocalizationText.Get("Common.No"), AppDialogResult.No),
                    new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
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
                DelayedFocusStatusTextBlock.Text = LocalizationText.Format("Developer.Countdown", button.Content, remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            DelayedFocusStatusTextBlock.Text = LocalizationText.Format("Developer.Triggered", button.Content);
            _notificationService.Notify(notificationEvent);
        }
        catch (OperationCanceledException)
        {
            DelayedFocusStatusTextBlock.Text = LocalizationText.Get("Developer.FocusCanceled");
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

        DiscTestStatusTextBlock.Text = LocalizationText.Format("Developer.SimulationStarted", GetSimulationDisplayName(scenario));
        try
        {
            var completed = await _discWaitDialogService.WaitForDiscAsync(
                this,
                new DiscWaitDialogRequest(
                    DiscNumber: 2,
                    TotalDiscs: 3,
                    MediaName: LocalizationText.Get("Disc.Media.Mp3"),
                    InitialInstruction: LocalizationText.Get("Developer.SimulationPreparing"),
                    HintText: LocalizationText.Get("Developer.SimulationNoDrive")),
                (_, _) => Task.FromResult(_developerDiscTestService.CreateSimulationResult(scenario, ++checkNumber)),
                status => DiscTestStatusTextBlock.Text = status,
                progress => DiscTestProgressTextBlock.Text = progress,
                cancellationToken);

            DiscTestStatusTextBlock.Text = completed == DiscWaitDialogOutcome.Ready
                ? LocalizationText.Format("Developer.SimulationCompleted", GetSimulationDisplayName(scenario))
                : LocalizationText.Format("Developer.SimulationPaused", GetSimulationDisplayName(scenario));
        }
        catch (OperationCanceledException)
        {
            DiscTestStatusTextBlock.Text = LocalizationText.Get("Developer.SimulationCanceled");
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
        _settingsManager.SaveImmediate();

        CancelCurrentDiscTest();
        _discTestCancellation = new CancellationTokenSource();
        var cancellationToken = _discTestCancellation.Token;

        try
        {
            var isAudio = tag.StartsWith("Audio", StringComparison.Ordinal);
            var duplicateTest = tag.EndsWith("Duplicate", StringComparison.Ordinal);
            var mediaName = isAudio ? LocalizationText.Get("Disc.Media.Audio") : LocalizationText.Get("Disc.Media.Mp3");

            HashSet<string> knownIdentities = [];
            if (duplicateTest)
            {
                var identity = isAudio
                    ? await ReadCurrentAudioDiscIdentityAsync(selectedDrive, cancellationToken)
                    : await ReadCurrentMp3DiscIdentityAsync(selectedDrive, cancellationToken);
                if (string.IsNullOrWhiteSpace(identity))
                    return;

                knownIdentities.Add(identity);
            }

            var ejected = await _discDriveService.TryEjectDiscAsync(selectedDrive, cancellationToken);
            DiscTestStatusTextBlock.Text = ejected
                ? LocalizationText.Format("Developer.HardwareEjected", mediaName)
                : LocalizationText.Format("Developer.HardwareInsert", mediaName);
            DiscTestProgressTextBlock.Text = duplicateTest
                ? LocalizationText.Get("Developer.DuplicateInstruction")
                : LocalizationText.Get("Developer.DetectionOnly");

            var completed = isAudio
                ? await RunAudioHybridTestAsync(selectedDrive, knownIdentities, cancellationToken)
                : await RunMp3HybridTestAsync(selectedDrive, knownIdentities, cancellationToken);

            DiscTestStatusTextBlock.Text = completed == DiscWaitDialogOutcome.Ready
                ? LocalizationText.Format("Developer.HardwareCompleted", mediaName)
                : LocalizationText.Format("Developer.HardwarePaused", mediaName);
        }
        catch (OperationCanceledException)
        {
            DiscTestStatusTextBlock.Text = LocalizationText.Get("Developer.HybridCanceled");
        }
        catch (Exception ex)
        {
            DiscTestStatusTextBlock.Text = LocalizationText.Get("Developer.HybridFailed");
            AppDialogService.Show(
                this,
                "BookStitch – " + LocalizationText.Get("Developer.HybridHardwareTest"),
                LocalizationText.Get("Developer.HybridFailed"),
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
                MediaName: LocalizationText.Get("Disc.Media.Mp3"),
                InitialInstruction: LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Dialog", 2, 3),
                HintText: LocalizationText.Get("Developer.AfterDetection"),
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDiscTestPollingState),
            (checkRequest, token) => pollingService.CheckDiscSourceForNextImportAsync(
                driveRoot, 2, 3, knownSignatures, cancellationToken: token, acceptDuplicate: checkRequest.AcceptNextDuplicate),
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
                MediaName: LocalizationText.Get("Disc.Media.Audio"),
                InitialInstruction: LocalizationText.Format("Disc.Polling.Audio.SourceNotReady.Dialog", 2, 3),
                HintText: LocalizationText.Get("Developer.AfterAudioDetection"),
                DriveDisplayName: CreateDiscDriveDisplayName(driveRoot),
                NotifyDisplayState: NotifyDiscTestPollingState),
            async (checkRequest, token) => (await pollingService.CheckNextDiscAsync(
                driveRoot, 2, 3, knownIdentities, token, checkRequest.AcceptNextDuplicate)).PollingResult,
            status => DiscTestStatusTextBlock.Text = status,
            progress => DiscTestProgressTextBlock.Text = progress,
            cancellationToken);
    }

    private async Task<string?> ReadCurrentMp3DiscIdentityAsync(string driveRoot, CancellationToken cancellationToken)
    {
        var result = await Task.Run(() =>
        {
            var analysis = _mp3DiscImportService.AnalyzeSource(driveRoot);
            return (analysis, signature: analysis.IsSupportedDataDisc
                ? _mp3DiscImportService.CreateDiscSignature(driveRoot, analysis)
                : null);
        }, cancellationToken);
        if (!result.analysis.IsSupportedDataDisc)
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateMp3Required"));
            return null;
        }

        return result.signature;
    }

    private async Task<string?> ReadCurrentAudioDiscIdentityAsync(string driveRoot, CancellationToken cancellationToken)
    {
        var mediaKind = await _discDriveService.GetMediaKindForPathAsync(driveRoot, cancellationToken);
        if (AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind))
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateAudioRequired"));
            return null;
        }

        var readResult = await Task.Run(() => new AudioDiscReaderService().ReadDisc(driveRoot), cancellationToken);
        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            ShowDiscTestInformation(LocalizationText.Get("Developer.DuplicateReadableAudioRequired"));
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
        return string.IsNullOrWhiteSpace(root) ? string.Empty : LocalizationText.Format("Developer.DriveName", root);
    }

    private string? SelectOpticalDrive()
    {
        var dialog = new DiscSourceSelectionDialog([], string.Empty, _settings.LastSelectedOpticalDrive, _settings.DiscDriveOrder, _discDriveService)
        {
            Owner = this
        };
        return ModalDialogService.ShowDialog(dialog, this) == true ? dialog.SelectedPath : null;
    }

    private void ShowDiscTestInformation(string message)
    {
        AppDialogService.Show(
            this,
            "BookStitch",
            LocalizationText.Get("Developer.TestUnavailable"),
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
        DeveloperDiscSimulationScenario.EmptyDrive => LocalizationText.Get("Developer.EmptyDrive"),
        DeveloperDiscSimulationScenario.UnsupportedDisc => LocalizationText.Get("Developer.UnsupportedDiscDescription"),
        DeveloperDiscSimulationScenario.DuplicateEjected => LocalizationText.Get("Developer.DuplicateDetected"),
        DeveloperDiscSimulationScenario.DuplicateManualEject => LocalizationText.Get("Developer.DuplicateManual"),
        DeveloperDiscSimulationScenario.SlowThenReady => LocalizationText.Get("Developer.SlowDriveDescription"),
        DeveloperDiscSimulationScenario.Ready => LocalizationText.Get("Developer.CorrectDisc"),
        _ => scenario.ToString()
    };

    private void InputPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppInputDialog(
            LocalizationText.Get("Developer.PreviewTitle"),
            LocalizationText.Get("Developer.NumberInput"),
            LocalizationText.Get("Developer.CheckInput"),
            12,
            1,
            99)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
    }

    private void DiscSourcePreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiscSourceSelectionDialog([], string.Empty, _settings.LastSelectedOpticalDrive, _settings.DiscDriveOrder, _discDriveService)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
    }

    private void AddDiscsPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddMoreDiscsDialog(1, 2, 2, 2, 99, [], _settings.LastSelectedOpticalDrive, string.Empty, _settings.DiscDriveOrder, _discDriveService)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
    }

    private void ResumeProjectPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ResumeProjectDialog(
            _settingsService.ProjectRootFolder,
            _projectIndexService,
            new ProjectResumePlanService(),
            _settings.ProjectRetentionDays,
            AppearanceSettingsService.GetEffectiveMode(_settings),
            selectedProject: null,
            projectRetentionMode: _settings.ProjectRetentionMode)
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
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
            ProjectSetupSourceKind.AudioDisc => LocalizationText.Get("MainWindow.Setup.AudioPrepare"),
            ProjectSetupSourceKind.Mp3Disc => LocalizationText.Get("MainWindow.Setup.Mp3Prepare"),
            _ => LocalizationText.Get("Developer.SetUpLocalProject")
        };

        var request = new ProjectSetupDialogRequest(
            sourceKind,
            windowTitle,
            sourceLabel,
            LocalizationText.Get("Developer.CheckSetup"),
            sourceKind == ProjectSetupSourceKind.Folder ? 1 : 2,
            1,
            99,
            ["AAC Stereo 128 kbps", "AAC Mono 64 kbps"],
            "AAC Stereo 128 kbps",
            _settings.SelectedParallelJobs,
            _settings.DefaultOutputExtension,
            _settings.OutputFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            LocalizationText.Get("Developer.SampleBook"),
            LocalizationText.Get("Developer.SampleBook"),
            "Beispielautor",
            "Beispielsprecher",
            _settings.DefaultGenre,
            _settings.DefaultFileNameTemplate,
            string.Empty,
            string.Empty,
            Path.Combine(_settingsService.ProjectRootFolder, "Covers"),
            _settings.MergeAutomaticallyAfterConversion,
            UsePrivateGenreList: _settings.UsePrivateGenreList,
            GenreOptions: GenreListService.GetGenres(_settings),
            SourceFolder: sourceLabel,
            MaxSourceBitrateKbps: 320,
            LastCoverFolder: _settings.LastCoverFolder ?? string.Empty);

        var dialog = new DiscProjectSetupDialog(
            request,
            AppearanceSettingsService.GetEffectiveMode(_settings))
        {
            Owner = this
        };
        ModalDialogService.ShowDialog(dialog, this);
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
