using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    public void Settings_scrollbar_reaches_content_edges_at_default_size(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                RunWithSettingsWindow(new AppSettings(), dialog =>
                {
                    dialog.Show();
                    DrainDispatcher();
                    var host = Assert.IsType<Grid>(dialog.FindName("SettingsContentHost"));
                    var view = Assert.IsType<SettingsGeneralControl>(dialog.FindName("GeneralSettingsControl"));
                    var scroll = FindVisualDescendant<ScrollViewer>(view)!;
                    // Auto may collapse the bar when all content fits; force it visible to measure its chrome.
                    scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                    dialog.UpdateLayout();
                    var bar = Assert.IsType<ScrollBar>(scroll.Template.FindName("PART_VerticalScrollBar", scroll));
                    var position = bar.TransformToAncestor(host).Transform(new Point(0, 0));
                    Assert.InRange(position.Y, -0.1, 0.1);
                    Assert.InRange(position.X + bar.ActualWidth - host.ActualWidth, -0.1, 0.1);
                    Assert.InRange(position.Y + bar.ActualHeight - host.ActualHeight, -0.1, 0.1);
                    SaveLayoutRepairPreview(dialog, language + "-settings");
                });
            }
            finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Actual_setup_and_metadata_dialog_worktype_headers_keep_lock_and_output_inside(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                var metadata = new ProjectMetadataState();
                metadata.CanonicalMetadata.SetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType, AudioWorkType.AudioDrama, MetadataOrigin.GlobalPreference);
                var preferences = new MetadataPreferences { PreferredAudioWorkType = AudioWorkType.AudioDrama, AudioWorkTypePreferenceLocked = true };
                var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.Folder, metadataState: metadata,
                    metadataPreferences: preferences, getAnimationMilliseconds: () => 0);
                var editor = CreateMetadataDialog(metadata, preferences);
                try
                {
                    setup.Show();
                    using var keyboard = CreateKeyboardInputSource("layout repair");
                    RaiseKey(setup.StartProjectButton, keyboard, Key.Right, Keyboard.PreviewKeyDownEvent);
                    editor.Show();
                    DrainDispatcher();
                    var setupField = Assert.IsType<ConceptSpecialMetadataFieldControl>(setup.FindName("SetupWorkTypeField"));
                    Assert.Equal(LocalizationText.Get("Metadata.Origin.GlobalPreference"), setupField.State!.OriginLabel);
                    AssertHeaderTogglesFit(setupField, setupField);
                    var header = Assert.IsType<Grid>(editor.FindName("ConceptQuickAccessWorkTypeField"));
                    AssertHeaderTogglesFit(header, header);
                    SaveLayoutRepairPreview(setup, language + "-setup");
                    SaveLayoutRepairPreview(editor, language + "-metadata");
                }
                finally { editor.Close(); setup.Close(); }
            }
            finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
        });
    }

    private static void AssertHeaderTogglesFit(FrameworkElement root, FrameworkElement boundary)
    {
        foreach (var key in new[] { "ConceptMetadataLockToggleStyle", "ConceptOutputChipToggleStyle" })
        {
            var toggle = FindVisualDescendant<ToggleButton>(root, item => item.Visibility == Visibility.Visible &&
                ReferenceEquals(item.Style, root.FindResource(key)));
            Assert.NotNull(toggle);
            var edge = toggle.TransformToAncestor(boundary).Transform(new Point(toggle.ActualWidth, 0)).X;
            Assert.True(edge <= boundary.ActualWidth + 0.1, $"{key}: {edge}/{boundary.ActualWidth}");
        }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Narrow_and_wide_candidate_popups_keep_complete_origin_labels(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                var dictionaries = new ResourceDictionary
                {
                    Source = new Uri("/BookStitch;component/Themes/ConceptDialogStyles.xaml", UriKind.Relative)
                };
                foreach (var width in new[] { 252d, 375d })
                foreach (var field in new[] { ProjectMetadataField.ISBN, ProjectMetadataField.AbridgedStatus,
                    ProjectMetadataField.AudioWorkType, ProjectMetadataField.Title, ProjectMetadataField.Authors })
                {
                    var origin = field == ProjectMetadataField.ISBN ? MetadataOrigin.User
                        : field is ProjectMetadataField.AbridgedStatus or ProjectMetadataField.AudioWorkType
                            ? MetadataOrigin.StaticList : MetadataOrigin.SourceAggregation;
                    var candidate = new ConceptMetadataCandidate(field, new string('x', 70), "fixture", 4, origin, false,
                        origin == MetadataOrigin.SourceAggregation);
                    var combo = new ComboBox { Width = width, IsEditable = field is ProjectMetadataField.ISBN or ProjectMetadataField.Title or ProjectMetadataField.Authors,
                        ItemsSource = new[] { candidate }, ItemTemplate = (DataTemplate)dictionaries["ConceptMetadataCandidateTemplate"],
                        Style = (Style)dictionaries["ConceptComboBoxStyle"] };
                    var window = new Window { Width = 500, Height = 160, ShowInTaskbar = false, Content = combo };
                    try
                    {
                        window.Show();
                        combo.IsDropDownOpen = true;
                        DrainDispatcher();
                        var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                        var text = FindVisualDescendant<TextBlock>(item, block => block.Text == candidate.EvidenceLabel);
                        Assert.NotNull(text);
                        Assert.True(text.ActualWidth >= text.DesiredSize.Width - 0.1);
                        var edge = text.TransformToAncestor(item).Transform(new Point(text.ActualWidth, 0)).X;
                        Assert.True(edge <= item.ActualWidth + 0.1, $"{language}/{field}: origin exceeds item ({edge}/{item.ActualWidth})");
                        var popup = Assert.IsType<Popup>(combo.Template.FindName("Popup", combo));
                        var child = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
                        Assert.True(item.ActualWidth <= child.ActualWidth + 0.1);
                        Assert.True(FindVisualDescendant<TextBlock>(item, block => block.Text == candidate.DisplayValue)!.ActualWidth > 0);
                        if (width == 252 && field == ProjectMetadataField.ISBN)
                            SaveLayoutRepairPreview(child, language + "-isbn-popup");
                    }
                    finally { window.Close(); }
                }
            }
            finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
        });
    }

    private static void SaveLayoutRepairPreview(FrameworkElement element, string name)
    {
        var folder = Environment.GetEnvironmentVariable("BOOKSTITCH_LAYOUT_REPAIR_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        System.IO.Directory.CreateDirectory(folder);
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height),
            96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, width, height);
            context.DrawRectangle(element.TryFindResource("AppBackgroundBrush") as Brush ?? Brushes.DarkSlateGray, null, bounds);
            context.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png"));
        encoder.Save(file);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Setup_enum_mouse_choice_commits_once_without_enter_and_keeps_header_controls_visible(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                foreach (var field in new[] { ProjectMetadataField.AudioWorkType, ProjectMetadataField.AbridgedStatus })
                {
                    var metadata = new ProjectMetadataState();
                    var preferences = new MetadataPreferences();
                    var saves = 0;
                    var state = new ConceptSpecialMetadataFieldState(metadata, preferences, field, _ => saves++, _ => { });
                    var value = field == ProjectMetadataField.AudioWorkType ? (object)AudioWorkType.AudioDrama : AbridgedStatus.Unabridged;
                    state.Candidates.Add(new ConceptMetadataCandidate(field, "fixture", value, 0, MetadataOrigin.StaticList));
                    var view = new ConceptSpecialMetadataFieldControl { Width = 252, DataContext = state };
                    var window = new Window { Width = 500, Height = 200, ShowInTaskbar = false, Content = view };
                    try
                    {
                        window.Show();
                        var combo = Assert.IsType<ComboBox>(view.FindName("EnumEditor"));
                        combo.IsDropDownOpen = true;
                        DrainDispatcher();
                        var item = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                        DrainDispatcher();
                        Assert.Equal(1, saves);
                        Assert.False(state.HasDraft);
                        Assert.False(combo.IsDropDownOpen);
                        var secondValue = field == ProjectMetadataField.AudioWorkType ? (object)AudioWorkType.Audiobook : AbridgedStatus.Abridged;
                        var second = new ConceptMetadataCandidate(field, "second", secondValue, 0, MetadataOrigin.StaticList);
                        state.Candidates.Add(second);
                        combo.SelectedItem = second;
                        Assert.Equal(1, saves); // Programmatic selection and keyboard browsing only update the draft.
                        using var input = CreateKeyboardInputSource("setup enum enter");
                        RaiseKey(combo, input, Key.Enter, Keyboard.PreviewKeyDownEvent);
                        DrainDispatcher();
                        Assert.Equal(2, saves);
                        Assert.False(state.HasDraft);
                        if (field == ProjectMetadataField.AudioWorkType)
                        {
                            Assert.Equal(AudioWorkType.Audiobook, metadata.WorkType);
                            state.IsLocked = true;
                            var before = saves;
                            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                            DrainDispatcher();
                            Assert.Equal(before, saves);
                            Assert.Equal(AudioWorkType.Audiobook, metadata.WorkType);
                        }
                        else Assert.Equal(AbridgedStatus.Abridged, metadata.AbridgedStatus);
                        window.UpdateLayout();
                        var output = FindVisualDescendant<ToggleButton>(view, toggle => toggle.Visibility == Visibility.Visible &&
                            ReferenceEquals(toggle.Style, view.FindResource("ConceptOutputChipToggleStyle")));
                        Assert.NotNull(output);
                        Assert.Equal(LocalizationText.Format("Metadata.Automation.Output", state.Label),
                            System.Windows.Automation.AutomationProperties.GetName(output));
                        Assert.Equal(state.Label, System.Windows.Automation.AutomationProperties.GetName(combo));
                        state.SetTechnicalFieldNameMode(true);
                        DrainDispatcher();
                        Assert.Equal(LocalizationText.Format("Metadata.Automation.Output", state.Label),
                            System.Windows.Automation.AutomationProperties.GetName(output));
                        state.SetTechnicalFieldNameMode(false);
                        DrainDispatcher();
                        Assert.True(output.TransformToAncestor(view).Transform(new Point(output.ActualWidth, 0)).X <= view.ActualWidth + 0.1);
                    }
                    finally { window.Close(); }
                }
            }
            finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
        });
    }
}
