using System.Xml.Linq;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("ConceptValueBadgeRemoveButtonStyle", false)]
    [InlineData("ConceptValueBadgeRemoveButtonStyle", true)]
    [InlineData("ConceptValueBadgeEditButtonStyle", false)]
    [InlineData("ConceptValueBadgeEditButtonStyle", true)]
    [InlineData("ConceptAuthorBadgeRemoveButtonStyle", false)]
    [InlineData("ConceptAuthorBadgeRemoveButtonStyle", true)]
    public void Release_freeze_badge_micro_buttons_keep_compact_clickable_bounds(string key, bool ivory)
    {
        RunOnStaThread(() =>
        {
            var resources = new ResourceDictionary { Source = new System.Uri("/BookStitch;component/Themes/ConceptDialogStyles.xaml", System.UriKind.Relative) };
            var button = new Button { Style = (Style)resources[key] };
            var clicked = 0;
            button.Click += (_, _) => clicked++;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = "Value", Style = (Style)resources["ConceptValueBadgeTextStyle"], VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(button);
            var badge = new Border { Style = (Style)resources["ConceptValueBadgeStyle"], Child = content };
            BookStitch.Services.AppearanceSettingsService.ApplyToResources(badge.Resources, new BookStitch.Models.AppSettings { BackgroundPreset = ivory ? "Ivory" : "Black" });
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            badge.Arrange(new Rect(badge.DesiredSize));
            Assert.Equal(17, button.ActualHeight);
            Assert.Equal(17, button.ActualWidth);
            Assert.InRange(badge.ActualHeight, 23, 25);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, clicked);
            SaveLayoutRepairPreview(badge, $"freeze-badge-{key}-{ivory}");
            Assert.Equal(28d, Application.Current.FindResource("StandardButtonHeight"));
        });
    }
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Release_freeze_action_groups_fit_minimum_window_with_one_or_multiple_actions(int state, bool ivory)
    {
        RunOnStaThread(() =>
        {
            var document = TestHelpers.RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var source = new System.Xml.Linq.XElement(document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ProjectActionBar"));
            source.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", x.NamespaceName);
            source.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "services", "clr-namespace:BookStitch.Services;assembly=BookStitch");
            foreach (var click in source.Descendants().Attributes("Click").ToArray()) click.Remove();
            // Detached XAML is loaded from the test assembly, so qualify app controls explicitly.
            System.Xml.Linq.XNamespace appControls = "clr-namespace:BookStitch.Controls;assembly=BookStitch";
            source.SetAttributeValue("xmlns", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            source.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "controls", appControls.NamespaceName);
            foreach (var attribute in source.Descendants().Attributes().Where(attribute => attribute.Name.NamespaceName == "clr-namespace:BookStitch.Controls").ToArray())
            {
                attribute.Parent!.SetAttributeValue(appControls + attribute.Name.LocalName, attribute.Value);
                attribute.Remove();
            }
            foreach (var element in source.Descendants().Where(element => element.Name.NamespaceName == "clr-namespace:BookStitch.Controls"))
                element.Name = appControls + element.Name.LocalName;
            var bar = (Grid)System.Windows.Markup.XamlReader.Parse(source.ToString());
            var presetRow = document.Descendants().Single(e => (string?)e.Attribute("Click") == "OpenWorkFolder_Click").Parent!;
            var presetMargin = (Thickness)new ThicknessConverter().ConvertFromInvariantString((string)presetRow.Attribute("Margin")!)!;
            Assert.Equal(presetMargin.Left, bar.Margin.Left);
            var actionHostMargin = (Thickness)new ThicknessConverter().ConvertFromInvariantString((string)document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ProjectActionBar").Parent!.Attribute("Margin")!)!;
            Assert.Equal(7, actionHostMargin.Bottom);
            BookStitch.Services.AppearanceSettingsService.ApplyToResources(bar.Resources, new BookStitch.Models.AppSettings { BackgroundPreset = ivory ? "Ivory" : "Black" });
            bar.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "AppBackgroundBrush");
            var projects = (StackPanel)bar.FindName("ProjectActionButtons");
            var buttons = projects.Children.OfType<Button>().Concat(projects.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())).ToArray();
            foreach (var button in buttons) { button.Visibility = Visibility.Collapsed; button.IsEnabled = true; }
            var resourceFolder = System.IO.Path.GetDirectoryName(TestHelpers.RepositorySource.Find(["BookStitch", "Resources", "LocalizationResources.de.resx"]))!;
            foreach (var file in System.IO.Directory.EnumerateFiles(resourceFolder, "LocalizationResources*.resx"))
            {
                var translations = XDocument.Load(file).Descendants("data").ToDictionary(e => (string)e.Attribute("name")!, e => e.Element("value")!.Value);
                buttons[0].Visibility = Visibility.Visible; buttons[0].Content = translations["MainWindow.Action.PrepareProject"];
                if (state == 1) { buttons[0].Content = translations["MainWindow.Action.Merge"]; buttons[1].Visibility = Visibility.Visible; buttons[1].Content = translations["MainWindow.Action.AddDiscs"]; }
                if (state == 2)
                {
                    buttons[0].Content = translations["MainWindow.Action.Continue"];
                    buttons[2].Visibility = Visibility.Visible; buttons[2].Content = translations["MainWindow.Action.Save"];
                    buttons[3].Visibility = Visibility.Visible; buttons[3].Content = translations["Project.Close.Delete"];
                }
                // Minimum window width minus frame/content insets and the unchanged cover column.
                bar.Measure(new Size(880, 50)); bar.Arrange(new Rect(0, 0, 880, 50));
                DrainDispatcher();
                bar.Measure(new Size(880, 50)); bar.Arrange(new Rect(0, 0, 880, 50));
                var arrows = (StackPanel)bar.FindName("TrackOrderButtons");
                var folder = (Button)bar.FindName("ActionBarOpenOutputFolderButton");
                var arrowLeft = arrows.TranslatePoint(new Point(), bar).X;
                Assert.True(projects.ActualWidth <= arrowLeft, $"{file}: Actions overlap arrows: {projects.ActualWidth}/{arrowLeft}");
                var expectedCenter = System.Math.Max(bar.ActualWidth / 2, projects.ActualWidth + arrows.ActualWidth / 2);
                Assert.InRange(arrowLeft + arrows.ActualWidth / 2, expectedCenter - 1, expectedCenter + 1);
                Assert.True(arrowLeft + arrows.ActualWidth <= folder.TranslatePoint(new Point(), bar).X);
                Assert.Equal(bar.ActualWidth, folder.TranslatePoint(new Point(folder.ActualWidth, 0), bar).X);
                Assert.InRange(folder.TranslatePoint(new Point(0, folder.ActualHeight), bar).Y - bar.ActualHeight, -0.1, 0.1);
                Assert.InRange(arrows.TranslatePoint(new Point(0, arrows.ActualHeight), bar).Y - bar.ActualHeight, -0.1, 0.1);
                foreach (var button in buttons.Where(button => button.Visibility == Visibility.Visible))
                    Assert.InRange(button.TranslatePoint(new Point(0, button.ActualHeight), bar).Y - bar.ActualHeight, -0.1, 0.1);
                if (file.EndsWith("Resources.de.resx")) SaveLayoutRepairPreview(bar, $"freeze-actions-{state}-{ivory}");
            }
            var orderButtons = ((StackPanel)bar.FindName("TrackOrderButtons")).Children.OfType<Button>().ToArray();
            var directions = new[] { BookStitch.Controls.TrackOrderDirection.Up, BookStitch.Controls.TrackOrderDirection.Down,
                BookStitch.Controls.TrackOrderDirection.Top, BookStitch.Controls.TrackOrderDirection.Bottom };
            Assert.Equal(directions.Length, orderButtons.Length);
            for (var i = 0; i < orderButtons.Length; i++)
            {
                var icon = Assert.IsType<BookStitch.Controls.TrackOrderIcon>(orderButtons[i].Content);
                Assert.Equal(directions[i], icon.Direction);
                Assert.True(icon.ActualWidth > 0 && icon.ActualHeight > 0);
            }
        });
    }

    [Theory]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Folder, false)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Folder, true)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Mp3Disc, false)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.Mp3Disc, true)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.AudioDisc, false)]
    [InlineData(BookStitch.Dialog.ProjectSetupSourceKind.AudioDisc, true)]
    public void Release_freeze_setup_missing_folder_remains_selectable_and_choice_publishes_once(BookStitch.Dialog.ProjectSetupSourceKind kind, bool ivory)
    {
        RunOnStaThread(() =>
        {
            using var root = new TestHelpers.TemporaryFolder();
            var published = new System.Collections.Generic.List<string>();
            var dialog = CreateProjectSetupDialog(kind, setOutputFolder: published.Add);
            try
            {
                BookStitch.Services.AppearanceSettingsService.ApplyToResources(dialog.Resources, new BookStitch.Models.AppSettings { BackgroundPreset = ivory ? "Ivory" : "Black" });
                dialog.Show(); DrainDispatcher();
                var set = dialog.GetType().GetMethod("SetOutputFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                set.Invoke(dialog, new object[] { System.IO.Path.Combine(root.Path, "missing"), false });
                Assert.True(dialog.OutputFolderStateChipButton.IsEnabled);
                Assert.False(dialog.OpenOutputFolderButton.IsEnabled);
                Assert.Empty(published);
                set.Invoke(dialog, new object[] { root.Path, true });
                Assert.True(dialog.OpenOutputFolderButton.IsEnabled);
                Assert.Equal(new[] { root.Path }, published);
                Assert.False(string.IsNullOrWhiteSpace(dialog.OutputFolderBadgeTextBlock.Text));
                SaveLayoutRepairPreview(dialog, $"freeze-setup-{kind}-{ivory}");
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Release_freeze_settings_output_edge_matches_focus_profile(bool ivory)
    {
        RunOnStaThread(() =>
        {
            using var root = new TestHelpers.TemporaryFolder();
            var service = new BookStitch.Services.SettingsService(System.IO.Path.Combine(root.Path, "app"), root.Path);
            using var manager = new BookStitch.Services.GlobalSettingsManager(service);
            using var notification = new BookStitch.Services.NotificationService(new BookStitch.Services.SoundNotificationService(() => manager.Current));
            var view = new BookStitch.Controls.SettingsGeneralControl();
            view.Initialize(manager, notification, _ => { }, () => root.Path, _ => { }, _ => { }, new TestOutputFolderChangeSource());
            var host = new Window { Content = view, Width = 1030, Height = 650, ShowInTaskbar = false };
            try
            {
                BookStitch.Services.AppearanceSettingsService.ApplyToResources(host.Resources, new BookStitch.Models.AppSettings { BackgroundPreset = ivory ? "Ivory" : "Black" });
                host.SetResourceReference(Window.BackgroundProperty, "AppBackgroundBrush");
                host.Show(); DrainDispatcher(); host.UpdateLayout();
                var folder = (Button)view.FindName("OpenOutputFolderButton");
                var focus = (ComboBox)view.FindName("FocusProfileComboBox");
                var folderEdge = folder.TranslatePoint(new Point(folder.ActualWidth, 0), view).X;
                var focusEdge = focus.TranslatePoint(new Point(focus.ActualWidth, 0), view).X;
                Assert.InRange(folderEdge - focusEdge, -0.1, 0.1);
                Assert.True(folderEdge < view.ActualWidth - 50);
                SaveLayoutRepairPreview(host, $"freeze-settings-{ivory}");
            }
            finally { host.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_folder_icon_variants_keep_borderless_geometry_and_distinct_theme_foregrounds(bool ivory)
    {
        RunOnStaThread(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            BookStitch.Services.AppearanceSettingsService.ApplyToResources(row.Resources,
                new BookStitch.Models.AppSettings { BackgroundPreset = ivory ? "Ivory" : "Black" });
            foreach (var (style, brush) in new[] { ("OpenOutputFolderButtonStyle", "MainTextBrush"),
                         ("AccentOpenOutputFolderButtonStyle", "AccentBrush") })
            {
                var button = new Button { Width = 36, Style = (Style)Application.Current.FindResource(style) };
                row.Children.Add(button);
                row.Measure(new Size(200, 50)); row.Arrange(new Rect(0, 0, 200, 50));
                button.ApplyTemplate();
                DrainDispatcher();
                Assert.Same(button.FindResource(brush), button.Foreground);
                var border = Assert.IsType<Border>(System.Windows.Media.VisualTreeHelper.GetChild(button, 0));
                Assert.Equal(new Thickness(0), border.BorderThickness);
                Assert.Equal(System.Windows.Media.Colors.Transparent,
                    Assert.IsType<System.Windows.Media.SolidColorBrush>(border.Background).Color);
                var icon = FindVisualDescendant<System.Windows.Shapes.Path>(button)!;
                Assert.Same(button.Foreground, icon.Stroke);
                var content = (ContentPresenter)button.Template.FindName("FolderIcon", button);
                button.IsEnabled = false;
                Assert.Equal(0.38, content.Opacity);
            }
        });
    }

}
