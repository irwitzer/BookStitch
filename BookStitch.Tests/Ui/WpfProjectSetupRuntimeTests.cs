using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(true, 44100, 44100)]
    [InlineData(true, 48000, 48000)]
    [InlineData(true, 44100, 48000)]
    [InlineData(true, 48000, 44100)]
    [InlineData(true, 96000, 48000)]
    [InlineData(false, 44100, 44100)]
    [InlineData(false, 88200, 44100)]
    public void AAC_decision_dialog_starts_off_and_explains_only_actual_conversion(bool full, int rate, int standard)
    {
        RunOnStaThread(() =>
        {
            var decision = new AacSampleRateDecision(rate, standard, true, full);
            var dialog = new AacSampleRateDialog(decision, "AAC Stereo 128 kbps");
            try
            {
                Assert.False(dialog.PreferStandard);
                Assert.Equal(full && rate == standard ? Visibility.Collapsed : Visibility.Visible,
                    dialog.PreferStandardToggle.Visibility);
                var before = dialog.Explanation.Text;
                dialog.PreferStandardToggle.IsChecked = true;
                Assert.True(dialog.PreferStandard);
                if (rate == standard) Assert.Equal(before, dialog.Explanation.Text);
                else Assert.Equal(LocalizationText.Format("Aac.SampleRate.ConvertExplanation", AacSampleRateDialog.FormatRate(standard)), dialog.Explanation.Text);
                dialog.PreferStandardToggle.IsChecked = false;
                Assert.Equal(before, dialog.Explanation.Text);
                Assert.Equal(AppDialogResult.None, dialog.Result);
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void Matching_AAC_pass_through_rate_keeps_notice_and_removes_entire_toggle_row_from_layout(int rate)
    {
        RunOnStaThread(() =>
        {
            var dialog = new AacSampleRateDialog(new AacSampleRateDecision(rate, rate, true, true), "AAC Stereo 96 kbps");
            try
            {
                Assert.Equal(LocalizationText.Get("Folder.PassThrough.Title"), dialog.HeadingText.Text);
                Assert.Equal(LocalizationText.Format("Folder.PassThrough.Message", "AAC Stereo 96 kbps",
                    AacSampleRateDialog.FormatRate(rate)), dialog.MessageText.Text);
                Assert.Equal(LocalizationText.Get("Aac.SampleRate.CopyExplanation"), dialog.Explanation.Text);
                var content = Assert.IsType<StackPanel>(dialog.Explanation.Parent);
                content.Measure(new Size(600, double.PositiveInfinity));
                content.Arrange(new Rect(new Point(), content.DesiredSize));
                Assert.Equal(new Size(), dialog.PreferStandardToggle.DesiredSize);
                Assert.Equal(0, dialog.Explanation.TranslatePoint(new Point(), content).Y);
                Assert.True(dialog.Explanation.ActualHeight > 0);
                Assert.False(dialog.PreferStandard);
                Assert.Equal(AppDialogResult.None, dialog.Result);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Conversion_settings_exposes_only_two_rates_and_saves_without_changing_existing_projects()
    {
        var settings = new AppSettings();
        SettingsService? persistence = null;
        RunOnStaThread(() => RunWithSettingsWindow(settings, dialog =>
        {
            dialog.Show();
            DrainDispatcher();
            var view = (SettingsConversionControl)dialog.FindName("ConversionSettingsControl");
            var combo = view.StandardSampleRateComboBox;
            Assert.Equal(new[] { 44100, 48000 }, combo.Items.Cast<ComboBoxItem>().Select(item => (int)item.Tag));
            Assert.Equal(44100, combo.SelectedValue);
            combo.SelectedValue = 48000;
            DrainDispatcher();
            Assert.Equal(48000, combo.SelectedValue);
            Assert.Equal(48000, settings.DefaultSampleRateHz);
            Assert.Contains("\"DefaultSampleRateHz\": 48000", File.ReadAllText(persistence!.SettingsFilePath));
            Assert.Equal(48000, persistence.Load().DefaultSampleRateHz);
            combo.SelectedValue = 44100;
            DrainDispatcher();
            Assert.Equal(44100, combo.SelectedValue);
            Assert.Equal(44100, settings.DefaultSampleRateHz);
            Assert.Equal(44100, persistence.Load().DefaultSampleRateHz);
        }, afterOpen: service => persistence = service, initialTabKey: "Conversion"));
    }

    [Fact]
    public void Folder_setup_preset_switch_reannounces_pass_through_after_returning_to_matching_bitrate()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder);
            try
            {
                dialog.PresetComboBox.ItemsSource = new[] { "AAC Mono 64 kbps", "AAC Stereo 128 kbps" };
                dialog.PresetComboBox.SelectedItem = "AAC Mono 64 kbps";
                var notice = new FolderPassThroughNoticeState();
                var track = new TrackInfo { Codec = "AAC", Extension = ".m4a", Channels = 2,
                    BitrateKbps = 128, SampleRateHz = 48000, TargetSampleRateHz = 48000 };
                var announcements = 0;
                var decisions = new List<string>();
                dialog.AcceptedPresetChanged += preset =>
                {
                    decisions.Add(preset);
                    if (notice.Update([track], ExportPreset.Parse(preset), true)) announcements++;
                };
                dialog.PresetComboBox.SelectedItem = "AAC Stereo 128 kbps";
                dialog.PresetComboBox.SelectedItem = "AAC Mono 64 kbps";
                dialog.PresetComboBox.SelectedItem = "AAC Stereo 128 kbps";
                Assert.Equal(2, announcements);
                Assert.Equal(new[] { "AAC Stereo 128 kbps", "AAC Mono 64 kbps", "AAC Stereo 128 kbps" }, decisions);
                Assert.Equal(48000, track.TargetSampleRateHz);
                Assert.False(File.Exists(track.FilePath)); // Preview has not started preparation.
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    public void Disc_resume_locks_preset_and_disc_boundary_but_keeps_metadata_and_output_editable(ProjectSetupSourceKind kind)
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(kind, defaultDiscCount: 5, isDiscResume: true);
            try
            {
                Assert.False(dialog.PresetComboBox.IsEnabled);
                Assert.False(dialog.DiscCountPanel.IsEnabled);
                Assert.True(dialog.ParallelJobsTextBox.IsEnabled);
                Assert.True(dialog.OutputExtensionComboBox.IsEnabled);
                Assert.True(dialog.SetupTitleField.IsEnabled);
                Assert.True(dialog.StartProjectButton.IsEnabled);
                Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), dialog.StartProjectButton.Content);
                Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"),
                    System.Windows.Automation.AutomationProperties.GetName(dialog.StartProjectButton));
                dialog.PresetComboBox.SelectedItem = "AAC Stereo 320 kbps";
                dialog.DiscCountState.Text = "9";
                typeof(DiscProjectSetupDialog).GetMethod("CaptureResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(dialog, [false]);
                Assert.Equal("AAC Standard", dialog.Result.SelectedExportPreset);
                Assert.Equal(5, dialog.Result.TotalDiscs);
            }
            finally { dialog.Close(); }
        });
    }
    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    [InlineData(ProjectSetupSourceKind.Folder)]
    public void Incomplete_project_setup_uses_continue_without_locking_normal_options(ProjectSetupSourceKind kind)
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(kind, isContinuation: true);
            try
            {
                Assert.Equal(LocalizationText.Get("MainWindow.Action.Continue"), dialog.StartProjectButton.Content);
                Assert.True(dialog.PresetComboBox.IsEnabled);
            }
            finally { dialog.Close(); }
        });
    }
    [Fact]
    public void Project_setup_left_right_navigation_uses_the_window_path_but_preserves_editor_arrow_keys()
    {
        RunOnStaThread(() =>
        {
            using var inputSource = CreateKeyboardInputSource("BookStitch project setup navigation test");
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0);

            try
            {
                dialog.Show();
                DrainDispatcher();

                RaiseKey(dialog.StartProjectButton, inputSource, Key.Right, Keyboard.PreviewKeyDownEvent);
                DrainDispatcher();
                Assert.Equal(1, dialog.CurrentSetupPage);

                RaiseKey(dialog.StartProjectButton, inputSource, Key.Left, Keyboard.PreviewKeyDownEvent);
                DrainDispatcher();
                Assert.Equal(0, dialog.CurrentSetupPage);

                dialog.SetupTitleField.FocusEditorAndSelectAll();
                DrainDispatcher();
                RaiseKey(dialog.SetupTitleField.EditorComboBox, inputSource, Key.Right, Keyboard.PreviewKeyDownEvent);
                DrainDispatcher();
                Assert.Equal(0, dialog.CurrentSetupPage);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_metadata_enter_does_not_start_the_project_and_start_button_remains_invokable()
    {
        RunOnStaThread(() =>
        {
            using var inputSource = CreateKeyboardInputSource("BookStitch project setup enter test");
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0);

            try
            {
                dialog.Show();
                DrainDispatcher();
                dialog.SetupTitleField.FocusEditorAndSelectAll();
                DrainDispatcher();

                RaiseKey(dialog.SetupTitleField.EditorComboBox, inputSource, Key.Enter, Keyboard.PreviewKeyDownEvent);
                RaiseKey(dialog.SetupTitleField.EditorComboBox, inputSource, Key.Enter, Keyboard.PreviewKeyDownEvent);
                DrainDispatcher();

                Assert.True(dialog.IsVisible);
            }
            finally
            {
                dialog.Close();
            }

            using var startInputSource = CreateKeyboardInputSource("BookStitch project setup start button test");
            var startDialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0);
            startDialog.Loaded += (_, _) => startDialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                Assert.True(startDialog.StartProjectButton.Focus());
                RaiseKey(startDialog.StartProjectButton, startInputSource, Key.Space, Keyboard.KeyDownEvent);
                RaiseKey(startDialog.StartProjectButton, startInputSource, Key.Space, Keyboard.KeyUpEvent);
            }));

            Assert.True(startDialog.ShowDialog());
        });
    }

    [Fact]
    public void Project_setup_pages_fill_the_viewport_without_cross_page_height_coupling()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0);

            try
            {
                dialog.Show();
                DrainDispatcher();

                Assert.True(dialog.SetupPage0.ComputedVerticalScrollBarVisibility != Visibility.Visible,
                    $"Setup height {dialog.ActualHeight}; viewport {dialog.SetupPage0.ViewportHeight}; extent {dialog.SetupPage0.ExtentHeight}; project card {dialog.SetupPage0ProjectCard.DesiredSize.Height}.");
                var initialPage0Height = dialog.SetupPage0ProjectCard.ActualHeight;
                dialog.Height += 120;
                DrainDispatcher();
                Assert.True(dialog.SetupPage0ProjectCard.ActualHeight > initialPage0Height);

                dialog.SetupPage0ProjectCard.MinHeight = 900;
                DrainDispatcher();
                Assert.True(dialog.NavigateSetupPage(1));
                DrainDispatcher();

                Assert.NotEqual(Visibility.Visible, dialog.SetupPage1.ComputedVerticalScrollBarVisibility);
                Assert.True(dialog.SetupPage1LeftCard.ActualHeight < 900);
                var initialPage1Height = dialog.SetupPage1LeftCard.ActualHeight;
                dialog.Height += 100;
                DrainDispatcher();
                Assert.True(dialog.SetupPage1LeftCard.ActualHeight > initialPage1Height);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_stable_scrollbar_gutter_preserves_card_horizontal_geometry_on_both_pages()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, getAnimationMilliseconds: () => 0);
            try
            {
                dialog.Show();
                DrainDispatcher();

                static (double X, double Width) Geometry(FrameworkElement card, Window host) =>
                    (card.TranslatePoint(new Point(0, 0), host).X, card.ActualWidth);
                static void AssertStable((double X, double Width) before, (double X, double Width) after)
                {
                    Assert.InRange(Math.Abs(after.X - before.X), 0, 0.5);
                    Assert.InRange(Math.Abs(after.Width - before.Width), 0, 0.5);
                }

                var page0Left = Geometry(dialog.SetupPage0ProjectCard, dialog);
                var page0Right = Geometry(dialog.SetupPage0MetadataCard, dialog);
                dialog.SetupPage0ProjectCard.MinHeight = 900;
                DrainDispatcher();
                Assert.Equal(Visibility.Visible, dialog.SetupPage0.ComputedVerticalScrollBarVisibility);
                AssertStable(page0Left, Geometry(dialog.SetupPage0ProjectCard, dialog));
                AssertStable(page0Right, Geometry(dialog.SetupPage0MetadataCard, dialog));

                Assert.True(dialog.NavigateSetupPage(1));
                DrainDispatcher();
                var page1Left = Geometry(dialog.SetupPage1LeftCard, dialog);
                var page1Right = Geometry(dialog.SetupPage1RightCard, dialog);
                dialog.SetupPage1LeftCard.MinHeight = 900;
                DrainDispatcher();
                Assert.Equal(Visibility.Visible, dialog.SetupPage1.ComputedVerticalScrollBarVisibility);
                AssertStable(page1Left, Geometry(dialog.SetupPage1LeftCard, dialog));
                AssertStable(page1Right, Geometry(dialog.SetupPage1RightCard, dialog));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_compatibility_field_uses_shared_providers_and_refreshes_after_metadata_return()
    {
        RunOnStaThread(() =>
        {
            var enabled = false;
            var title = "Vor Setup";
            var changedValues = new List<bool>();
            FinalAudioTagData Preview() => new() { Title = title, OutputPresentationCulture = "de-DE" };

            var dialog = CreateProjectSetupDialog(
                ProjectSetupSourceKind.Folder,
                getAnimationMilliseconds: () => 0,
                openMetadataDialog: _ =>
                {
                    enabled = false;
                    title = "Nach Metadaten";
                },
                compatibilityPreviewProvider: Preview,
                compatibilityDescriptionEnabledProvider: () => enabled,
                compatibilityDescriptionEnabledChanged: value =>
                {
                    enabled = value;
                    changedValues.Add(value);
                });

            try
            {
                dialog.Show();
                DrainDispatcher();

                var field = Assert.IsType<ConceptCompatibilityFieldControl>(dialog.SetupCompatibilityField);
                var toggle = Assert.IsType<ToggleButton>(field.FindName("CompatibilityDescriptionToggle"));
                var text = Assert.IsType<TextBox>(field.FindName("CompatibilityFieldTextBox"));

                Assert.False(toggle.IsChecked);
                Assert.Contains("Vor Setup", text.Text, StringComparison.Ordinal);

                toggle.IsChecked = true;
                DrainDispatcher();
                Assert.Equal([true], changedValues);
                Assert.True(enabled);

                dialog.MetadataButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                DrainDispatcher();

                Assert.False(toggle.IsChecked);
                Assert.Contains("Nach Metadaten", text.Text, StringComparison.Ordinal);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    [InlineData(ProjectSetupSourceKind.Folder)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    public void Project_setup_special_metadata_persists_preferences_while_owner_settings_lifecycle_is_alive(
        ProjectSetupSourceKind sourceKind)
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings();
            var saveCount = 0;
            using var settingsManager = new GlobalSettingsManager(
                () => settings,
                _ => saveCount++,
                new TestGlobalSettingsSaveScheduler(),
                GlobalSettingsManager.DefaultDebounceDelay);
            _ = settingsManager.Current;

            var dialog = CreateProjectSetupDialog(
                sourceKind,
                getAnimationMilliseconds: () => 0,
                metadataPreferences: settings.MetadataPreferences,
                metadataPreferencesChanged: _ => settingsManager.SaveImmediate());

            try
            {
                dialog.Show();
                DrainDispatcher();
                Assert.True(dialog.NavigateSetupPage(1));
                DrainDispatcher();

                var abridged = Assert.IsType<ConceptSpecialMetadataFieldState>(dialog.SetupAbridgedStatusField.DataContext);
                var workType = Assert.IsType<ConceptSpecialMetadataFieldState>(dialog.SetupWorkTypeField.DataContext);

                abridged.IsOutputEnabled = !abridged.IsOutputEnabled;
                workType.IsOutputEnabled = !workType.IsOutputEnabled;

                Assert.Equal(2, saveCount);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    [InlineData(ProjectSetupSourceKind.Folder)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    public void Project_setup_special_metadata_enum_dropdowns_open_without_resource_resolution_failure(
        ProjectSetupSourceKind sourceKind)
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateProjectSetupDialog(sourceKind, getAnimationMilliseconds: () => 0);

            try
            {
                dialog.Show();
                DrainDispatcher();
                Assert.True(dialog.NavigateSetupPage(1));
                DrainDispatcher();

                foreach (var field in new[] { dialog.SetupAbridgedStatusField, dialog.SetupWorkTypeField })
                {
                    var specialField = Assert.IsType<ConceptSpecialMetadataFieldControl>(field);
                    var editor = Assert.IsType<ComboBox>(specialField.FindName("EnumEditor"));

                    editor.IsDropDownOpen = true;
                    DrainDispatcher();
                    Assert.True(editor.IsDropDownOpen);

                    editor.IsDropDownOpen = false;
                    DrainDispatcher();
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Project_setup_disc_count_step_keys_and_wheel_clamp_and_publish_once_per_value_change()
    {
        RunOnStaThread(() =>
        {
            using var inputSource = CreateKeyboardInputSource("BookStitch project setup disc count test");
            var observed = new List<int>();
            var dialog = CreateProjectSetupDialog(
                ProjectSetupSourceKind.Mp3Disc,
                defaultDiscCount: 2,
                minimumDiscs: 1,
                maximumDiscs: 3,
                discCountChanged: observed.Add,
                getAnimationMilliseconds: () => 0);

            try
            {
                dialog.Show();
                DrainDispatcher();
                observed.Clear();

                RaiseKey(dialog.DiscCountTextBox, inputSource, Key.Up, Keyboard.PreviewKeyDownEvent);
                Assert.Equal("3", dialog.DiscCountTextBox.Text);
                Assert.Equal([3], observed);
                Assert.Equal(dialog.DiscCountTextBox.Text.Length, dialog.DiscCountTextBox.SelectionLength);

                RaiseKey(dialog.DiscCountTextBox, inputSource, Key.Up, Keyboard.PreviewKeyDownEvent);
                Assert.Equal([3], observed);

                RaiseKey(dialog.DiscCountTextBox, inputSource, Key.Down, Keyboard.PreviewKeyDownEvent);
                Assert.Equal("2", dialog.DiscCountTextBox.Text);
                Assert.Equal([3, 2], observed);

                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };
                dialog.DiscCountTextBox.RaiseEvent(wheel);
                Assert.Equal("1", dialog.DiscCountTextBox.Text);
                Assert.Equal([3, 2, 1], observed);

                dialog.DiscCountTextBox.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                });
                Assert.Equal([3, 2, 1], observed);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.Folder)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    public void Preparation_cover_drop_and_picker_share_import_and_metadata_state(ProjectSetupSourceKind kind)
    {
        RunOnStaThread(() =>
        {
            var state = new ProjectMetadataState();
            string? receivedPath = null;
            IDataObject? receivedData = null;
            Window? receivedOwner = null;
            var cancellations = 0;
            var dialog = CreateProjectSetupDialog(kind, metadataState: state,
                importCover: (path, data, owner) =>
                {
                    receivedPath = path;
                    receivedData = data;
                    receivedOwner = owner;
                    state.ApplyCoverMetadata("shared-source.png", "shared-cover.jpg", MetadataOrigin.User);
                    return System.Threading.Tasks.Task.CompletedTask;
                }, cancelCoverImport: () => cancellations++);
            try
            {
                dialog.Show();
                Assert.True(dialog.CoverBorder.AllowDrop);
                var data = new DataObject(DataFormats.FileDrop, new[] { "cover.png" });
                var importer = typeof(DiscProjectSetupDialog).GetMethod("ImportCoverAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                ((System.Threading.Tasks.Task)importer.Invoke(dialog, new object?[] { null, data })!).GetAwaiter().GetResult();
                Assert.Null(receivedPath);
                Assert.Same(data, receivedData);
                Assert.Same(dialog, receivedOwner);
                Assert.Equal("shared-cover.jpg", state.ProcessedCoverPath);
                var localPath = typeof(DiscProjectSetupDialog).GetField("_processedCoverPath",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.Equal(state.ProcessedCoverPath, localPath.GetValue(dialog));
                ((System.Threading.Tasks.Task)importer.Invoke(dialog, new object?[] { "chosen.png", null })!).GetAwaiter().GetResult();
                Assert.Equal("chosen.png", receivedPath);
                Assert.Null(receivedData);
                // A change from either other view is reflected in the preparation view too.
                state.ApplyCoverMetadata("other.png", "other.jpg", MetadataOrigin.User);
                Assert.Equal("other.jpg", localPath.GetValue(dialog));
            }
            finally { dialog.Close(); }
            Assert.Equal(1, cancellations);
        });
    }

    [Fact]
    public void Preparation_drop_processes_a_real_image_without_changing_the_source()
    {
        RunOnStaThread(() =>
        {
            using var folder = new BookStitch.Tests.TestHelpers.TemporaryFolder();
            var previousContext = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext());
            var source = folder.CreateFile("original.png");
            var bytes = BookStitch.Tests.TestHelpers.CoverTestImages.Create(600, 600);
            File.WriteAllBytes(source, bytes);
            var state = new ProjectMetadataState();
            var dialog = CreateProjectSetupDialog(ProjectSetupSourceKind.Mp3Disc, metadataState: state,
                coverWorkFolder: folder.Path, getCoverWarningThresholdPixels: () => 0);
            try
            {
                dialog.Show();
                var data = new DataObject(DataFormats.FileDrop, new[] { source });
                var importer = typeof(DiscProjectSetupDialog).GetMethod("ImportCoverAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var task = (System.Threading.Tasks.Task)importer.Invoke(dialog, new object?[] { null, data })!;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    DrainDispatcher();
                    System.Threading.Thread.Sleep(1);
                }
                Assert.True(task.IsCompleted);
                task.GetAwaiter().GetResult();
                Assert.True(File.Exists(state.ProcessedCoverPath));
                Assert.NotNull(dialog.CoverPreviewImage.Source);
                Assert.Equal(bytes, File.ReadAllBytes(source));
            }
            finally
            {
                dialog.Close();
                System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        });
    }

    private static DiscProjectSetupDialog CreateProjectSetupDialog(
        ProjectSetupSourceKind sourceKind,
        int defaultDiscCount = 1,
        int minimumDiscs = 1,
        int maximumDiscs = 99,
        Action<int>? discCountChanged = null,
        Func<int>? getAnimationMilliseconds = null,
        ProjectMetadataState? metadataState = null,
        MetadataPreferences? metadataPreferences = null,
        Action<MetadataPreferences>? metadataPreferencesChanged = null,
        Action<ProjectMetadata>? saveMetadata = null,
        Action<Window>? openMetadataDialog = null,
        Func<FinalAudioTagData>? compatibilityPreviewProvider = null,
        Func<bool>? compatibilityDescriptionEnabledProvider = null,
        Action<bool>? compatibilityDescriptionEnabledChanged = null,
        bool confirmDiscCountOnStart = false,
        bool isDiscResume = false,
        bool isContinuation = false,
        Func<bool>? shouldWarnDiscCount = null,
        Action<bool>? setAlwaysWarnDiscCount = null,
        Action<string>? setOutputFolder = null,
        GlobalSettingsManager? settingsManager = null,
        Func<string?, IDataObject?, Window, System.Threading.Tasks.Task>? importCover = null,
        Action? cancelCoverImport = null,
        string? coverWorkFolder = null,
        Func<int>? getCoverWarningThresholdPixels = null)
    {
        var workFolder = Path.GetTempPath();

        return new DiscProjectSetupDialog(
            new ProjectSetupDialogRequest(
                SourceKind: sourceKind,
                WindowTitle: "Projekt vorbereiten",
                SourceInformation: "Testquelle",
                Instruction: "Test",
                DefaultDiscCount: defaultDiscCount,
                MinimumDiscs: minimumDiscs,
                MaximumDiscs: maximumDiscs,
                ExportPresets: ["AAC Standard"],
                SelectedExportPreset: "AAC Standard",
                ParallelJobs: "Auto",
                OutputExtension: ".m4a",
                OutputFolder: workFolder,
                BookTitle: "Titel",
                Album: "Album",
                Author: "Autor",
                Narrator: "Sprecher",
                Genre: "Audiobook",
                FileNameTemplate: "{Autor} - {Titel}",
                CoverSourcePath: "",
                CoverPreviewSource: "",
                CoverWorkFolder: coverWorkFolder ?? workFolder,
                AutoMergeAfterConversion: false,
                SourceFolder: workFolder,
                ConfirmDiscCountOnStart: confirmDiscCountOnStart,
                IsDiscResume: isDiscResume,
                IsContinuation: isContinuation),
            AppearanceMode.Dark,
            setOutputFolder: setOutputFolder,
            discCountChanged: discCountChanged,
            metadataState: metadataState,
            metadataPreferences: metadataPreferences,
            metadataPreferencesChanged: metadataPreferencesChanged,
            saveMetadata: saveMetadata,
            openMetadataDialog: openMetadataDialog,
            compatibilityPreviewProvider: compatibilityPreviewProvider,
            compatibilityDescriptionEnabledProvider: compatibilityDescriptionEnabledProvider,
            compatibilityDescriptionEnabledChanged: compatibilityDescriptionEnabledChanged,
            getProjectSetupPageAnimationMilliseconds: getAnimationMilliseconds,
            shouldWarnDiscCount: shouldWarnDiscCount,
            setAlwaysWarnDiscCount: setAlwaysWarnDiscCount,
            settingsManager: settingsManager,
            importCover: importCover,
            cancelCoverImport: cancelCoverImport,
            getCoverWarningThresholdPixels: getCoverWarningThresholdPixels);
    }

    private static System.Windows.Interop.HwndSource CreateKeyboardInputSource(string name) =>
        new(new System.Windows.Interop.HwndSourceParameters(name)
        {
            Width = 1,
            Height = 1,
            WindowStyle = 0
        });

    private static void RaiseKey(
        UIElement target,
        System.Windows.Interop.HwndSource inputSource,
        Key key,
        RoutedEvent routedEvent)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, key)
        {
            RoutedEvent = routedEvent
        });
    }
}
