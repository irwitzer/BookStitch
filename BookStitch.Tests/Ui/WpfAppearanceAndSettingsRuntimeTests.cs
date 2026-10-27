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
    [Theory]
    [InlineData("de", "invalid")]
    [InlineData("en", "Black")]
    [InlineData("ja", "White")]
    public void Cover_transparency_setting_selects_persists_and_exposes_localized_help(string locale, string stored)
    {
        RunOnStaThread(() => WithSettingsCulture(locale, () =>
        {
            var settings = new AppSettings { CoverTransparencyMode = stored, LastSettingsTab = "Metadata" };
            RunWithSettingsWindow(settings, dialog =>
            {
                var owner = Assert.IsType<SettingsTagsChaptersControl>(dialog.FindName("TagsChaptersSettingsControl"));
                owner.ShowSection(SettingsTagsChaptersSection.Metadata);
                var combo = Assert.IsType<ComboBox>(owner.FindName("CoverTransparencyComboBox"));
                Assert.Equal(3, combo.Items.Count);
                Assert.Equal(CoverTransparencySettingsService.NormalizeMode(stored), ((ComboBoxItem)combo.SelectedItem).Tag);
                var info = Assert.IsType<InfoPoint>(owner.FindName("CoverTransparencyInfoPoint"));
                Assert.Equal(LocalizationText.Get("Cover.Transparency.Heading"), info.Title);
                Assert.Equal(LocalizationText.Get("Cover.Transparency.Info"), info.Body);
                Assert.Equal(LocalizationText.Get("Cover.Transparency.Heading"), System.Windows.Automation.AutomationProperties.GetName(combo));
                foreach (var mode in stored == "White" ? new[] { "Black", "White", "Auto" } : new[] { "White", "Black", "Auto" })
                {
                    combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, mode));
                    Assert.Equal(mode, settings.CoverTransparencyMode);
                    var persisted = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                        File.ReadAllText(SettingsService.GetSettingsFilePath(settings.WorkingFolder!)));
                    Assert.Equal(mode, persisted!.CoverTransparencyMode);
                }
            });
        }));
    }

    [Fact]
    public void Header_branding_uses_distinct_embedded_theme_images_and_reuses_frozen_decodes()
    {
        RunOnStaThread(() =>
        {
            var resources = new WpfResourceDictionary();
            var settings = new AppSettings { BackgroundPreset = nameof(BackgroundPreset.Black) };
            AppearanceSettingsService.ApplyToResources(resources, settings);
            var dark = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(resources[AppearanceResourceKeys.HeaderBrandImageSource]);
            Assert.True(dark.IsFrozen);
            Assert.True(dark.PixelWidth > dark.PixelHeight);
            var cropped = Assert.IsType<System.Windows.Media.Imaging.CroppedBitmap>(dark);
            Assert.True(cropped.PixelWidth < cropped.Source.PixelWidth);
            Assert.True(cropped.PixelHeight < cropped.Source.PixelHeight);
            settings.BackgroundPreset = nameof(BackgroundPreset.Ivory);
            AppearanceSettingsService.ApplyToResources(resources, settings);
            var ivory = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(resources[AppearanceResourceKeys.HeaderBrandImageSource]);
            Assert.True(ivory.IsFrozen);
            Assert.NotSame(dark, ivory);
            var alphaPixel = new byte[4];
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(ivory,
                System.Windows.Media.PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), alphaPixel, 4, 0);
            Assert.Equal(0, alphaPixel[3]);
            settings.BackgroundPreset = nameof(BackgroundPreset.DeepPetrol);
            AppearanceSettingsService.ApplyToResources(resources, settings);
            Assert.Same(dark, resources[AppearanceResourceKeys.HeaderBrandImageSource]);
        });
    }

    [Fact]
    public void Main_header_actions_fit_localized_captions_at_the_same_compact_height()
    {
        RunOnStaThread(() =>
        {
            var document = TestHelpers.RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            var actions = new[]
            {
                ("SelectFolder_Click", "UiOrdner"), ("SelectDiscSource_Click", "UiDisk"),
                ("OpenMetadataDialog_Click", "UiMetadaten"), ("OpenResumeProjects_Click", "UiProjekte")
            };
            var resourcesFolder = Path.GetDirectoryName(TestHelpers.RepositorySource.Find(
                ["BookStitch", "Resources", "LocalizationResources.de.resx"]))!;
            foreach (var file in Directory.EnumerateFiles(resourcesFolder, "LocalizationResources*.resx"))
            {
                var translations = System.Xml.Linq.XDocument.Load(file).Descendants("data")
                    .ToDictionary(element => (string)element.Attribute("name")!, element => element.Element("value")!.Value);
                var heights = new List<double>();
                foreach (var (click, key) in actions)
                {
                    var source = document.Descendants().Single(element => (string?)element.Attribute("Click") == click);
                    var label = new TextBlock { Text = translations[key] };
                    var button = new Button
                    {
                        Style = (System.Windows.Style)WpfApplication.Current.FindResource("PrimaryCompactButtonStyle"),
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                        FontSize = 12,
                        VerticalAlignment = Enum.TryParse<System.Windows.VerticalAlignment>(
                            (string?)source.Attribute("VerticalAlignment"), out var alignment)
                            ? alignment : System.Windows.VerticalAlignment.Stretch,
                        Padding = (System.Windows.Thickness)new System.Windows.ThicknessConverter().ConvertFromInvariantString(
                            (string)source.Attribute("Padding")!)!,
                        Content = label
                    };
                    button.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.Equal((double)WpfApplication.Current.FindResource("StandardButtonHeight"), button.DesiredSize.Height);
                    Assert.True(button.DesiredSize.Height >= label.DesiredSize.Height + button.Padding.Top + button.Padding.Bottom);
                    if (click == "OpenResumeProjects_Click")
                    {
                        // The settings action makes the real horizontal header taller than its captions.
                        var row = new StackPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(button);
                        row.Children.Add(new Button { Height = 38, Width = 44 });
                        row.Measure(new System.Windows.Size(600, 100));
                        row.Arrange(new System.Windows.Rect(0, 0, 600, row.DesiredSize.Height));
                        Assert.Equal(heights[0], button.ActualHeight);
                    }
                    heights.Add(button.DesiredSize.Height);
                }
                Assert.All(heights, height => Assert.Equal(heights[0], height));
            }
        });
    }

    [Fact]
    public void Metadata_banner_switches_between_distinct_cached_dark_and_ivory_images()
    {
        RunOnStaThread(() =>
        {
            var resources = new WpfResourceDictionary();
            var settings = new AppSettings();
            settings.BackgroundPreset = nameof(BackgroundPreset.Black);
            AppearanceSettingsService.ApplyToResources(resources, settings);
            var dark = Assert.IsType<System.Windows.Media.Imaging.BitmapImage>(
                resources[AppearanceSettingsService.MetadataBannerImageSourceKey]);
            Assert.True(dark.IsFrozen);
            Assert.True(dark.PixelWidth > 0 && dark.PixelHeight > 0);
            settings.BackgroundPreset = nameof(BackgroundPreset.Ivory);
            AppearanceSettingsService.ApplyToResources(resources, settings);
            var ivory = Assert.IsType<System.Windows.Media.Imaging.BitmapImage>(
                resources[AppearanceSettingsService.MetadataBannerImageSourceKey]);
            Assert.True(ivory.IsFrozen);
            Assert.NotSame(dark, ivory);
            Assert.Same(AppearanceSettingsService.LoadArtwork(AppearanceAssetRole.MetadataBanner,
                AppearanceAssetCatalog.GetOptional(AppearanceAssetRole.MetadataBanner, AppearanceMode.Light)!.RelativePath), ivory);
            Assert.True(ivory.PixelWidth > 0 && ivory.PixelHeight > 0);
            foreach (var preset in BackgroundPresetCatalog.All)
            {
                settings.BackgroundPreset = preset.Preset.ToString();
                AppearanceSettingsService.ApplyToResources(resources, settings);
                if (preset.Mode == AppearanceMode.Light)
                    Assert.Same(ivory, resources[AppearanceSettingsService.MetadataBannerImageSourceKey]);
                else
                    Assert.Same(dark, resources[AppearanceSettingsService.MetadataBannerImageSourceKey]);
            }
            settings.BackgroundPreset = nameof(BackgroundPreset.Black);
            AppearanceSettingsService.ApplyToResources(resources, settings);
            Assert.Same(dark, resources[AppearanceSettingsService.MetadataBannerImageSourceKey]);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_chip_drag_state_resolves_brushes_and_survives_layout(bool series)
    {
        RunOnStaThread(() =>
        {
            var value = new ConceptSingleValue("Author A");
            var seriesValue = new ConceptSeriesEntryValue("Series A", "1");
            ContentControl owner = series ? new ConceptSeriesFieldControl() : new ConceptMetadataFieldControl();
            var badge = new Border
            {
                DataContext = series ? seriesValue : (object)value,
                Child = new TextBlock { Text = "Test chip" },
                Style = (System.Windows.Style)owner.FindResource(series
                    ? "ConceptSeriesValueBadgeStyle" : "ConceptValueBadgeStyle")
            };
            owner.Content = badge;
            var host = new System.Windows.Window { Content = owner, ShowInTaskbar = false };
            try
            {
                host.Show();
                DrainDispatcher();
                if (series) seriesValue.IsBeingDragged = true;
                else value.IsDragging = true;
                Assert.IsAssignableFrom<System.Windows.Media.Brush>(badge.BorderBrush);
                Assert.IsAssignableFrom<System.Windows.Media.Brush>(badge.Background);
                badge.Measure(new System.Windows.Size(300, 100));
                badge.Arrange(new System.Windows.Rect(0, 0, 300, 100));
                if (series) seriesValue.IsBeingDragged = false;
                else value.IsDragging = false;
                Assert.IsAssignableFrom<System.Windows.Media.Brush>(badge.BorderBrush);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void General_settings_reset_warnings_persists_without_affecting_language_selection()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings { UiLanguage = UiLanguageIds.English, CoverWarningThresholdPixels = CoverWarningSettingsService.Disabled, SuppressedWarnings = [WarningSuppressionService.DiscCount, WarningSuppressionService.ProjectFolder, "future.warning"] };
            settings.MetadataPreferences.WarnBeforeDiscardingConceptDrafts = false;
            RunWithSettingsWindow(settings, dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var general = Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl"));
                var reset = Assert.IsType<Button>(general.FindName("ResetWarningsButton"));
                Assert.Null(general.FindName("LanguageHeadingTextBlock"));
                var warningRow = Assert.IsType<StackPanel>(reset.Parent);
                Assert.Single(warningRow.Children.OfType<InfoPoint>());
                reset.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
                var stored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsService.GetSettingsFilePath(settings.WorkingFolder!)))!;
                Assert.Empty(stored.SuppressedWarnings);
                Assert.Equal(CoverWarningSettingsService.Threshold2000Pixels, stored.CoverWarningThresholdPixels);
                Assert.True(stored.MetadataPreferences.WarnBeforeDiscardingConceptDrafts);
                Assert.Equal(UiLanguageIds.English, stored.UiLanguage);
            });
        });
    }

    [Fact]
    public void Central_combo_box_template_builds_editable_and_selection_modes_with_single_root_chrome()
    {
        RunOnStaThread(() =>
        {
            var style = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("AppearanceComboBoxStyle"));
            var comboBox = new ComboBox
            {
                IsEditable = true,
                Width = 220,
                Style = style,
                ItemsSource = new[] { "Alpha", "Beta" }
            };
            var host = new System.Windows.Window
            {
                Content = comboBox,
                ShowInTaskbar = false
            };

            try
            {
                host.Show();
                DrainDispatcher();
                comboBox.ApplyTemplate();

                var chrome = Assert.IsType<Border>(comboBox.Template.FindName("ComboBoxChrome", comboBox));
                var editor = Assert.IsType<TextBox>(comboBox.Template.FindName("PART_EditableTextBox", comboBox));
                Assert.Equal(System.Windows.Visibility.Visible, editor.Visibility);
                Assert.Equal(new System.Windows.Thickness(0), editor.BorderThickness);
                Assert.Equal((byte)0, Assert.IsType<System.Windows.Media.SolidColorBrush>(editor.Background).Color.A);
                Assert.Equal(comboBox.BorderThickness, chrome.BorderThickness);

                comboBox.IsEditable = false;
                DrainDispatcher();
                Assert.Equal(System.Windows.Visibility.Hidden, editor.Visibility);
                Assert.NotNull(comboBox.Template.FindName("ContentSite", comboBox));
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Context_submenu_style_opens_child_popup_and_keeps_leaf_item_reachable()
    {
        RunOnStaThread(() =>
        {
            var menuStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("BookStitchContextMenuStyle"));
            var submenuStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("BookStitchContextSubmenuItemStyle"));
            var leafStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("BookStitchContextMenuItemStyle"));
            var owner = new Button { Width = 80, Height = 28 };
            var child = new MenuItem { Header = "Child", Style = leafStyle };
            var parent = new MenuItem { Header = "Parent", Style = submenuStyle };
            parent.Items.Add(child);
            var menu = new ContextMenu
            {
                Style = menuStyle,
                PlacementTarget = owner,
                Placement = PlacementMode.Bottom
            };
            menu.Items.Add(parent);
            var host = new System.Windows.Window
            {
                Content = owner,
                ShowInTaskbar = false
            };

            try
            {
                host.Show();
                DrainDispatcher();
                menu.IsOpen = true;
                DrainDispatcher();
                parent.IsSubmenuOpen = true;
                DrainDispatcher();

                parent.ApplyTemplate();
                var popup = Assert.IsType<Popup>(parent.Template.FindName("PART_Popup", parent));
                Assert.True(parent.IsSubmenuOpen);
                Assert.True(popup.IsOpen);
                Assert.True(child.IsVisible);
                Assert.NotNull(System.Windows.PresentationSource.FromVisual(child));
            }
            finally
            {
                parent.IsSubmenuOpen = false;
                menu.IsOpen = false;
                host.Close();
            }
        });
    }

    [Fact]
    public void Source_launch_nested_content_follows_primary_and_secondary_button_foregrounds()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            AppearanceSettingsService.ApplyToResources(
                WpfApplication.Current.Resources,
                new AppSettings
                {
                    BackgroundPreset = BackgroundPreset.Ivory.ToString(),
                    TextColorPreset = TextColorPreset.Ink.ToString()
                });
            var expectedPrimary = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                WpfApplication.Current.FindResource(AppearanceSettingsService.PrimaryButtonForegroundBrushKey));
            var contentStyle = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("ButtonContentForegroundTextStyle"));
            var primary = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("PrimaryButtonStyle"));
            var secondary = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("SecondaryButtonStyle"));
            var settingsStyle = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("GlobalSettingsButtonStyle"));
            var texts = new[]
            {
                new TextBlock { Text = "▣", Style = contentStyle },
                new TextBlock { Text = "Ordner", Style = contentStyle },
                new TextBlock { Text = "◉", Style = contentStyle },
                new TextBlock { Text = "Disk", Style = contentStyle }
            };
            var folder = new Button { Style = primary, Content = new StackPanel { Children = { texts[0], texts[1] } } };
            var disc = new Button { Style = primary, Content = new StackPanel { Children = { texts[2], texts[3] } } };
            var settingsButton = new Button { Style = settingsStyle };
            var host = new System.Windows.Window { Content = new StackPanel { Children = { folder, disc, settingsButton } }, ShowInTaskbar = false };

            try
            {
                host.Show();
                DrainDispatcher();
                Assert.Equal(expectedPrimary.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(folder.Foreground).Color);
                Assert.Equal(expectedPrimary.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(disc.Foreground).Color);
                foreach (var text in texts)
                    Assert.Equal(expectedPrimary.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(text.Foreground).Color);

                var settingsGlyph = FindVisualDescendant<TextBlock>(settingsButton, text => text.Text == UiSymbolResources.Settings);
                Assert.NotNull(settingsGlyph);
                Assert.Equal(
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(settingsButton.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(settingsGlyph.Foreground).Color);
                settingsButton.Foreground = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                    WpfApplication.Current.FindResource(AppearanceSettingsService.AccentBrushKey));
                DrainDispatcher();
                Assert.Equal(
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(settingsButton.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(settingsGlyph.Foreground).Color);

                folder.Style = secondary;
                disc.Style = secondary;
                DrainDispatcher();
                Assert.Equal(Assert.IsType<System.Windows.Media.SolidColorBrush>(folder.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(texts[0].Foreground).Color);
                Assert.Equal(Assert.IsType<System.Windows.Media.SolidColorBrush>(folder.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(texts[1].Foreground).Color);
                Assert.Equal(Assert.IsType<System.Windows.Media.SolidColorBrush>(disc.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(texts[2].Foreground).Color);
                Assert.Equal(Assert.IsType<System.Windows.Media.SolidColorBrush>(disc.Foreground).Color,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(texts[3].Foreground).Color);
            }
            finally
            {
                host.Close();
                AppearanceSettingsService.ApplyToResources(
                    WpfApplication.Current.Resources,
                    new AppSettings());
            }
        });
    }

    [Fact]
    public void Accent_selection_styles_propagate_on_accent_foreground_to_visible_content()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            AppearanceSettingsService.ApplyToResources(
                WpfApplication.Current.Resources,
                new AppSettings());
            var expected = Assert.IsType<System.Windows.Media.SolidColorBrush>(
                WpfApplication.Current.FindResource("OnAccentTextBrush"));
            var tabStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("AccentSelectionTabItemStyle"));
            var listStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("AccentSelectionListBoxItemStyle"));
            var primaryTextStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("AccentSelectionPrimaryTextStyle"));
            var tertiaryTextStyle = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("AccentSelectionTertiaryTextStyle"));

            var tab = new TabItem { Header = "Darstellung", Style = tabStyle };
            var tabs = new TabControl();
            tabs.Items.Add(tab);
            tabs.SelectedItem = tab;

            var itemText = new TextBlock { Text = "G:" };
            var item = new ListBoxItem { Content = itemText, Style = listStyle };
            var list = new ListBox();
            list.Items.Add(item);
            list.SelectedItem = item;

            var primaryStateText = new TextBlock
            {
                Text = "Titel",
                Style = primaryTextStyle,
                DataContext = new SelectionTextState { IsSelected = true }
            };
            var tertiaryStateText = new TextBlock
            {
                Text = "Kapitel",
                Style = tertiaryTextStyle,
                DataContext = new SelectionTextState { IsSelected = true }
            };

            var host = new System.Windows.Window
            {
                Content = new StackPanel { Children = { tabs, list, primaryStateText, tertiaryStateText } },
                ShowInTaskbar = false
            };

            try
            {
                host.Show();
                DrainDispatcher();

                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(tab.Foreground).Color);
                var header = FindVisualDescendant<TextBlock>(tab, text => text.Text == "Darstellung");
                Assert.NotNull(header);
                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(header.Foreground).Color);
                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(item.Foreground).Color);
                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(itemText.Foreground).Color);
                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(primaryStateText.Foreground).Color);
                Assert.Equal(expected.Color, Assert.IsType<System.Windows.Media.SolidColorBrush>(tertiaryStateText.Foreground).Color);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Disc_source_selection_dialog_executes_compiled_xaml_on_sta_thread()
    {
        RunOnStaThread(() =>
        {
            var dialog = new DiscSourceSelectionDialog(
                Array.Empty<DiscDriveInfo>(),
                Path.GetTempPath(),
                preferredDriveRoot: null);

            try
            {
                Assert.NotNull(dialog.Content);
                Assert.NotNull(dialog.Icon);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Large_resizable_window_runtime_chrome_preserves_resize_maximize_and_header_interaction_contracts()
    {
        RunOnStaThread(() =>
        {
            var style = Assert.IsType<System.Windows.Style>(
                WpfApplication.Current.FindResource("LargeResizableWindowStyle"));
            var header = new LargeResizableWindowHeader();
            var actionButton = new Button { Content = "Action" };
            header.HeaderActions = actionButton;
            var window = new System.Windows.Window
            {
                Width = 720,
                Height = 480,
                ShowInTaskbar = false,
                Style = style,
                Content = header
            };

            try
            {
                window.Show();
                DrainDispatcher();
                window.ApplyTemplate();
                header.ApplyTemplate();

                var chrome = Assert.IsType<System.Windows.Shell.WindowChrome>(
                    System.Windows.Shell.WindowChrome.GetWindowChrome(window));
                Assert.Equal(System.Windows.ResizeMode.CanResizeWithGrip, window.ResizeMode);
                Assert.Equal(new System.Windows.Thickness(0), chrome.GlassFrameThickness);
                Assert.True(chrome.ResizeBorderThickness.Left > 0);
                Assert.Equal(46d, chrome.CaptionHeight);

                var shell = Assert.IsType<Border>(window.Template.FindName("LargeResizableWindowShell", window));
                Assert.Equal(new System.Windows.CornerRadius(8), shell.CornerRadius);
                Assert.True(DialogWindowBehavior.GetIsDragRegion(header));
                Assert.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(header));
                Assert.True(DialogWindowBehavior.IsInteractiveDragSource(actionButton, header));
                Assert.False(DialogWindowBehavior.IsInteractiveDragSource(new TextBlock(), header));

                var minimizeButton = Assert.IsAssignableFrom<ButtonBase>(header.Template.FindName("PART_MinimizeButton", header));
                var maximizeRestoreButton = Assert.IsAssignableFrom<ButtonBase>(header.Template.FindName("PART_MaximizeRestoreButton", header));
                var closeButton = Assert.IsAssignableFrom<ButtonBase>(header.Template.FindName("PART_CloseButton", header));
                Assert.False(header.ShowMinimize);
                Assert.Equal(System.Windows.Visibility.Collapsed, minimizeButton.Visibility);
                header.ShowMinimize = true;
                DrainDispatcher();
                Assert.Equal(System.Windows.Visibility.Visible, minimizeButton.Visibility);

                minimizeButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
                DrainDispatcher();
                Assert.Equal(System.Windows.WindowState.Minimized, window.WindowState);
                window.WindowState = System.Windows.WindowState.Normal;
                DrainDispatcher();

                maximizeRestoreButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
                DrainDispatcher();
                Assert.Equal(System.Windows.WindowState.Maximized, window.WindowState);
                Assert.Equal(new System.Windows.CornerRadius(0), shell.CornerRadius);

                maximizeRestoreButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
                DrainDispatcher();
                Assert.Equal(System.Windows.WindowState.Normal, window.WindowState);
                Assert.Equal(new System.Windows.CornerRadius(8), shell.CornerRadius);

                var closeRequested = false;
                header.CloseRequested += (_, _) => closeRequested = true;
                closeButton.RaiseEvent(new System.Windows.RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(closeRequested);

                window.ResizeMode = System.Windows.ResizeMode.NoResize;
                Assert.False(DialogWindowBehavior.TryToggleMaximizeRestore(window));
                Assert.Equal(System.Windows.WindowState.Normal, window.WindowState);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Button_families_match_the_main_header_height()
    {
        RunOnStaThread(() =>
        {
            var height = (double)WpfApplication.Current.FindResource("StandardButtonHeight");
            var styles = new[] { "PrimaryCompactButtonStyle", "PrimaryButtonStyle", "SecondaryButtonStyle",
                "SecondaryCompactButtonStyle", "DangerButtonStyle", "QuietDangerButtonStyle",
                "SettingsPrimaryButtonStyle", "SettingsSecondaryButtonStyle", "SettingsDestructiveButtonStyle",
                "UtilityIconButtonStyle", "DialogCloseButtonStyle", "IconButtonStyle", "HiddenTextButtonStyle" };
            var buttons = styles.Select(key => new Button
            {
                Content = "Action",
                Style = (System.Windows.Style)WpfApplication.Current.FindResource(key)
            }).ToArray();
            foreach (var button in buttons)
            {
                button.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.Equal(height, button.DesiredSize.Height);
            }
        });
    }

    [Fact]
    public void Numeric_steppers_use_their_own_shorter_central_height()
    {
        RunOnStaThread(() =>
        {
            var stepper = new RepeatButton { Content = "▲",
                Style = (System.Windows.Style)WpfApplication.Current.FindResource("StepperRepeatButtonStyle") };
            var down = new RepeatButton { Content = "▼", Margin = new System.Windows.Thickness(4, 0, 0, 0),
                Style = stepper.Style };
            var reference = new Button { Content = "20", Width = 64, Margin = new System.Windows.Thickness(0, 0, 4, 0),
                Style = (System.Windows.Style)WpfApplication.Current.FindResource("SecondaryCompactButtonStyle") };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new System.Windows.Thickness(12) };
            row.Children.Add(reference); row.Children.Add(stepper); row.Children.Add(down);
            var window = new System.Windows.Window { Content = row, Width = 200, Height = 100, ShowInTaskbar = false };
            try
            {
                window.Show();
                DrainDispatcher();
                Assert.Equal((double)WpfApplication.Current.FindResource("StepperButtonHeight"), stepper.ActualHeight);
                Assert.True(stepper.ActualHeight < reference.ActualHeight);
                SaveLayoutRepairPreview(window, "numeric-stepper");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Settings_button_variants_share_compact_height_and_corner_geometry()
    {
        RunOnStaThread(() =>
        {
            var buttons = new[]
            {
                new Button { Content = "Primary", Style = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("SettingsPrimaryButtonStyle")) },
                new Button { Content = "Secondary", Style = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("SettingsSecondaryButtonStyle")) },
                new Button { Content = "Delete", Style = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("SettingsDestructiveButtonStyle")) }
            };
            var host = new System.Windows.Window
            {
                Content = new StackPanel { Children = { buttons[0], buttons[1], buttons[2] } },
                ShowInTaskbar = false
            };
            try
            {
                host.Show();
                DrainDispatcher();
                foreach (var button in buttons)
                {
                    button.ApplyTemplate();
                    Assert.Equal((double)WpfApplication.Current.FindResource("StandardButtonHeight"), button.ActualHeight);
                    var border = Assert.IsType<Border>(button.Template.FindName("ButtonBorder", button));
                    Assert.Equal(new System.Windows.CornerRadius(8), border.CornerRadius);
                    Assert.Equal(System.Windows.VerticalAlignment.Center, button.VerticalContentAlignment);
                }
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void Combo_box_content_width_tracks_the_widest_item_and_respects_max_width()
    {
        RunOnStaThread(() =>
        {
            var comboBox = new ComboBox
            {
                Style = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("AppearanceCompactComboBoxStyle")),
                MaxWidth = 240
            };
            ComboBoxContentWidth.SetIsEnabled(comboBox, true);
            comboBox.Items.Add("Kurz");
            comboBox.Items.Add("Ein deutlich längerer Eintrag");
            var host = new System.Windows.Window { Content = comboBox, ShowInTaskbar = false };

            try
            {
                host.Show();
                DrainDispatcher();
                var firstWidth = comboBox.Width;
                Assert.True(firstWidth > comboBox.MinWidth);
                Assert.True(firstWidth <= comboBox.MaxWidth);

                comboBox.Items.Add("Ein noch wesentlich längerer nachträglich geladener und lokalisierter Eintrag");
                DrainDispatcher();
                Assert.True(comboBox.Width >= firstWidth);
                Assert.True(comboBox.Width <= comboBox.MaxWidth);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void Editable_combo_box_width_tracks_visible_text_without_exceeding_its_localization_limit()
    {
        RunOnStaThread(() =>
        {
            var comboBox = new ComboBox
            {
                IsEditable = true,
                MinWidth = 100,
                MaxWidth = 240,
                Text = "Kurz",
                Style = Assert.IsType<System.Windows.Style>(WpfApplication.Current.FindResource("AppearanceCompactComboBoxStyle"))
            };
            ComboBoxContentWidth.SetIsEnabled(comboBox, true);
            var host = new System.Windows.Window { Content = comboBox, ShowInTaskbar = false };
            try
            {
                host.Show();
                DrainDispatcher();
                var shortWidth = comboBox.ActualWidth;
                comboBox.Text = "Ein sehr langer sichtbarer benutzerdefinierter Dateiname mit zusätzlichen Platzhaltern";
                DrainDispatcher();

                Assert.True(comboBox.ActualWidth > shortWidth);
                Assert.Equal(240d, comboBox.ActualWidth);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void Settings_window_executes_compiled_xaml_on_sta_thread()
    {
        RunOnStaThread(() =>
        {
            RunWithSettingsWindow(
                new AppSettings(),
                dialog =>
                {
                    dialog.Show();
                    DrainDispatcher();

                    Assert.True(dialog.IsVisible);
                    Assert.NotNull(dialog.Content);
                    Assert.NotNull(dialog.Icon);
                    Assert.True(Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl")).IsLoaded);
                    var general = Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl"));
                    var appearance = Assert.IsType<SettingsAppearanceControl>(general.FindName("AppearanceSettingsControl"));
                    Assert.True(appearance.IsLoaded);
                    var output = Assert.IsType<TextBlock>(general.FindName("OutputFolderPathTextBlock"));
                    var expectedLeft = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(output.Parent)
                        .TranslatePoint(new System.Windows.Point(), general).X;
                    foreach (var name in new[] { "EventProfileComboBox", "SoundVolumeSlider", "ResetWarningsButton" })
                    {
                        var control = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(general.FindName(name));
                        Assert.Equal(expectedLeft, control.TranslatePoint(new System.Windows.Point(), general).X, 1);
                    }
                    var background = Assert.IsType<ComboBox>(appearance.FindName("BackgroundPresetComboBox"));
                    var textColors = Assert.IsType<ComboBox>(appearance.FindName("TextColorPresetComboBox"));
                    Assert.Equal(expectedLeft, background.TranslatePoint(new System.Windows.Point(), general).X, 1);
                    Assert.Equal(background.TranslatePoint(new System.Windows.Point(), general).Y,
                        textColors.TranslatePoint(new System.Windows.Point(), general).Y, 1);
                    var events = Assert.IsType<ComboBox>(general.FindName("EventProfileComboBox"));
                    var sounds = Assert.IsType<ComboBox>(general.FindName("SoundLibraryComboBox"));
                    Assert.InRange(background.ActualWidth / events.ActualWidth, 0.84, 0.90);
                    Assert.Equal(background.ActualWidth, textColors.ActualWidth, 1);
                    Assert.True(textColors.TranslatePoint(new System.Windows.Point(), general).X >
                        sounds.TranslatePoint(new System.Windows.Point(), general).X);
                    var navigation = Assert.IsType<ListBox>(dialog.FindName("SettingsNavigationList"));
                    Assert.DoesNotContain(navigation.Items.OfType<ListBoxItem>(), item => Equals(item.Tag, "Appearance"));
                });
        });
    }

    [Fact]
    public void Settings_appearance_selection_applies_a_compatible_text_palette()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings
            {
                BackgroundPreset = BackgroundPreset.Black.ToString(),
                TextColorPreset = TextColorPreset.WarmSand.ToString()
            };
            AppSettings? appliedSettings = null;

            RunWithSettingsWindow(
                settings,
                dialog =>
                {
                    var owner = Assert.IsType<SettingsAppearanceControl>(Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl")).FindName("AppearanceSettingsControl"));
                    var background = Assert.IsType<ComboBox>(owner.FindName("BackgroundPresetComboBox"));
                    var ivory = background.Items
                        .OfType<ComboBoxItem>()
                        .Single(item => Equals(item.Tag, BackgroundPreset.Ivory.ToString()));

                    background.SelectedItem = ivory;
                    DrainDispatcher();

                    var textPalette = Assert.IsType<ComboBox>(owner.FindName("TextColorPresetComboBox"));
                    var selectedText = Assert.IsType<ComboBoxItem>(textPalette.SelectedItem);
                    Assert.Equal(BackgroundPreset.Ivory.ToString(), settings.BackgroundPreset);
                    Assert.Equal(TextColorPreset.Ink.ToString(), settings.TextColorPreset);
                    Assert.Equal(TextColorPreset.Ink.ToString(), selectedText.Tag);
                    Assert.Same(settings, appliedSettings);
                },
                applied => appliedSettings = applied);
        });
    }

    [Fact]
    public void Settings_navigation_restores_records_and_maps_legacy_sections_without_duplicate_owners()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings { LastSettingsTab = "TagsChapters" };

            RunWithSettingsWindow(
                settings,
                dialog =>
                {
                    var navigation = Assert.IsType<ListBox>(dialog.FindName("SettingsNavigationList"));
                    var selected = Assert.IsType<ListBoxItem>(navigation.SelectedItem);
                    Assert.Equal("Metadata", selected.Tag);

                    var owner = Assert.IsType<SettingsTagsChaptersControl>(dialog.FindName("TagsChaptersSettingsControl"));
                    var metadata = Assert.IsType<StackPanel>(owner.FindName("MetadataSettingsSection"));
                    var chapters = Assert.IsType<StackPanel>(owner.FindName("ChaptersSettingsSection"));
                    Assert.Equal(System.Windows.Visibility.Visible, metadata.Visibility);
                    Assert.Equal(System.Windows.Visibility.Collapsed, chapters.Visibility);

                    navigation.SelectedItem = navigation.Items.OfType<ListBoxItem>().Single(item => Equals(item.Tag, "Chapters"));
                    DrainDispatcher();
                    Assert.Equal("Chapters", settings.LastSettingsTab);
                    Assert.Equal(System.Windows.Visibility.Collapsed, metadata.Visibility);
                    Assert.Equal(System.Windows.Visibility.Visible, chapters.Visibility);
                });

            settings.LastSettingsTab = "Appearance";
            RunWithSettingsWindow(
                settings,
                dialog =>
                {
                    var navigation = Assert.IsType<ListBox>(dialog.FindName("SettingsNavigationList"));
                    Assert.Equal("Drive", Assert.IsType<ListBoxItem>(navigation.SelectedItem).Tag);
                    Assert.Equal("Appearance", settings.LastSettingsTab);
                },
                initialTabKey: "Drive");
        });
    }

    [Fact]
    public void Settings_navigation_arrow_keys_wrap_visible_sections_and_preserve_combo_box_input()
    {
        RunOnStaThread(() =>
        {
            RunWithSettingsWindow(
                new AppSettings(),
                dialog =>
                {
                    dialog.Show();
                    DrainDispatcher();

                    var navigation = Assert.IsType<ListBox>(dialog.FindName("SettingsNavigationList"));
                    var utilityNavigation = Assert.IsType<ListBox>(dialog.FindName("SettingsUtilityNavigationList"));
                    ListBoxItem? Selected() => navigation.SelectedItem as ListBoxItem ?? utilityNavigation.SelectedItem as ListBoxItem;
                    var visibleSections = navigation.Items.OfType<ListBoxItem>().Concat(utilityNavigation.Items.OfType<ListBoxItem>())
                        .Where(item => item.Visibility == System.Windows.Visibility.Visible)
                        .ToArray();
                    Assert.Equal(new[] { "General", "Conversion", "Metadata", "Chapters", "ProjectManagement", "Drive", "Language", "Info", "FAQ" },
                        visibleSections.Select(item => (string)item.Tag));

                    navigation.SelectedItem = visibleSections[0];
                    for (var index = 1; index < visibleSections.Length; index++)
                    {
                        var args = RaiseKeyDown(dialog, Key.Right);
                        DrainDispatcher();
                        Assert.True(args.Handled);
                        Assert.Same(visibleSections[index], Selected());
                    }

                    var wrappedRight = RaiseKeyDown(dialog, Key.Right);
                    DrainDispatcher();
                    Assert.True(wrappedRight.Handled);
                    Assert.Same(visibleSections[0], Selected());

                    var wrappedLeft = RaiseKeyDown(dialog, Key.Left);
                    DrainDispatcher();
                    Assert.True(wrappedLeft.Handled);
                    Assert.Same(visibleSections[^1], Selected());

                    navigation.SelectedItem = visibleSections[0];
                    var general = Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl"));
                    var comboBox = Assert.IsType<ComboBox>(general.FindName("EventProfileComboBox"));
                    comboBox.Focus();
                    Keyboard.Focus(comboBox);
                    DrainDispatcher();

                    var selectedBeforeInputKey = navigation.SelectedItem;
                    RaiseKeyDown(comboBox, Key.Right);
                    DrainDispatcher();
                    Assert.Same(selectedBeforeInputKey, navigation.SelectedItem);
                });
        });
    }

    private static KeyEventArgs RaiseKeyDown(System.Windows.UIElement source, Key key)
    {
        var presentationSource = System.Windows.PresentationSource.FromVisual(source)
            ?? throw new InvalidOperationException("The key source is not connected to a presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        source.RaiseEvent(args);
        return args;
    }

    [Fact]
    public void Settings_chapter_selection_persists_notifies_and_refreshes_the_preview()
    {
        RunOnStaThread(() =>
        {
            var settings = CreateChapterEditorSettings();
            var changeCount = 0;
            string? persistedModeWhenNotified = null;

            RunWithSettingsWindow(
                settings,
                dialog =>
                {
                    var owner = Assert.IsType<SettingsTagsChaptersControl>(dialog.FindName("TagsChaptersSettingsControl"));
                    var mode = Assert.IsType<ComboBox>(owner.FindName("ChapterModeComboBox"));
                    var preview = Assert.IsType<TextBlock>(owner.FindName("ChapterPreviewTextBlock"));
                    var initialPreview = preview.Text;

                    SelectValue(mode, ChapterSettingsService.ModeOriginal);
                    DrainDispatcher();

                    Assert.Equal(ChapterSettingsService.ModeOriginal, settings.ChapterSettings!.Mode);
                    Assert.Equal(1, changeCount);
                    Assert.Equal(ChapterSettingsService.ModeOriginal, persistedModeWhenNotified);
                    Assert.NotEqual(initialPreview, preview.Text);
                },
                chapterSettingsChanged: () =>
                {
                    changeCount++;
                    var json = File.ReadAllText(SettingsService.GetSettingsFilePath(settings.WorkingFolder!));
                    persistedModeWhenNotified = System.Text.Json.JsonSerializer
                        .Deserialize<AppSettings>(json)?
                        .ChapterSettings?
                        .Mode;
                });
        });
    }

    [Fact]
    public void Metadata_cover_warning_selection_updates_shared_app_settings()
    {
        RunOnStaThread(() =>
        {
            var settings = new AppSettings();
            var changeCount = 0;
            var dialog = CreateMetadataDialog(
                new ProjectMetadataState(),
                new MetadataPreferences(),
                appSettings: settings,
                appSettingsChanged: () => changeCount++);

            try
            {
                var threshold = Assert.IsType<ComboBox>(dialog.FindName("ConceptCoverWarningThresholdComboBox"));
                Assert.Equal(
                    CoverWarningSettingsPresentation.Options.Select(option => option.ThresholdPixels),
                    threshold.Items
                        .OfType<ComboBoxItem>()
                        .Select(item => Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture)));

                Assert.Equal(1000, Convert.ToInt32(((ComboBoxItem)threshold.SelectedItem).Tag, CultureInfo.InvariantCulture));
                threshold.SelectedItem = threshold.Items
                    .OfType<ComboBoxItem>()
                    .Single(item => Convert.ToInt32(item.Tag, CultureInfo.InvariantCulture) == 500);
                DrainDispatcher();

                Assert.Equal(500, settings.CoverWarningThresholdPixels);
                Assert.Equal(1, changeCount);
            }
            finally
            {
                CloseWithoutDraftWarning(dialog);
            }
        });
    }

}
