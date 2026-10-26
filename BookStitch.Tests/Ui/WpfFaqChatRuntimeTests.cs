using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF: actual resource decoding, focus, layout, theme bindings and window lifetime.
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Faq_chat_opens_starts_and_stops_without_playing_the_conversation()
    {
        RunOnStaThread(() => RunWithSettingsWindow(new AppSettings(), window =>
        {
            window.Show();
            DrainDispatcher();
            var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
            var combo = (ComboBox)faq.FindName("QuestionsComboBox");
            combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Chat is not null);
            DrainDispatcher();
            var chat = (FaqChatControl)faq.FindName("IconChat");
            var session = Assert.IsType<FaqChatSession>(chat.Session);
            var start = chat.StartChatAsync();
            DrainDispatcher();
            Assert.True(chat.IsVisible);
            Assert.True(session.HasPendingHuman);
            Assert.Empty(session.History);
            window.Close();
            WaitForChat(() => start.IsCompleted);
            start.GetAwaiter().GetResult();
            Assert.False(session.CanSend);
        }, initialTabKey: "FAQ"));
    }

    [Fact]
    public void Closing_settings_cancels_pending_chat_and_reopening_starts_clean()
    {
        RunOnStaThread(() =>
        {
            FaqChatSession? closedSession = null;
            RunWithSettingsWindow(new AppSettings(), window =>
            {
                window.Show();
                DrainDispatcher();
                var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
                var combo = (ComboBox)faq.FindName("QuestionsComboBox");
                combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "icon-design");
                DrainDispatcher();
                var chat = (FaqChatControl)faq.FindName("IconChat");
                closedSession = chat.Session!;
                WaitForChat(() => closedSession.CanSend);
                var pending = chat.SendPreparedMessageAsync();
                DrainDispatcher();
                window.Close();
                WaitForChat(() => pending.IsCompleted);
                pending.GetAwaiter().GetResult();
                Assert.Single(closedSession.History);
                Assert.False(closedSession.CanSend);
            }, initialTabKey: "FAQ");
            RunWithSettingsWindow(new AppSettings(), window =>
            {
                var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
                var chat = (FaqChatControl)faq.FindName("IconChat");
                Assert.NotSame(closedSession, chat.Session);
                Assert.Empty(chat.Session!.History);
                Assert.False(chat.Session.CanSend);
            });
        });
    }

    [Theory]
    [InlineData("Black", "acronym")]
    [InlineData("Ivory", "acronym")]
    [InlineData("Black", "nightly-build")]
    [InlineData("Ivory", "nightly-build")]
    public void Faq_illustrations_fit_below_approved_text_without_cropping(string background, string entryId)
    {
        RunOnStaThread(() =>
        {
            AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings { BackgroundPreset = background });
            try
            {
                RunWithSettingsWindow(new AppSettings(), window =>
                {
                    window.Show();
                    var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
                    var combo = (ComboBox)faq.FindName("QuestionsComboBox");
                    combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == entryId);
                    DrainDispatcher();
                    var entry = (FaqEntry)combo.SelectedItem;
                    var image = FindVisualDescendant<Image>(faq, element =>
                        System.Windows.Automation.AutomationProperties.GetName(element) == entry.Sections.OfType<FaqImage>().Single().Text);
                    Assert.NotNull(image);
                    var source = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(image.Source);
                    foreach (var width in new[] { window.MinWidth, 1300d })
                    {
                        window.Width = width;
                        DrainDispatcher();
                        var lastParagraph = FindVisualDescendant<TextBlock>(faq, text => text.Text == entry.Sections.OfType<FaqParagraph>().Last().Text);
                        Assert.NotNull(lastParagraph);
                        Assert.True(image.TranslatePoint(new Point(), faq).Y >= lastParagraph.TranslatePoint(new Point(0, lastParagraph.ActualHeight), faq).Y);
                        Assert.Equal((double)source.PixelWidth / source.PixelHeight, image.ActualWidth / image.ActualHeight, 3);
                        Assert.Equal(Stretch.Uniform, image.Stretch);
                        if (entryId is "acronym" or "nightly-build")
                        {
                            Assert.Equal(combo.ActualWidth, image.ActualWidth, 2);
                            Assert.Equal(combo.TranslatePoint(new Point(), faq).X, image.TranslatePoint(new Point(), faq).X, 2);
                            var clip = Assert.IsType<RectangleGeometry>(image.Clip);
                            Assert.Equal(28, clip.RadiusX);
                            Assert.Equal(image.ActualWidth, clip.Rect.Width, 2);
                        }
                        else Assert.Null(image.Clip);
                        // WPF layout arithmetic can exceed an exact DIP bound by machine epsilon.
                        Assert.InRange(image.ActualWidth, 0, entry.Sections.OfType<FaqImage>().Single().MaxWidth + 0.001);
                    }
                }, initialTabKey: "FAQ");
            }
            finally { AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings()); }
        });
    }

    [Fact]
    public void Faq_images_are_embedded_byte_identical_to_the_approved_source_assets()
    {
        RunOnStaThread(() =>
        {
            var expected = new Dictionary<string, string>
            {
                ["BookStitch-Lock-Closed-optimiert.png"] = "E0D60C879EDD72BEA940B695C337B1A94C4B23E6AEEA96B5B6048D65FCAD4962",
                ["BookStitch-Lock-Open-optimiert.png"] = "DCADDC17F6CA7D7EC0F7D2E2FBE7AF841F1C20114D57ED93B1B52A4843D9F905",
                ["BookStitch-Lock-Unlocked-optimiert.png"] = "ECFCDB4676169208C788E433D8B123595863A2F7435FEB3AE91905B11EA53CDF",


            };
            expected.Add("BookMachin Bauplan.png", "9B79F38BD4EF58ED0BECDE6A47BFB58759481D343A14FEED77C2045AFD331778");
            expected.Add("BookMachin Press.png", "A3B804F2448F4E0EB6E9B944BD5773ADB0D665678860A1311FA188CE065CC3DF");
            expected.Add("BookMachin Band.png", "928E3515E0F487245456DCA9DA6C13E079313DD12EBB7C2D7FAE6D9D1C7CCC63");
            expected.Add("audible-cake-optimiert.jpg", "918A65EAC5F9DB9398AE742FA1488E29718194FB80CC647D73180C90D36368B4");
            using var packed = typeof(SettingsFaqControl).Assembly.GetManifestResourceStream("BookStitch.g.resources")!;
            using var resources = new System.Resources.ResourceReader(packed);
            var names = resources.Cast<System.Collections.DictionaryEntry>().Select(item => (string)item.Key).ToArray();
            Assert.DoesNotContain("assets/faq/audible-cake.png", names);
            Assert.DoesNotContain("assets/faq/the-cake-is-a-lie.jpg", names);
            foreach (var (name, hash) in expected)
            {
                var resource = Application.GetResourceStream(new Uri("/BookStitch;component/Assets/Faq/" + name, UriKind.Relative));
                Assert.NotNull(resource);
                using var stream = resource.Stream;
                Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
                stream.Position = 0;
                var decoded = System.Windows.Media.Imaging.BitmapFrame.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Assert.True(decoded.PixelWidth > 0 && decoded.PixelHeight > 0);
                if (name.StartsWith("BookStitch-Lock-", StringComparison.Ordinal))
                {
                    var rgba = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        decoded, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    var stride = rgba.PixelWidth * 4;
                    var pixels = new byte[stride * rgba.PixelHeight];
                    rgba.CopyPixels(pixels, stride, 0);
                    Assert.Contains(Enumerable.Range(0, rgba.PixelWidth * rgba.PixelHeight),
                        pixel => pixels[pixel * 4 + 3] == 0);
                    Assert.Contains(Enumerable.Range(0, rgba.PixelWidth * rgba.PixelHeight),
                        pixel => pixels[pixel * 4 + 3] > 0);
                }
            }
        });
    }

    [Theory]
    [InlineData("Black")]
    [InlineData("Ivory")]
    public void Settings_brand_logo_and_square_faq_previews_follow_the_selected_question(string background)
    {
        RunOnStaThread(() =>
        {
            AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings { BackgroundPreset = background });
            try
            {
                RunWithSettingsWindow(new AppSettings(), window =>
                {
                    window.Show();
                    DrainDispatcher();
                    var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
                    var combo = (ComboBox)faq.FindName("QuestionsComboBox");
                    var sidebar = (Image)faq.FindName("FaqSidebarImage");
                    var logo = sidebar.Source;
                    Assert.Same(FaqSidebarImageService.GetImage(((FaqEntry)combo.SelectedItem).SidebarImage), logo);
                    var navigation = (ListBox)window.FindName("SettingsUtilityNavigationList");
                    navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "Info"));
                    DrainDispatcher();
                    var info = (SettingsInfoControl)window.FindName("InfoSettingsControl");
                    var infoImage = FindVisualDescendant<Image>(info, _ => true);
                    Assert.NotNull(infoImage);
                    Assert.Same(window.FindResource("InfoSquareImageSource"), infoImage.Source);
                    var infoBorder = (Border)info.FindName("InfoSidebarBorder");
                    var infoGrid = (Grid)infoBorder.Parent;
                    var infoText = infoGrid.Children.OfType<StackPanel>().Single();
                    var faqBorder = (Border)faq.FindName("FaqSidebarBorder");
                    var faqGrid = (Grid)faqBorder.Parent;
                    var faqText = faqGrid.Children.OfType<StackPanel>().Single();
                    foreach (var width in new[] { 1030d, 1300d })
                    {
                        window.Width = width;
                        DrainDispatcher();
                        Assert.Equal(176, infoBorder.ActualWidth, 3);
                        Assert.Equal(176, infoBorder.ActualHeight, 3);
                        Assert.Equal(204, infoText.TranslatePoint(new Point(), infoGrid).X, 3);
                        Assert.Equal(3, infoBorder.TranslatePoint(new Point(), infoGrid).X, 3);
                        Assert.Null(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(infoBorder));
                        Assert.Equal(25, infoText.TranslatePoint(new Point(), infoBorder).X - infoBorder.ActualWidth, 3);
                        Assert.Equal(infoBorder.ActualWidth - 2, infoImage.ActualWidth, 3);
                        Assert.True(Assert.IsType<RectangleGeometry>(infoImage.Clip).RadiusX > 0);
                    }

                    navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "FAQ"));
                    DrainDispatcher();
                    foreach (var width in new[] { 1030d, 1300d })
                    {
                        window.Width = width;
                        DrainDispatcher();
                        Assert.Equal(176, faqBorder.ActualWidth, 3);
                        Assert.Equal(176, faqBorder.ActualHeight, 3);
                        Assert.Equal(204, faqText.TranslatePoint(new Point(), faqGrid).X, 3);
                        Assert.Equal(3, faqBorder.TranslatePoint(new Point(), faqGrid).X, 3);
                        Assert.Null(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(faqBorder));
                        Assert.Equal(25, faqText.TranslatePoint(new Point(), faqBorder).X - faqBorder.ActualWidth, 3);
                    }
                    foreach (var id in combo.Items.Cast<FaqEntry>().Where(entry => entry.Id != "ffmpeg-wrapper").Select(entry => entry.Id))
                    {
                        var entry = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == id);
                        combo.SelectedItem = entry;
                        if (entry.Narration is not null) ((FaqNarrationControl)faq.FindName("AudibleNarration")).Stop();
                        DrainDispatcher();
                        var preview = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(sidebar.Source);
                        if (entry.SidebarImage != ((FaqEntry)combo.Items[0]).SidebarImage) Assert.NotSame(logo, preview);
                        else Assert.Same(logo, preview);
                        var border = (Border)faq.FindName("FaqSidebarBorder");
                        Assert.Equal(new Thickness(0), border.Padding);
                        Assert.Equal(Stretch.UniformToFill, sidebar.Stretch);
                        Assert.Equal(border.ActualWidth - border.BorderThickness.Left - border.BorderThickness.Right, sidebar.ActualWidth, 3);
                        Assert.Equal(border.ActualHeight - border.BorderThickness.Top - border.BorderThickness.Bottom, sidebar.ActualHeight, 3);
                        var clip = Assert.IsType<RectangleGeometry>(sidebar.Clip);
                        Assert.Equal(sidebar.ActualWidth, clip.Rect.Width, 3);
                        Assert.Equal(sidebar.ActualHeight, clip.Rect.Height, 3);
                        Assert.True(clip.RadiusX > 0 && clip.RadiusY > 0);
                        Assert.Equal(preview.PixelWidth, preview.PixelHeight);
                        Assert.Equal(FaqSidebarImageService.ThumbnailPixels, preview.PixelWidth);
                        Assert.True(preview.IsFrozen);
                        Assert.Same(preview, FaqSidebarImageService.GetImage(entry.SidebarImage));
                    }
                    var entries = combo.Items.Cast<FaqEntry>().ToArray();
                    Assert.NotSame(
                        FaqSidebarImageService.GetImage(entries.Single(entry => entry.Id == "acronym").SidebarImage),
                        FaqSidebarImageService.GetImage(entries.Single(entry => entry.Id == "audible-drm").SidebarImage));
                    combo.SelectedItem = combo.Items.Cast<FaqEntry>().Single(entry => entry.Id == "help");
                    DrainDispatcher();
                    Assert.Same(logo, sidebar.Source);
                    Assert.Equal(new Thickness(0), ((Border)faq.FindName("FaqSidebarBorder")).Padding);
                    Assert.NotNull(sidebar.Clip);
                    Assert.Equal(Stretch.UniformToFill, sidebar.Stretch);
                    Assert.Same(window.FindResource("InfoSquareImageSource"), infoImage.Source);
                }, initialTabKey: "FAQ");
            }
            finally { AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings()); }
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
    public void Faq_navigation_and_heading_share_localized_text_while_content_stays_english(string locale)
    {
        RunOnStaThread(() => WithSettingsCulture(locale, () =>
            RunWithSettingsWindow(new AppSettings(), window =>
            {
                window.Show();
                DrainDispatcher();
                var navigation = (ListBox)window.FindName("SettingsUtilityNavigationList");
                var item = navigation.Items.Cast<ListBoxItem>().Single(item => Equals(item.Tag, "FAQ"));
                var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
                var expected = LocalizationText.Get("Settings.Faq.NavigationTitle");
                Assert.Equal(expected, item.Content);
                Assert.Equal(expected, LocalizationText.Get("Settings.Faq.Title"));
                Assert.NotNull(FindVisualDescendant<TextBlock>(faq, text => text.FontSize == 28 && text.Text == expected));
                Assert.NotNull(FindVisualDescendant<TextBlock>(faq, text => text.Text == LocalizationText.Get("Settings.Faq.Subtitle")));
                var questions = (ComboBox)faq.FindName("QuestionsComboBox");
                Assert.Equal("Yet another FFmpeg wrapper?", ((FaqEntry)questions.SelectedItem).Question);
            }, initialTabKey: "FAQ")));
    }

    [Fact]
    public void Generating_placeholder_is_centered_with_the_reduced_lock_width()
    {
        RunOnStaThread(() =>
        {
            var chat = new FaqChatControl();
            var template = (DataTemplate)chat.Resources[new DataTemplateKey(typeof(FaqChatGenerating))];
            var host = new ContentControl { Content = new FaqChatGenerating(), ContentTemplate = template };
            var window = new Window { Width = 600, Height = 220, Content = host };
            try
            {
                window.Show();
                DrainDispatcher();
                var panel = FindVisualDescendant<StackPanel>(host, _ => true);
                var glyph = FindVisualDescendant<System.Windows.Shapes.Path>(host, _ => true);
                var label = FindVisualDescendant<TextBlock>(host, text => text.Text == FaqChatLabels.Generating);
                Assert.NotNull(panel);
                Assert.NotNull(glyph);
                Assert.NotNull(label);
                Assert.Equal(HorizontalAlignment.Center, panel.HorizontalAlignment);
                Assert.Equal(FaqChatPresentation.IconWidth, glyph.ActualWidth, 2);
                Assert.Equal(60, FaqChatPresentation.IconWidth);
                Assert.Equal(TextAlignment.Center, label.TextAlignment);
                Assert.Equal(panel.ActualWidth / 2, glyph.TranslatePoint(new Point(glyph.ActualWidth / 2, 0), panel).X, 2);
            }
            finally { window.Close(); }
        });
    }

    private static void WaitForChat(Func<bool> complete)
    {
        var timeout = Stopwatch.StartNew();
        while (!complete() && timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            DrainDispatcher();
            Thread.Sleep(10);
        }
        DrainDispatcher();
        Assert.True(complete(), "The FAQ chat did not reach its expected state.");
    }
}
