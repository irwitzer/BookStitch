using BookStitch;
using BookStitch.Dialog;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;
using WpfShutdownMode = System.Windows.ShutdownMode;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Release_wait_and_startup_message_dialogs_have_rounded_custom_chrome()
    {
        RunOnStaThread(() =>
        {
            foreach (var dialog in new System.Windows.Window[]
            {
                new BoxedDiscWaitDialog("MP3-CD 6 von 7", "Z: Test", "MP3-CD 6 von 7", "Leer", "Hinweis", true),
                new AppMessageDialog("BookStitch", "Hinweis", "Test", AppDialogKind.Information, null, null)
            })
            {
                try
                {
                    dialog.Show();
                    DrainDispatcher();
                    Assert.Equal(System.Windows.WindowStyle.None, dialog.WindowStyle);
                    var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(dialog);
                    Assert.NotNull(chrome);
                    Assert.Equal(new System.Windows.CornerRadius(8), chrome.CornerRadius);
                }
                finally { dialog.Close(); }
            }
        });
    }
    private static void ClickMinimalEditorToggle(ConceptMetadataFieldControl control)
    {
        var toggle = Assert.IsType<Button>(control.FindName("MinimalEditorToggleButton"));
        toggle.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        control.UpdateLayout();
        DrainDispatcher();
    }

    private static void RaiseConceptFieldEnter(ConceptMetadataFieldControl control)
    {
        using var source = new System.Windows.Interop.HwndSource(
            new System.Windows.Interop.HwndSourceParameters("BookStitch Concept field test")
            {
                Width = 1,
                Height = 1,
                WindowStyle = 0
            });
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        control.EditorComboBox.RaiseEvent(args);
        DrainDispatcher();
    }

    private static ComboBox CreateCandidateComboBox(params ConceptMetadataCandidate[] candidates)
    {
        var editorFactory = new System.Windows.FrameworkElementFactory(typeof(TextBox));
        editorFactory.Name = "PART_EditableTextBox";
        var template = new ControlTemplate(typeof(ComboBox))
        {
            VisualTree = editorFactory
        };
        var comboBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = candidates,
            Template = template
        };
        TextSearch.SetTextPath(comboBox, nameof(ConceptMetadataCandidate.DisplayValue));
        CandidateComboBoxInteraction.SetIsEnabled(comboBox, true);
        comboBox.ApplyTemplate();
        comboBox.Measure(new System.Windows.Size(400, 40));
        comboBox.Arrange(new System.Windows.Rect(0, 0, 400, 40));
        comboBox.UpdateLayout();
        comboBox.ApplyTemplate();
        return comboBox;
    }

    private static TextBox GetEditableTextBox(ComboBox comboBox)
    {
        comboBox.ApplyTemplate();
        return Assert.IsType<TextBox>(
            comboBox.Template.FindName("PART_EditableTextBox", comboBox));
    }

    private static void AssertAutocompleteStep(
        ComboBox comboBox,
        TextBox textBox,
        string input,
        string typedPrefix,
        string selectedCompletion)
    {
        Assert.True(CandidateComboBoxInteraction.ProcessTextInput(comboBox, input));
        Assert.Equal(typedPrefix + selectedCompletion, textBox.Text);
        Assert.Equal(typedPrefix.Length, textBox.SelectionStart);
        Assert.Equal(selectedCompletion.Length, textBox.SelectionLength);
        Assert.Equal(selectedCompletion, textBox.SelectedText);
    }

    private static ConceptMetadataFieldViewModel CreateConceptField(
        ProjectMetadata metadata,
        ProjectMetadataField fieldId)
    {
        return new ConceptMetadataFieldViewModel(
            () => metadata,
            new MetadataPreferences(),
            new MetadataNormalizationService(),
            fieldId,
            ConceptMetadataFieldCatalog.Get(fieldId),
            _ => { },
            _ => { });
    }

    private static void PrepareControl(ConceptMetadataFieldControl control)
    {
        control.ApplyTemplate();
        control.Measure(new System.Windows.Size(800, 300));
        control.Arrange(new System.Windows.Rect(0, 0, 800, 300));
        control.UpdateLayout();
        control.EditorComboBox.ApplyTemplate();
    }

    private static void RunWithComposerBindingDialog(
        Action<MetadataDialog, ProjectMetadataState, MetadataPreferences> assertion)
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Narrators,
                ["Sprecher A"],
                MetadataOrigin.User);
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Composers,
                ["Komponist A", "Komponist B"],
                MetadataOrigin.User);
            var preferences = new MetadataPreferences();
            MetadataFieldBindingPreferencesService.SetIsEnabled(
                preferences,
                ProjectMetadataFieldBindingCatalog.NarratorsToComposers,
                false);
            var dialog = CreateMetadataDialog(metadataState, preferences);

            try
            {
                assertion(dialog, metadataState, preferences);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static MetadataDialog CreateMetadataDialog(
        ProjectMetadataState metadataState,
        MetadataPreferences preferences,
        Action<ProjectMetadata>? saveMetadata = null,
        ObservableCollection<TrackInfo>? tracks = null,
        AppSettings? appSettings = null,
        Action? chapterSettingsChanged = null,
        Action? appSettingsChanged = null) =>
        new(
            usePrivateGenreList: false,
            openSettings: _ => { },
            metadataState: metadataState,
            appSettings: appSettings ?? new AppSettings(),
            canChooseCover: false,
            chooseCover: _ => { },
            removeCover: () => { },
            refreshUi: () => { },
            appSettingsChanged: appSettingsChanged,
            metadataPreferences: preferences,
            metadataPreferencesChanged: _ => { },
            projectFolderProvider: () => null,
            sourceSnapshotsProvider: () => [],
            metadataFieldDecisionsProvider: () => [],
            saveMetadata: saveMetadata ?? (_ => { }),
            tracks: tracks,
            chapterSettingsChanged: chapterSettingsChanged);

    private static SettingsWindow CreateSettingsWindow(
        AppSettings settings,
        GlobalSettingsManager settingsManager,
        SettingsService settingsService,
        NotificationService notificationService,
        Action<AppSettings>? applyAppearanceSettings = null,
        Action? chapterSettingsChanged = null,
        string? initialTabKey = null)
    {
        var constructor = typeof(SettingsWindow)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .Single();

        var arguments = constructor
            .GetParameters()
            .Select(parameter => CreateSettingsWindowArgument(
                parameter,
                settings,
                settingsManager,
                settingsService,
                notificationService,
                applyAppearanceSettings,
                chapterSettingsChanged,
                initialTabKey))
            .ToArray();

        return (SettingsWindow)(constructor.Invoke(arguments)
            ?? throw new InvalidOperationException(
                "SettingsWindow constructor returned null."));
    }

    private static object CreateSettingsWindowArgument(
        ParameterInfo parameter,
        AppSettings settings,
        GlobalSettingsManager settingsManager,
        SettingsService settingsService,
        NotificationService notificationService,
        Action<AppSettings>? applyAppearanceSettings,
        Action? chapterSettingsChanged,
        string? initialTabKey)
    {
        var parameterType = parameter.ParameterType;
        if (parameterType == typeof(GlobalSettingsManager))
        {
            return settingsManager;
        }

        if (parameterType == typeof(AppSettings))
        {
            return settings;
        }

        if (parameterType == typeof(SettingsService))
        {
            return settingsService;
        }

        if (parameterType == typeof(NotificationService))
        {
            return notificationService;
        }

        if (parameterType == typeof(INotifyPropertyChanged))
        {
            return new TestOutputFolderChangeSource();
        }

        if (parameterType == typeof(Action<AppSettings>) && applyAppearanceSettings is not null)
        {
            return applyAppearanceSettings;
        }

        if (parameterType == typeof(Action) && chapterSettingsChanged is not null)
        {
            return chapterSettingsChanged;
        }

        if (parameterType == typeof(CoverPickerService))
        {
            return new CoverPickerService(settings, () => { }, () => null);
        }

        if (parameterType == typeof(ProjectIndexService))
        {
            return new ProjectIndexService();
        }

        if (parameterType == typeof(ProjectResumePlanService))
        {
            return new ProjectResumePlanService();
        }

        if (parameterType == typeof(bool))
        {
            return false;
        }

        if (parameterType == typeof(IReadOnlyList<TrackInfo>))
        {
            return Array.Empty<TrackInfo>();
        }

        if (parameterType == typeof(IReadOnlyDictionary<Guid, string>))
        {
            return new Dictionary<Guid, string>();
        }

        if (parameterType == typeof(string))
        {
            return string.Equals(parameter.Name, "initialTabKey", StringComparison.Ordinal)
                ? initialTabKey!
                : null!;
        }

        if (typeof(Delegate).IsAssignableFrom(parameterType))
        {
            return CreateNoOpDelegate(parameterType);
        }

        throw new NotSupportedException(
            $"Unsupported SettingsWindow constructor parameter: {parameterType}.");
    }

    private sealed class TestOutputFolderChangeSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RaiseOutputFolderChanged() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("OutputFolder"));
    }

    private static void RunWithSettingsWindow(
        AppSettings settings,
        Action<SettingsWindow> assertion,
        Action<AppSettings>? applyAppearanceSettings = null,
        Action? chapterSettingsChanged = null,
        Action<SettingsService>? afterOpen = null,
        string? initialTabKey = null)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "BookStitch-WpfSettingsRuntimeTest-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(tempRoot, "projects");
        var appDataRoot = Path.Combine(tempRoot, "app-data");
        settings.WorkingFolder = projectRoot;
        Directory.CreateDirectory(tempRoot);

        try
        {
            var settingsService = new SettingsService(appDataRoot, projectRoot);
            using var settingsManager = new GlobalSettingsManager(
                () => settings,
                settingsService.Save,
                new TestGlobalSettingsSaveScheduler(),
                GlobalSettingsManager.DefaultDebounceDelay);
            using var notificationService = new NotificationService(
                new SoundNotificationService(
                    () => settings,
                    Path.Combine(tempRoot, "sounds")));
            var dialog = CreateSettingsWindow(
                settings,
                settingsManager,
                settingsService,
                notificationService,
                applyAppearanceSettings,
                chapterSettingsChanged,
                initialTabKey);

            try
            {
                afterOpen?.Invoke(settingsService);
                assertion(dialog);
            }
            finally
            {
                dialog.Close();
            }
        }
        finally
        {
            DeleteTestDirectoryWithRetry(tempRoot);
        }
    }

    private static void DeleteTestDirectoryWithRetry(string path)
    {
        const int maxAttempts = 6;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);

                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }
    }

    private sealed class TestGlobalSettingsSaveScheduler : IGlobalSettingsSaveScheduler
    {
        private Action? _pending;

        public void Schedule(TimeSpan delay, Action callback) => _pending = callback;

        public void Cancel() => _pending = null;

        public void Dispose() => Cancel();
    }

    private static Delegate CreateNoOpDelegate(Type delegateType)
    {
        var invokeMethod = delegateType.GetMethod("Invoke")
            ?? throw new InvalidOperationException(
                $"Delegate invoke method was not found: {delegateType}.");

        var parameters = invokeMethod
            .GetParameters()
            .Select(parameter => Expression.Parameter(
                parameter.ParameterType,
                parameter.Name))
            .ToArray();

        Expression body = invokeMethod.ReturnType == typeof(void)
            ? Expression.Empty()
            : invokeMethod.ReturnType == typeof(Task)
                ? Expression.Constant(Task.CompletedTask, typeof(Task))
                : Expression.Default(invokeMethod.ReturnType);

        return Expression.Lambda(
                delegateType,
                body,
                parameters)
            .Compile();
    }

    private static T? FindVisualDescendant<T>(System.Windows.DependencyObject root, Func<T, bool>? predicate = null)
        where T : System.Windows.DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match && (predicate is null || predicate(match)))
                return match;

            var nested = FindVisualDescendant(child, predicate);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static void RunOnStaThread(Action action, int timeoutSeconds = 20)
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplicationResources();
                // STA alone does not keep awaited UI work on this thread. Model the
                // context installed by a running WPF dispatcher before creating controls.
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                action();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        })
        {
            IsBackground = true,
            Name = "BookStitch WPF runtime test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(timeoutSeconds)),
            $"The WPF runtime-test thread did not finish within {timeoutSeconds} seconds.");

        if (capturedException is not null)
        {
            ExceptionDispatchInfo.Capture(capturedException).Throw();
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = WpfApplication.Current ?? new WpfApplication
        {
            ShutdownMode = WpfShutdownMode.OnExplicitShutdown
        };

        EnsureApplicationResourceDictionary(application, "/BookStitch;component/Themes/AppearanceTheme.xaml");
        EnsureApplicationResourceDictionary(application, "/BookStitch;component/Themes/WindowChromeStyles.xaml");
        EnsureApplicationResourceDictionary(application, "/BookStitch;component/Themes/NavigationDialogStyles.xaml");
        EnsureApplicationResourceDictionary(application, "/BookStitch;component/Themes/ProjectTypeVisuals.xaml");
        EnsureApplicationResourceDictionary(application, "/BookStitch;component/Themes/ChapterSymbols.xaml");
        foreach (var key in LocalizationText.GetKeys())
            application.Resources[key] = LocalizationText.Get(key);
    }

    private static void EnsureApplicationResourceDictionary(WpfApplication application, string source)
    {
        if (application.Resources.MergedDictionaries.Any(dictionary =>
                string.Equals(dictionary.Source?.OriginalString, source, StringComparison.Ordinal)))
        {
            return;
        }

        application.Resources.MergedDictionaries.Add(new WpfResourceDictionary
        {
            Source = new Uri(source, UriKind.Relative)
        });
    }

    private static void CloseWithoutDraftWarning(MetadataDialog dialog)
    {
        dialog.WarnBeforeDiscardingConceptDrafts = false;
        dialog.Close();
    }

    private sealed class SelectionTextState
    {
        public bool IsSelected { get; init; }
    }

}
