using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF: discovery, two-way selection and themed caption layout.
public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(BookStitchEnvironment.Production)]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Chapter_master_choice_follows_enabled_access_independent_of_wrench_visibility(BookStitchEnvironment environment)
    {
        RunOnStaThread(() =>
        {
            var state = DeveloperToolsCommands.AccessState;
            var accessSettings = new AppSettings { DeveloperToolsSelectorUnlocked = true };
            state.Initialize(accessSettings, environment, () => true);
            try
            {
                RunWithSettingsWindow(CreateChapterEditorSettings(), window =>
                {
                    window.Show();
                    DrainDispatcher();
                    var editor = (SettingsTagsChaptersControl)window.FindName("TagsChaptersSettingsControl");
                    AssertChapterMasterAccess((ComboBox)editor.FindName("ChapterModeComboBox"), state,
                        initiallyVisible: true);
                }, initialTabKey: "Chapters");

                state.Initialize(accessSettings = new AppSettings { DeveloperToolsSelectorUnlocked = true }, environment, () => true);
                var metadata = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences(),
                    appSettings: CreateChapterEditorSettings());
                try
                {
                    ((ListBox)metadata.FindName("ConceptUtilityFamilyList")).SelectedItem = metadata.FindName("ConceptChapterFamilyItem");
                    metadata.Show();
                    DrainDispatcher();
                    AssertChapterMasterAccess((ComboBox)metadata.FindName("ConceptChapterModeComboBox"), state,
                        initiallyVisible: true);
                }
                finally { metadata.Close(); }
            }
            finally { state.Initialize(new(), BookStitchEnvironment.Production, () => false); }
        });
    }

    private static void AssertChapterMasterAccess(ComboBox combo, DeveloperToolsAccessState state, bool initiallyVisible)
    {
        combo.IsDropDownOpen = true;
        DrainDispatcher();
        var master = combo.Items.OfType<ComboBoxItem>()
            .Single(item => Equals(item.Tag, ChapterSettingsService.ModeChapterMaster));
        Assert.Equal(initiallyVisible ? Visibility.Visible : Visibility.Collapsed, master.Visibility);
        Assert.Equal(initiallyVisible, master.IsEnabled);
        state.IsVisible = true;
        DrainDispatcher();
        Assert.Equal(Visibility.Visible, master.Visibility);
        Assert.True(master.IsEnabled);
        combo.SelectedItem = master;
        Assert.Same(master, combo.SelectedItem);
        state.IsVisible = false;
        DrainDispatcher();
        Assert.Equal(Visibility.Visible, master.Visibility);
        Assert.True(state.IsEnabled);
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        DrainDispatcher();
        Assert.Equal(Visibility.Collapsed, master.Visibility);
        Assert.False(master.IsEnabled);
        // Hiding developer access must not silently overwrite an existing chapter selection.
        Assert.Same(master, combo.SelectedItem);
        foreach (var item in combo.Items.OfType<ComboBoxItem>().Where(item => item != master))
        {
            Assert.Equal(Visibility.Visible, item.Visibility);
            Assert.True(item.IsEnabled);
        }
        for (var click = 0; click < 10; click++) state.RegisterDiscoveryClick();
        state.IsVisible = true;
        DrainDispatcher();
        Assert.Equal(Visibility.Visible, master.Visibility);
        Assert.True(master.IsEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_developer_conceal_restores_selector_selection(bool throws)
    {
        RunOnStaThread(() =>
        {
            var state = DeveloperToolsCommands.AccessState;
            var settings = new AppSettings { DeveloperToolsSelectorUnlocked = true, DeveloperToolsVisible = true };
            state.Initialize(settings, BookStitchEnvironment.Production, () => throws ? throw new System.IO.IOException("injected") : false);
            var info = new SettingsInfoControl();
            var host = new Window { Content = info, Width = 1200, Height = 600 };
            try
            {
                host.Show();
                DrainDispatcher();
                var selector = (ComboBox)info.FindName("DeveloperVisibilityComboBox");
                selector.SelectedValue = DeveloperToolsVisibilityChoice.Conceal;
                DrainDispatcher();
                Assert.True(state.IsVisible);
                Assert.Equal(Visibility.Visible, selector.Visibility);
                Assert.Equal(DeveloperToolsVisibilityChoice.Show, selector.SelectedValue);
            }
            finally
            {
                host.Close();
                state.Initialize(new(), BookStitchEnvironment.Production, () => false);
            }
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Developer_selector_discovery_and_visibility_work_in_every_ui_language(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
        {
            try
            {
                foreach (var environment in new[] { BookStitchEnvironment.Production, BookStitchEnvironment.Development, BookStitchEnvironment.XTester })
                {
                    var settings = new AppSettings();
                    var state = DeveloperToolsCommands.AccessState;
                    state.Initialize(settings, environment, () => true);
                    RunWithSettingsWindow(settings, dialog =>
                    {
                        dialog.Show();
                        DrainDispatcher();
                        var info = (SettingsInfoControl)dialog.FindName("InfoSettingsControl");
                        var name = (Button)info.FindName("DeveloperNameButton");
                        var selector = (ComboBox)info.FindName("DeveloperVisibilityComboBox");
                        var wrench = FindVisualDescendant<Button>(dialog, b => b.Command == DeveloperToolsCommands.OpenDeveloperToolsWindow)!;
                        Assert.NotNull(wrench);
                        Assert.Null(name.ToolTip);
                        Assert.Equal(environment == BookStitchEnvironment.Production ? Visibility.Collapsed : Visibility.Visible, selector.Visibility);
                        if (environment == BookStitchEnvironment.Production)
                        {
                            for (var i = 0; i < 9; i++) name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal(Visibility.Collapsed, selector.Visibility);
                            name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            DrainDispatcher();
                            Assert.Equal(Visibility.Visible, selector.Visibility);
                            Assert.Equal(DeveloperToolsVisibilityChoice.Hide, selector.SelectedValue);
                            Assert.Equal(Visibility.Collapsed, wrench.Visibility);
                        }
                        else
                        {
                            Assert.Equal(DeveloperToolsVisibilityChoice.Show, selector.SelectedValue);
                            Assert.Equal(Visibility.Visible, wrench.Visibility);
                        }
                        selector.SelectedValue = DeveloperToolsVisibilityChoice.Show;
                        DrainDispatcher();
                        Assert.True(state.IsVisible);
                        Assert.Equal(Visibility.Visible, wrench.Visibility);
                        Assert.True(selector.TransformToAncestor(info).Transform(new Point(selector.ActualWidth, 0)).X <= info.ActualWidth + 0.1);
                        SaveLayoutRepairPreview(dialog, language + "-" + environment + "-developer-access");
                        selector.SelectedValue = DeveloperToolsVisibilityChoice.Hide;
                        DrainDispatcher();
                        Assert.False(state.IsVisible);
                        Assert.True(state.IsEnabled);
                        Assert.Equal(Visibility.Collapsed, wrench.Visibility);
                        Assert.Equal(Visibility.Visible, selector.Visibility);
                        if (environment == BookStitchEnvironment.Production)
                        {
                            selector.SelectedValue = DeveloperToolsVisibilityChoice.Conceal;
                            DrainDispatcher();
                            Assert.False(state.IsSelectorAvailable);
                            Assert.Equal(Visibility.Collapsed, selector.Visibility);
                            Assert.Equal(Visibility.Collapsed, wrench.Visibility);
                            Assert.Equal(DeveloperToolsVisibilityChoice.Hide, selector.SelectedValue);
                            for (var i = 0; i < 9; i++) name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal(Visibility.Collapsed, selector.Visibility);
                            name.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            DrainDispatcher();
                            Assert.Equal(Visibility.Visible, selector.Visibility);
                        }
                    }, initialTabKey: "Info");
                }
            }
            finally { DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false); }
        }));
    }

    [Fact]
    public void Caption_hairline_tracks_light_and_dark_theme_without_geometry_changes()
    {
        RunOnStaThread(() =>
        {
            var header = new LargeResizableWindowHeader { Height = LargeResizableWindowHeader.SystemCaptionHeight };
            var window = new Window { Width = 600, Height = 100, Content = header };
            try
            {
                window.Show();
                DrainDispatcher();
                var line = FindVisualDescendant<Border>(header, b => b.BorderThickness == new Thickness(0, 0, 0, 1))!;
                Assert.NotNull(line);
                Color? previous = null;
                foreach (var preset in new[] { "Ivory", "Black" })
                {
                    AppearanceSettingsService.ApplyToResources(System.Windows.Application.Current.Resources,
                        new AppSettings { BackgroundPreset = preset });
                    DrainDispatcher();
                    Assert.Same(window.FindResource("PanelBorderBrush"), line.BorderBrush);
                    var color = Assert.IsType<SolidColorBrush>(line.BorderBrush).Color;
                    if (previous.HasValue) Assert.NotEqual(previous.Value, color);
                    previous = color;
                    Assert.Equal(new Thickness(0, 0, 0, 1), line.BorderThickness);
                    Assert.Equal(LargeResizableWindowHeader.SystemCaptionHeight, header.ActualHeight);
                }
            }
            finally
            {
                window.Close();
                AppearanceSettingsService.ApplyToResources(System.Windows.Application.Current.Resources, new AppSettings());
            }
        });
    }
}
