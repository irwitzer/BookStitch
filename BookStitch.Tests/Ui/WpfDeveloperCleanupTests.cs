using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Catalog_mouse_choice_commits_in_shared_editor_without_changing_other_field_behavior(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
        {
            foreach (var fieldId in new[] { ProjectMetadataField.Genres, ProjectMetadataField.Languages,
                ProjectMetadataField.OriginalLanguages, ProjectMetadataField.Authors })
            foreach (var mode in new[] { ConceptMetadataFieldPresentationMode.Standard, ConceptMetadataFieldPresentationMode.Minimal })
            {
                var metadata = new ProjectMetadata();
                var field = CreateConceptField(metadata, fieldId);
                var value = fieldId == ProjectMetadataField.Genres ? "Fantasy" :
                    fieldId == ProjectMetadataField.Authors ? "Example Author" : "en-GB";
                field.Candidates.Add(new ConceptMetadataCandidate(fieldId, value, value, 0, MetadataOrigin.StaticList));
                var control = new ConceptMetadataFieldControl { Width = 400, DataContext = field, PresentationMode = mode };
                var window = new Window { Width = 450, Height = 260, ShowInTaskbar = false, Content = control };
                try
                {
                    window.Show();
                    if (mode == ConceptMetadataFieldPresentationMode.Minimal)
                        ClickMinimalEditorToggle(control);
                    var combo = control.EditorComboBox;
                    combo.IsDropDownOpen = true;
                    DrainDispatcher();
                    var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                    combo.SelectedIndex = 0;
                    Assert.Empty(field.Values); // Keyboard browsing remains a draft.
                    item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                    DrainDispatcher();
                    if (fieldId == ProjectMetadataField.Authors)
                        Assert.Empty(field.Values);
                    else
                    {
                        Assert.Equal(value, Assert.Single(field.Values).Value);
                        Assert.True(string.IsNullOrEmpty(field.Draft));
                        Assert.False(combo.IsDropDownOpen);
                        field.IsLocked = true;
                        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                        DrainDispatcher();
                        Assert.Single(field.Values);
                    }
                }
                finally { window.Close(); }
            }
        }));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Developer_sidebar_visits_every_section_and_persists_sound_page(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "BookStitch-DeveloperCleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var settings = new AppSettings();
                var settingsService = new SettingsService(Path.Combine(root, "app-data"), Path.Combine(root, "projects"));
                using var manager = new GlobalSettingsManager(() => settings, settingsService.Save,
                    new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
                using var notifications = new NotificationService(new SoundNotificationService(() => settings, Path.Combine(root, "sounds")));
                var debug = new DebugSettings { LastTab = "Drives" }; // Retired persisted page falls back safely.
                var debugService = new DebugSettingsService(settingsService);
                var environment = new BookStitchEnvironmentContext(BookStitchEnvironment.Development, null, root, root);
                DeveloperToolsCommands.AccessState.Initialize(new AppSettings { DeveloperToolsEnabled = true, DeveloperToolsVisible = true },
                    BookStitchEnvironment.Production, () => true);
                var appearancePreview = DeveloperToolsCommands.AppearancePreview;
                appearancePreview.Clear();
                (int Seconds, bool Gold)? previewRequest = null;
                var previewRestored = false;
                var window = new DeveloperToolsWindow(manager, root, debug, debugService, environment,
                    new ProjectEnvironmentSelectionService(), () => { }, notifications, false,
                    _ => { }, _ => { }, _ => { }, _ => { }, appearancePreview.SetBackground, appearancePreview.SetTextColor, () => { },
                    () => Task.CompletedTask, () => "Preparing", () => "Ready", () => "0 %",
                    () => new ChapterDebugSnapshot("Auto", null, null, null, null, "", 0),
                    () => new MetadataDebugSnapshot([]), startProgressPreview: (seconds, gold) => previewRequest = (seconds, gold),
                    stopProgressPreview: () => previewRestored = true);
                try
                {
                    window.Show();
                    DrainDispatcher();
                    var navigation = Assert.IsType<ListBox>(window.FindName("DeveloperNavigationList"));
                    var restart = FindVisualDescendant<Button>(window,
                        button => Equals(button.Content, LocalizationText.Get("DeveloperTools.Restart")))!;
                    Assert.NotNull(restart);
                    Assert.True(restart.IsVisible);
                    Assert.True(restart.ActualWidth > 0 && restart.ActualHeight > 0);
                    Assert.True(restart.TranslatePoint(new Point(0, restart.ActualHeight), window).Y <= window.ActualHeight,
                        "The restart button must remain inside the visible window.");
                    Assert.True(KeyboardNavigation.GetTabIndex(navigation) < KeyboardNavigation.GetTabIndex(restart));
                    Assert.True(restart.TranslatePoint(new Point(), window).Y >
                        navigation.TranslatePoint(new Point(0, navigation.ActualHeight), window).Y);
                    Assert.Equal("Status", ((ListBoxItem)navigation.SelectedItem).Tag);
                    Assert.Equal(new[] { "Status", "Appearance", "Images", "Projects", "Tools", "Sounds", "Chapters", "Performance" },
                        navigation.Items.OfType<ListBoxItem>().Select(item => item.Tag!.ToString()).ToArray());
                    foreach (var item in navigation.Items.OfType<ListBoxItem>())
                    {
                        navigation.SelectedItem = item;
                        DrainDispatcher();
                        var host = Assert.IsType<Grid>(window.FindName("DeveloperContentHost"));
                        Assert.Single(host.Children.OfType<FrameworkElement>(), section => section.Visibility == Visibility.Visible);
                        Assert.Equal(item.Tag, host.Children.OfType<FrameworkElement>().Single(section => section.Visibility == Visibility.Visible).Tag);
                        if (Equals(item.Tag, "Images"))
                        {
                            Assert.Equal("Images", debugService.Load().LastTab);
                            var imageBackground = (ComboBox)window.FindName("ImagesBackgroundComboBox");
                            imageBackground.SelectedItem = imageBackground.Items.OfType<ComboBoxItem>().Single(choice => Equals(choice.Tag, "Ivory"));
                            DrainDispatcher();
                            Assert.Equal("Ivory", ((ComboBoxItem)((ComboBox)window.FindName("BackgroundPresetComboBox")).SelectedItem).Tag);
                            Assert.Equal("Black", settings.BackgroundPreset);
                            var originalRegularText = settings.TextColorPreset;
                            var imageText = (ComboBox)window.FindName("ImagesTextColorComboBox");
                            imageText.SelectedIndex = imageText.SelectedIndex == 0 ? imageText.Items.Count - 1 : 0;
                            DrainDispatcher();
                            Assert.Equal(((ComboBoxItem)imageText.SelectedItem).Tag,
                                ((ComboBoxItem)((ComboBox)window.FindName("TextColorPresetComboBox")).SelectedItem).Tag);
                            Assert.Equal(originalRegularText, settings.TextColorPreset);
                            var roles = (ItemsControl)window.FindName("ImagePreviewRolesItemsControl");
                            var rows = roles.Items.Cast<BookStitch.Presentation.DeveloperImagePreviewRow>().ToArray();
                            Assert.Equal(5, rows.Length);
                            var headerRow = rows.Single(row => row.Role == AppearanceAssetRole.HeaderBrand);
                            var selector = FindVisualDescendant<ComboBox>(roles, combo => ReferenceEquals(combo.DataContext, headerRow))!;
                            Assert.NotNull(selector);
                            Assert.True(selector.IsEnabled);
                            selector.SelectedValue = "HeaderBrand.Dark";
                            DrainDispatcher();
                            Assert.Equal("HeaderBrand.Dark", appearancePreview.GetImageId(AppearanceAssetRole.HeaderBrand));
                            Assert.Equal("Ivory", appearancePreview.Resolve(settings).BackgroundPreset);
                            selector.SelectedValue = "";
                            DrainDispatcher();
                            Assert.Null(appearancePreview.GetImageId(AppearanceAssetRole.HeaderBrand));
                        }
                        if (Equals(item.Tag, "Appearance"))
                        {
                            var seconds = (TextBox)window.FindName("ProgressPreviewSecondsTextBox");
                            var kind = (ComboBox)window.FindName("ProgressPreviewKindComboBox");
                            var start = (Button)window.FindName("TestProgressArtworkButton");
                            seconds.Text = "0";
                            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Null(previewRequest);
                            seconds.Text = "6";
                            kind.SelectedIndex = 1;
                            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal((6, true), previewRequest!.Value);
                            Assert.False(previewRestored);
                        }
                        if (language == "de" || (language == "fr" && Equals(item.Tag, "Projects")) ||
                            (language == "en" && Equals(item.Tag, "Sounds")))
                            SaveLayoutRepairPreview(window, $"developer-{language}-{item.Tag}");
                    }
                    var genres = Assert.IsType<CheckBox>(window.FindName("PrivateGenreListCheckBox"));
                    Assert.Same(window.FindResource("SettingsToggleStyle"), genres.Style);
                    var environmentCombo = Assert.IsType<ComboBox>(window.FindName("ProjectEnvironmentComboBox"));
                    Assert.Equal(27, environmentCombo.Height);
                    navigation.SelectedItem = navigation.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, "Sounds"));
                    DrainDispatcher();
                    Assert.Equal("Sounds", debugService.Load().LastTab);
                }
                finally { window.Close(); }
                Assert.True(previewRestored);
                appearancePreview.Clear();
                DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
            }
            finally { DeleteTestDirectoryWithRetry(root); }
        }));
    }

    [Fact]
    public void Small_drive_dialogs_have_native_rounding_and_rounded_list_templates()
    {
        RunOnStaThread(() =>
        {
            var drives = Enumerable.Range(0, 5).Select(index =>
                new DiscDriveInfo($"{(char)('U' + index)}:\\", $"{(char)('U' + index)}:", true,
                    $"Fixture {index + 1}", DiscMediaKind.Mp3Disc)).ToArray();
            var dialog = new DiscSourceSelectionDialog(drives, Path.GetTempPath(), "U:\\");
            try
            {
                dialog.ApplyTemplate();
                Assert.Equal(new CornerRadius(8), WindowChrome.GetWindowChrome(dialog).CornerRadius);
                var list = Assert.IsType<ListBox>(dialog.FindName("DriveList"));
                list.ApplyTemplate();
                Assert.Same(dialog.FindResource("RoundedDialogListBoxStyle"), list.Style);
                Assert.NotNull(FindVisualDescendant<Border>(list, border => border.CornerRadius == new CornerRadius(6)));
                var shell = Assert.IsType<Border>(dialog.Content);
                Assert.Equal(new CornerRadius(8), shell.CornerRadius);
                Assert.Equal(263, list.Height); // Five rows plus border and rounded-panel inset.
                shell.Measure(new Size(dialog.Width - 2, dialog.Height - 2));
                shell.Arrange(new Rect(0, 0, dialog.Width - 2, dialog.Height - 2));
                shell.UpdateLayout();
                SaveLayoutRepairPreview(shell, "rounded-drive-selection");
            }
            finally { dialog.Close(); }
        });
    }
}
