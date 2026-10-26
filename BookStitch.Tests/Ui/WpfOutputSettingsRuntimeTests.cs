using BookStitch.Controls;
using BookStitch.Services;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_combo_editors_share_enabled_and_disabled_text_colors_without_double_dimming(bool enabled)
    {
        RunOnStaThread(() =>
        {
            var view = new MainOutputSettingsControl { DataContext = new OutputSettingsViewState() };
            var row = new StackPanel();
            row.Children.Add(view);
            var style = (Style)System.Windows.Application.Current.FindResource("AppearanceCompactComboBoxStyle");
            // Mode and numbering use this same compact style in MainWindow.
            var mode = new ComboBox { Style = style, ItemsSource = new[] { "Mode" }, SelectedIndex = 0 };
            var numbering = new ComboBox { Style = style, ItemsSource = new[] { "Numbering" }, SelectedIndex = 0 };
            row.Children.Add(mode);
            row.Children.Add(numbering);
            var rules = new[] { "Number", "Space", "Clean filename", "Space", "Empty" }
                .Select(text => new ComboBox { Style = style, SelectedIndex = 0,
                    Items = { new ComboBoxItem { Content = text } } }).ToArray();
            foreach (var rule in rules) row.Children.Add(rule);
            var host = new Window { Content = row, Width = 1600, Height = 250 };
            try
            {
                host.Show();
                var combos = new[] { (ComboBox)view.FindName("OutputFolderLayoutComboBox"),
                    (ComboBox)view.FindName("FileNameTemplateComboBox"), (ComboBox)view.FindName("OutputExtensionComboBox"), mode, numbering }.Concat(rules).ToArray();
                foreach (var palette in new[] { (Active: Colors.White, Disabled: Colors.SlateGray),
                    (Active: Colors.Black, Disabled: Colors.DimGray) })
                foreach (var currentEnabled in new[] { enabled, !enabled, enabled })
                {
                    // Scoped palette changes exercise live DynamicResources without changing
                    // application-wide theme state.
                    host.Resources["MainTextBrush"] = new SolidColorBrush(palette.Active);
                    host.Resources["DisabledButtonTextBrush"] = new SolidColorBrush(palette.Disabled);
                    row.IsEnabled = currentEnabled;
                    DrainDispatcher();
                    var color = currentEnabled ? palette.Active : palette.Disabled;
                    foreach (var combo in combos)
                    {
                        combo.ApplyTemplate();
                        Assert.Equal(color, ((SolidColorBrush)combo.Foreground).Color);
                        Assert.Equal(currentEnabled ? 1 : 0.72, combo.Opacity);
                        if (combo.IsEditable)
                        {
                            var editor = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                            Assert.Equal(color, ((SolidColorBrush)editor.Foreground).Color);
                            Assert.Equal(1, editor.Opacity);
                            Assert.NotNull(System.Windows.Data.BindingOperations.GetBindingExpression(combo, ComboBox.TextProperty));
                        }
                        else
                        {
                            var content = (ContentPresenter)combo.Template.FindName("ContentSite", combo);
                            Assert.Equal(color, ((SolidColorBrush)TextElement.GetForeground(content)).Color);
                            var renderedText = FindVisualDescendant<TextBlock>(content, text => !string.IsNullOrEmpty(text.Text));
                            Assert.NotNull(renderedText);
                            Assert.Equal(color, ((SolidColorBrush)renderedText.Foreground).Color);
                            Assert.Equal(1, content.Opacity);
                        }
                    }
                }
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void Output_controls_follow_shared_editing_permission()
    {
        RunOnStaThread(() =>
        {
            var state = new OutputSettingsViewState();
            var view = new MainOutputSettingsControl { DataContext = state };
            view.SetBinding(System.Windows.UIElement.IsEnabledProperty, new System.Windows.Data.Binding("CanChangeExportOptions"));
            var host = new Window { Content = view, Width = 1600, Height = 200 };
            try
            {
                host.Show();
                foreach (var editable in new[] { true, false, true })
                {
                    state.CanChangeExportOptions = editable;
                    DrainDispatcher();
                    foreach (var name in new[] { "OutputFolderStateChipButton", "OutputFolderLayoutComboBox", "FileNameTemplateComboBox", "OutputExtensionComboBox" })
                        Assert.Equal(editable, Assert.IsAssignableFrom<System.Windows.UIElement>(view.FindName(name)).IsEnabled);
                }
            }
            finally { host.Close(); }
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Output_settings_fit_default_window_width_and_share_the_bound_setting_values(string culture)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var state = new OutputSettingsViewState();
                var view = new MainOutputSettingsControl { DataContext = state, OutputFolder = state.OutputFolder,
                    VerticalAlignment = VerticalAlignment.Top, FontFamily = new FontFamily("Segoe UI") };
                // MainWindow defaults to 1250; allow for window/frame and two panel insets.
                var host = new Window { Width = 1250, Height = 150, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None, Content = new Border { Padding = new Thickness(32), Child = view } };
                try
                {
                    host.Show();
                    DrainDispatcher();
                    host.UpdateLayout();
                    DrainDispatcher();
                    var extension = Assert.IsType<ComboBox>(view.FindName("OutputExtensionComboBox"));
                    var edge = extension.TransformToAncestor(view).Transform(new Point(extension.ActualWidth, 0)).X;
                    Assert.True(edge <= view.ActualWidth + 0.1, $"{culture}: output controls exceed default width ({edge}/{view.ActualWidth}).");
                    var badge = Assert.IsType<Button>(view.FindName("OutputFolderStateChipButton"));
                    Assert.InRange(badge.ActualWidth, 233, MainOutputSettingsControl.BadgeMaximumWidth);
                    var previewDirectory = Environment.GetEnvironmentVariable("BOOKSTITCH_OUTPUT_PREVIEW_DIR");
                    if (!string.IsNullOrWhiteSpace(previewDirectory))
                    {
                        System.IO.Directory.CreateDirectory(previewDirectory);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                            (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        var drawing = new DrawingVisual();
                        using (var context = drawing.RenderOpen())
                        {
                            var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
                            context.DrawRectangle(view.TryFindResource("PanelBackgroundBrush") as Brush ?? Brushes.DarkSlateGray, null, bounds);
                            context.DrawRectangle(new VisualBrush(view), null, bounds);
                        }
                        bitmap.Render(drawing);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var image = System.IO.File.Create(System.IO.Path.Combine(previewDirectory, culture + ".png"));
                        encoder.Save(image);
                        System.IO.File.WriteAllText(System.IO.Path.Combine(previewDirectory, culture + ".txt"),
                            $"Available row width: {view.ActualWidth}; last control edge: {edge}; path width: {badge.ActualWidth}");
                    }
                    var layout = Assert.IsType<ComboBox>(view.FindName("OutputFolderLayoutComboBox"));
                    layout.SelectedValue = "None";
                    extension.SelectedValue = ".m4b";
                    var template = Assert.IsType<ComboBox>(view.FindName("FileNameTemplateComboBox"));
                    template.SelectedIndex = 3;
                    DrainDispatcher();
                    Assert.Equal("None", state.OutputFolderLayout);
                    Assert.Equal(".m4b", state.OutputExtension);
                    Assert.Equal("{Autor} - {Sprecher} - {Titel}", state.FileNameTemplate);
                    state.OutputFolderLayout = "Title";
                    state.OutputExtension = ".m4a";
                    state.FileNameTemplate = "{Titel}";
                    DrainDispatcher();
                    Assert.Equal("Title", layout.SelectedValue);
                    Assert.Equal(".m4a", extension.SelectedValue);
                    Assert.Equal("{Titel}", template.Text);
                    var widestTemplateWidth = template.Width;
                    state.FileNameTemplate = new string('X', 200);
                    DrainDispatcher();
                    host.UpdateLayout();
                    Assert.True(template.ActualWidth <= widestTemplateWidth + 0.1);
                    Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(badge)));
                }
                finally { host.Close(); }
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    [Fact]
    public void Main_output_path_chooses_without_redundant_open_icon_or_changing_path_display()
    {
        RunOnStaThread(() =>
        {
            var state = new OutputSettingsViewState();
            var view = new MainOutputSettingsControl { DataContext = state, OutputFolder = state.OutputFolder };
            var chosen = 0; var opened = 0;
            view.ChangeFolderRequested += (_, _) => chosen++;
            view.OpenFolderRequested += (_, _) => opened++;
            var host = new Window { Content = view, Width = 1250, Height = 160, ShowInTaskbar = false };
            try
            {
                host.Show(); DrainDispatcher();
                var path = (Button)view.FindName("OutputFolderStateChipButton");
                Assert.Null(view.FindName("OpenOutputFolderButton"));
                path.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, chosen); Assert.Equal(0, opened);

                Assert.Equal(state.OutputFolder, view.OutputFolder);
                SaveLayoutRepairPreview(host, "main-output-folder-actions");
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void Settings_output_path_chooses_and_folder_icon_only_opens()
    {
        RunOnStaThread(() =>
        {
            using var root = new BookStitch.Tests.TestHelpers.TemporaryFolder();
            var service = new SettingsService(System.IO.Path.Combine(root.Path, "app"), root.Path);
            using var manager = new GlobalSettingsManager(service);
            using var notification = new NotificationService(new SoundNotificationService(() => manager.Current));
            var chosen = 0; var opened = 0;
            var view = new SettingsGeneralControl();
            view.Initialize(manager, notification, _ => { }, () => root.Path,
                _ => chosen++, _ => opened++, new TestOutputFolderChangeSource());
            var host = new Window { Content = view, Width = 1030, Height = 650, ShowInTaskbar = false };
            try
            {
                host.Show(); DrainDispatcher();
                ((Button)view.FindName("ChooseOutputFolderPathButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, chosen); Assert.Equal(0, opened);
                ((Button)view.FindName("OpenOutputFolderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, chosen); Assert.Equal(1, opened);
                Assert.Equal(root.Path, ((TextBlock)view.FindName("OutputFolderPathTextBlock")).Text);
                SaveLayoutRepairPreview(host, "settings-output-folder-actions");
            }
            finally { host.Close(); }
        });
    }

    private sealed class OutputSettingsViewState : INotifyPropertyChanged
    {
        private string _layout = "AuthorTitleNested", _extension = ".m4a", _template = "{Autor} - {Titel}";
        public event PropertyChangedEventHandler? PropertyChanged;
        public string OutputFolder => @"C:\Users\Reader\Audiobooks\Prepared\Large audiobook project output directory with a deliberately long path";
        public bool CanOpenOutputFolder => true;
        public bool CanChooseOutputFolder => true;
        private bool _canChangeExportOptions = true;
        public bool CanChangeExportOptions { get => _canChangeExportOptions; set { _canChangeExportOptions = value; PropertyChanged?.Invoke(this, new(nameof(CanChangeExportOptions))); } }
        public string OutputFolderLayout { get => _layout; set { _layout = value; PropertyChanged?.Invoke(this, new(nameof(OutputFolderLayout))); } }
        public string OutputExtension { get => _extension; set { _extension = value; PropertyChanged?.Invoke(this, new(nameof(OutputExtension))); } }
        public string FileNameTemplate { get => _template; set { _template = value; PropertyChanged?.Invoke(this, new(nameof(FileNameTemplate))); } }
    }
}
