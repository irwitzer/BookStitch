using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF: these contracts require real layout, bindings and HWND window state.
public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Settings_export_options_align_with_path_and_info_fits_default_width(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
            RunWithSettingsWindow(new AppSettings(), dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var view = (SettingsGeneralControl)dialog.FindName("GeneralSettingsControl");
                var path = (TextBlock)view.FindName("OutputFolderPathTextBlock");
                var pathBorder = (Border)path.Parent;
                var merge = (CheckBox)view.FindName("MergeAutomaticallyCheckBox");
                var awake = (CheckBox)view.FindName("KeepComputerAwakeCheckBox");
                var pathX = pathBorder.TransformToAncestor(view).Transform(new Point()).X;
                var mergeX = merge.TransformToAncestor(view).Transform(new Point()).X;
                Assert.InRange(mergeX - pathX, -0.1, 0.1);
                foreach (var toggle in new[] { merge, awake })
                {
                    var panel = (StackPanel)toggle.Parent;
                    var info = Assert.Single(panel.Children.OfType<InfoPoint>());
                    Assert.False(string.IsNullOrWhiteSpace(info.Body));
                    Assert.True(info.TransformToAncestor(view).Transform(new Point(info.ActualWidth, 0)).X <= view.ActualWidth);
                }
                SaveLayoutRepairPreview(dialog, language + "-general-new");
            })));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Project_integrity_and_retention_use_full_width_without_horizontal_scrolling(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
            RunWithSettingsWindow(new AppSettings(), dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var view = (SettingsProjectManagementControl)dialog.FindName("ProjectManagementSettingsControl");
                var preview = (ScrollViewer)view.FindName("ProjectPreviewScrollViewer");
                var border = (Border)preview.Parent;
                Assert.True(border.ActualHeight >= 150, $"Preview height: {border.ActualHeight}");
                var button = (Button)view.FindName("DeleteSelectedProjectButton");
                Assert.Equal(LocalizationText.Get("Settings.Projects.DeleteSelected"), button.Content);
                Assert.Equal(HorizontalAlignment.Right, button.HorizontalAlignment);
                var deleteAll = FindVisualDescendant<Button>(view, b => Equals(b.Content, LocalizationText.Get("UiAlleProjekteLoeschen")))!;
                var integrity=(StackPanel)view.FindName("ProjectIntegritySection");
                var retention=(StackPanel)view.FindName("ProjectRetentionSection");
                Assert.InRange(Math.Abs(integrity.ActualWidth-retention.ActualWidth),0,0.1);
                Assert.True(integrity.ActualWidth>view.ActualWidth-60);
                Assert.True(retention.TransformToAncestor(view).Transform(new Point()).Y<integrity.TransformToAncestor(view).Transform(new Point()).Y);
                Assert.False(((CheckBox)view.FindName("FullContentHashesCheckBox")).IsChecked);
                var outer=(ScrollViewer)view.FindName("IntegrityRetentionScrollViewer");
                Assert.Equal(ScrollBarVisibility.Disabled,outer.HorizontalScrollBarVisibility);
                Assert.Equal(0,outer.ScrollableWidth);
                SaveLayoutRepairPreview(dialog, language + "-projects-new");
            }, initialTabKey: "ProjectManagement")));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Faq_navigation_keeps_english_content_and_selection_across_categories(string language)
    {
        RunOnStaThread(() => WithSettingsCulture(language, () =>
            RunWithSettingsWindow(new AppSettings(), dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var view = (SettingsFaqControl)dialog.FindName("FaqSettingsControl");
                Assert.Equal(Visibility.Visible, view.Visibility);
                var combo = (ComboBox)view.FindName("QuestionsComboBox");
                void AssertSelectedQuestionText()
                {
                    combo.ApplyTemplate();
                    var presenter = Assert.IsType<ContentPresenter>(combo.Template.FindName("ContentSite", combo));
                    var text = FindVisualDescendant<TextBlock>(presenter, _ => true);
                    Assert.NotNull(text);
                    Assert.Equal(Assert.IsType<FaqEntry>(combo.SelectedItem).Question, text.Text);
                }
                AssertSelectedQuestionText();
                for (var questionIndex = 0; questionIndex < combo.Items.Count; questionIndex++)
                {
                    combo.SelectedIndex = questionIndex;
                    DrainDispatcher();
                    AssertSelectedQuestionText();
                }
                combo.SelectedIndex = 0;
                DrainDispatcher();
                Assert.Equal(10, combo.Items.Count);
                Assert.Equal("ffmpeg-wrapper", Assert.IsType<BookStitch.Models.FaqEntry>(combo.SelectedItem).Id);
                Assert.Equal(new[] { "ffmpeg-wrapper", "living", "artificial-intelligence", "help", "acronym", "icon-design", "nightly-build", "criticism", "audible-drm", "faq-count" },
                    combo.Items.Cast<BookStitch.Models.FaqEntry>().Select(entry => entry.Id));
                Assert.Equal("Yet another FFmpeg wrapper?", Assert.IsType<BookStitch.Models.FaqEntry>(combo.SelectedItem).Question);
                combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "help");
                DrainDispatcher();
                var scroll = (ScrollViewer)view.FindName("AnswerScrollViewer");
                Assert.True(scroll.ScrollableHeight > 0);
                var width = scroll.ViewportWidth;
                scroll.ScrollToBottom();
                DrainDispatcher();
                Assert.True(scroll.VerticalOffset > 0);
                combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "nightly-build");
                DrainDispatcher();
                Assert.Equal(0, scroll.VerticalOffset);
                Assert.True(scroll.ScrollableHeight > 0); // The approved machine illustration needs scrolling.
                Assert.Equal(width, scroll.ViewportWidth, 2);
                combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "faq-count");
                DrainDispatcher();
                Assert.Equal(0, scroll.VerticalOffset);
                Assert.Equal(0, scroll.ScrollableHeight);
                Assert.Equal(width, scroll.ViewportWidth, 2);
                var selected = combo.SelectedItem;
                var upper = (ListBox)dialog.FindName("SettingsNavigationList");
                var lower = (ListBox)dialog.FindName("SettingsUtilityNavigationList");
                upper.SelectedIndex = 0;
                DrainDispatcher();
                Assert.Equal(Visibility.Collapsed, view.Visibility);
                Assert.Null(lower.SelectedItem);
                lower.SelectedItem = lower.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "FAQ"));
                DrainDispatcher();
                Assert.Equal(Visibility.Visible, view.Visibility);
                Assert.Null(upper.SelectedItem);
                Assert.Same(selected, combo.SelectedItem);
                combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "help");
                DrainDispatcher();
                Assert.Equal(0, scroll.VerticalOffset);
                Assert.True(scroll.ScrollableHeight > 0);
                Assert.True(combo.ActualWidth > 300);
                Assert.Equal(LocalizationText.Get("Settings.Faq.Subtitle"), System.Windows.Automation.AutomationProperties.GetName(combo));
                SaveLayoutRepairPreview(dialog, language + "-faq-new");
            }, initialTabKey: "FAQ")));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("gsw-CH")]
    [InlineData("es-ES")]
    [InlineData("fr-FR")]
    [InlineData("cs-CZ")]
    [InlineData("da-DK")]
    [InlineData("it-IT")]
    [InlineData("nl-NL")]
    [InlineData("nb-NO")]
    [InlineData("pl-PL")]
    [InlineData("pt-BR")]
    [InlineData("fi-FI")]
    [InlineData("sv-SE")]
    [InlineData("vi-VN")]
    [InlineData("tr-TR")]
    [InlineData("ru-RU")]
    [InlineData("bn-BD")]
    [InlineData("ja-JP")]
    [InlineData("zh-CN")]
    [InlineData("zh-HK")]
    [InlineData("System")]
    public void Restart_prompt_uses_selected_language_without_switching_active_ui(string target)
    {
        RunOnStaThread(() => WithSettingsCulture("de", () =>
            RunWithSettingsWindow(new AppSettings { UiLanguage = "de-DE" }, dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var view = (SettingsLanguageControl)dialog.FindName("LanguageSettingsControl");
                var combo = (ComboBox)view.FindName("UiLanguageComboBox");
                Assert.Equal(UiLanguageService.Choices.Select(choice => choice.Id),
                    combo.Items.Cast<object>().Select(item => (string)item.GetType().GetProperty("Id")!.GetValue(item)!));
                combo.SelectedItem = combo.Items.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Id")!.GetValue(item), target));
                DrainDispatcher();
                var expectedCulture = UiLanguageService.Resolve(target, CultureInfo.InstalledUICulture);
                Assert.Equal(LocalizationText.Get("LanguageRestartButton", expectedCulture), ((Button)view.FindName("UiLanguageRestartButton")).Content);
                Assert.Equal(LocalizationText.Get("LanguageRestart", expectedCulture), ((TextBlock)view.FindName("LanguageRestartTextBlock")).Text);
                Assert.Equal(LocalizationText.Get("Settings.Language.TranslationNotice", expectedCulture), ((TextBlock)view.FindName("TranslationNoticeTextBlock")).Text);
                Assert.Equal(LocalizationText.Get("Settings.Language.ChapterNotice", expectedCulture), ((TextBlock)view.FindName("ChapterNoticeTextBlock")).Text);
                Assert.Equal("de", CultureInfo.CurrentUICulture.Name);
            })));
    }

    [Theory]
    [InlineData("de-DE", "en-US")]
    [InlineData("en-US", "de-DE")]
    public void Language_notices_follow_selection_on_open_and_reserve_restart_space(string initial, string target)
    {
        RunOnStaThread(() => WithSettingsCulture(initial, () =>
        {
            var settings = new AppSettings { UiLanguage = initial };
            RunWithSettingsWindow(settings, dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var view = (SettingsLanguageControl)dialog.FindName("LanguageSettingsControl");
                var combo = (ComboBox)view.FindName("UiLanguageComboBox");
                var notice = (TextBlock)view.FindName("TranslationNoticeTextBlock");
                var chapters = (TextBlock)view.FindName("ChapterNoticeTextBlock");
                var restart = (TextBlock)view.FindName("LanguageRestartTextBlock");
                Assert.Equal(LocalizationText.Get("Settings.Language.TranslationNotice", CultureInfo.GetCultureInfo(initial)), notice.Text);
                var position = notice.TranslatePoint(new Point(), view);
                Assert.Equal(combo.TranslatePoint(new Point(), view).X, position.X, 3);
                Assert.True(notice.ActualWidth > combo.ActualWidth);
                Assert.True(restart.ActualHeight >= 40);
                SaveLayoutRepairPreview(dialog, initial + "-language-notices-initial");
                combo.SelectedItem = combo.Items.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Id")!.GetValue(item), target));
                DrainDispatcher();
                Assert.Equal(position.Y, notice.TranslatePoint(new Point(), view).Y, 3);
                Assert.Equal(LocalizationText.Get("Settings.Language.TranslationNotice", CultureInfo.GetCultureInfo(target)), notice.Text);
                Assert.Equal(LocalizationText.Get("Settings.Language.ChapterNotice", CultureInfo.GetCultureInfo(target)), chapters.Text);
                Assert.Contains("\n\n", notice.Text);
                Assert.Contains("\n\n", chapters.Text);
                SaveLayoutRepairPreview(dialog, target + "-language-notices-selected");
                combo.SelectedItem = combo.Items.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Id")!.GetValue(item), initial));
                DrainDispatcher();
                Assert.Equal(position.Y, notice.TranslatePoint(new Point(), view).Y, 3);
            }, initialTabKey: "Language");
            settings.UiLanguage = target;
            RunWithSettingsWindow(settings, dialog =>
            {
                var view = (SettingsLanguageControl)dialog.FindName("LanguageSettingsControl");
                Assert.Equal(LocalizationText.Get("Settings.Language.TranslationNotice", CultureInfo.GetCultureInfo(target)),
                    ((TextBlock)view.FindName("TranslationNoticeTextBlock")).Text);
                Assert.Equal(LocalizationText.Get("Settings.Language.ChapterNotice", CultureInfo.GetCultureInfo(target)),
                    ((TextBlock)view.FindName("ChapterNoticeTextBlock")).Text);
            }, initialTabKey: "Language");
        }));
    }

    [Fact]
    public void Language_page_keeps_selection_and_restart_prompt_when_navigating()
    {
        RunOnStaThread(() => WithSettingsCulture("de", () =>
        {
            var settings = new AppSettings { UiLanguage = "de-DE" };
            RunWithSettingsWindow(settings, dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                var navigation = (ListBox)dialog.FindName("SettingsNavigationList");
                var utility = (ListBox)dialog.FindName("SettingsUtilityNavigationList");
                var language = utility.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "Language"));
                var general = (SettingsGeneralControl)dialog.FindName("GeneralSettingsControl");
                Assert.Null(general.FindName("UiLanguageComboBox"));
                var view = (SettingsLanguageControl)dialog.FindName("LanguageSettingsControl");
                Assert.True(view.IsVisible);
                var combo = (ComboBox)view.FindName("UiLanguageComboBox");
                var restart = (Button)view.FindName("UiLanguageRestartButton");
                Assert.Equal(Visibility.Collapsed, restart.Visibility);
                combo.SelectedItem = combo.Items.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Id")!.GetValue(item), "fr-FR"));
                Assert.Equal("fr-FR", settings.UiLanguage);
                Assert.Equal(Visibility.Visible, restart.Visibility);
                navigation.SelectedItem = navigation.Items[0];
                DrainDispatcher();
                Assert.False(view.IsVisible);
                Assert.True(general.IsVisible);
                utility.SelectedItem = language;
                DrainDispatcher();
                Assert.True(view.IsVisible);
                Assert.Equal("Language", settings.LastSettingsTab);
                Assert.Equal("fr-FR", combo.SelectedItem!.GetType().GetProperty("Id")!.GetValue(combo.SelectedItem));
                Assert.Equal(Visibility.Visible, restart.Visibility);
                combo.SelectedItem = combo.Items.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Id")!.GetValue(item), "de-DE"));
                Assert.Equal(Visibility.Collapsed, restart.Visibility);
            }, initialTabKey: "Language");
            RunWithSettingsWindow(settings, dialog =>
            {
                dialog.Show();
                DrainDispatcher();
                Assert.True(((SettingsLanguageControl)dialog.FindName("LanguageSettingsControl")).IsVisible);
            });
        }));
    }

    [Theory]
    [InlineData("Black")]
    [InlineData("Ivory")]
    public void Language_symbol_stays_inside_navigation_and_matches_heading_in_every_locale(string background)
    {
        RunOnStaThread(() =>
        {
            foreach (var language in UiLanguageService.Choices.Where(choice => choice.Id != UiLanguageService.System))
                WithSettingsCulture(language.Id, () =>
                {
                    AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings { BackgroundPreset = background });
                    RunWithSettingsWindow(new AppSettings(), window =>
                    {
                        window.Show();
                        DrainDispatcher();
                        var navigation = (ListBox)window.FindName("SettingsNavigationList");
                        Assert.Equal(new[] { "General", "Conversion", "Metadata", "Chapters", "ProjectManagement", "Drive" },
                            navigation.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag));
                        var utility = (ListBox)window.FindName("SettingsUtilityNavigationList");
                        Assert.Equal(new[] { "Language", "Info", "FAQ" }, utility.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag));
                        var item = (ListBoxItem)utility.Items[0];
                        var icon = FindVisualDescendant<ContentControl>(item, element => element.Name == "LanguageNavigationIcon");
                        Assert.NotNull(icon);
                        var view = (SettingsLanguageControl)window.FindName("LanguageSettingsControl");
                        var heading = (TextBlock)view.FindName("LanguageHeadingTextBlock");
                        var headingIcon = (ContentControl)view.FindName("LanguageHeadingIcon");
                        Assert.Equal(LocalizationText.Get("LanguageHeading"), heading.Text);
                        Assert.Equal(heading.Text, System.Windows.Automation.AutomationProperties.GetName(item));
                        Assert.True(icon.IsVisible && headingIcon.IsVisible);
                        Assert.Same(icon.ContentTemplate, headingIcon.ContentTemplate);
                        var languageImage = FindVisualDescendant<Image>(icon, image => image.Source is not null);
                        Assert.NotNull(languageImage);
                        var bitmap = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(languageImage.Source);
                        using var languageAsset = Application.GetResourceStream(new Uri(
                            "/BookStitch;component/Assets/Icons/BookStitch-Language-Petrol.png", UriKind.Relative))!.Stream;
                        var expectedBitmap = System.Windows.Media.Imaging.BitmapFrame.Create(languageAsset,
                            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        var stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
                        var actualPixels = new byte[stride * bitmap.PixelHeight];
                        var expectedPixels = new byte[stride * expectedBitmap.PixelHeight];
                        bitmap.CopyPixels(actualPixels, stride, 0);
                        expectedBitmap.CopyPixels(expectedPixels, stride, 0);
                        Assert.Equal(expectedPixels, actualPixels);
                        Assert.Equal(512, bitmap.PixelWidth);
                        Assert.Equal(System.Windows.Media.BitmapScalingMode.HighQuality,
                            System.Windows.Media.RenderOptions.GetBitmapScalingMode(languageImage));
                        Assert.False(icon.Focusable);
                        // Stress the actual layout, including languages longer than today's translations.
                        foreach (var label in new[] { heading.Text, new string('W', 200) })
                        {
                            item.Content = label;
                            DrainDispatcher();
                            var right = icon.TranslatePoint(new Point(icon.ActualWidth, 0), item).X;
                            Assert.InRange(right, icon.ActualWidth, item.ActualWidth - item.Padding.Right);
                            Assert.InRange(icon.TranslatePoint(new Point(), item).X, item.Padding.Left, item.ActualWidth);
                            Assert.Equal(24, icon.ActualWidth, 2);
                            var scroller = FindVisualDescendant<ScrollViewer>(navigation, _ => true);
                            Assert.NotNull(scroller);
                            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                            Assert.Equal(Visibility.Collapsed, scroller.ComputedHorizontalScrollBarVisibility);
                            Assert.Equal(0, scroller.ScrollableWidth, 2);
                            foreach (var category in navigation.Items.Cast<ListBoxItem>())
                            {
                                navigation.SelectedItem = category;
                                DrainDispatcher();
                                Assert.InRange(category.TranslatePoint(new Point(), navigation).X, 0, navigation.ActualWidth);
                                Assert.InRange(category.TranslatePoint(new Point(category.ActualWidth, 0), navigation).X, 0, navigation.ActualWidth);
                            }
                            Assert.InRange(icon.TranslatePoint(new Point(icon.ActualWidth, 0), navigation).X, 0, navigation.ActualWidth);
                        }
                        Assert.True(headingIcon.TranslatePoint(new Point(), view).X > heading.TranslatePoint(new Point(heading.ActualWidth, 0), view).X);
                    }, initialTabKey: "Language");
                });
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("bn-BD")]
    [InlineData("cs")]
    [InlineData("da")]
    [InlineData("es")]
    [InlineData("fi")]
    [InlineData("fr")]
    [InlineData("gsw-CH")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("nb")]
    [InlineData("nl")]
    [InlineData("pl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("sv")]
    [InlineData("tr")]
    [InlineData("vi")]
    [InlineData("zh-CN")]
    [InlineData("zh-HK")]
    public void Global_chapter_preview_displays_all_available_sample_rows_and_updates_on_mode_change(string locale)
    {
        RunOnStaThread(() => WithSettingsCulture(locale, () => RunWithSettingsWindow(new AppSettings
        {
            ChapterSettings = new ChapterSettings { Slot1 = "OriginalTagTitle", Slot2 = "Empty", Slot3 = "Empty" }
        }, window =>
        {
            window.Show();
            DrainDispatcher();
            var view = (SettingsTagsChaptersControl)window.FindName("TagsChaptersSettingsControl");
            var mode = (ComboBox)view.FindName("ChapterModeComboBox");
            var preview = (TextBlock)view.FindName("ChapterPreviewTextBlock");
            foreach (var value in new[] { "Auto", "Custom", "None", "Auto" })
            {
                mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, value));
                DrainDispatcher();
                if (value == "None") Assert.Empty(preview.Text);
                else
                {
                    Assert.Equal(5, preview.Text.Split(Environment.NewLine).Length);
                    Assert.True(preview.ActualHeight >= preview.LineHeight * 5 - 1);
                    var lines = preview.Text.Split(Environment.NewLine);
                    for (var index = 0; index < 5; index++)
                    {
                        var key = "Settings.ChapterSample." + (value == "Auto" ? "Content" : "Tag") + (index + 1);
                        Assert.EndsWith(LocalizationText.Get(key), lines[index]);
                        Assert.DoesNotContain("Settings.ChapterSample.", lines[index]);
                    }
                }
            }
        }, initialTabKey: "Chapters")));
    }

    private static void WithSettingsCulture(string language, Action action)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            EnsureApplicationResources();
            action();
        }
        finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
    }
}
