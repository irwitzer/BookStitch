using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BookStitch.Controls;

// A second view of the main window's existing output settings; no persisted state.
public partial class MainOutputSettingsControl : UserControl
{
    internal const double BadgeMaximumWidth = 464;
    private double _availableBadgeWidth = BadgeMaximumWidth;

    public static readonly DependencyProperty OutputFolderProperty = DependencyProperty.Register(
        nameof(OutputFolder), typeof(string), typeof(MainOutputSettingsControl),
        new PropertyMetadata("", (owner, _) => ((MainOutputSettingsControl)owner).RefreshOutputFolderBadge()));
    public string OutputFolder
    {
        get => (string)GetValue(OutputFolderProperty);
        set => SetValue(OutputFolderProperty, value);
    }

    public event RoutedEventHandler? OpenFolderRequested;
    public event RoutedEventHandler? ChangeFolderRequested;

    public MainOutputSettingsControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshOutputFolderBadge();
        LayoutUpdated += (_, _) => UpdateAvailableBadgeWidth();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs args) => OpenFolderRequested?.Invoke(this, args);
    private void ChangeFolder_Click(object sender, RoutedEventArgs args) => ChangeFolderRequested?.Invoke(this, args);

    private void UpdateAvailableBadgeWidth()
    {
        if (ActualWidth <= 0)
            return;
        var fixedWidth = OutputRow.Children.OfType<FrameworkElement>().Sum(child => child.DesiredSize.Width)
            - OutputFolderStateChipButton.DesiredSize.Width;
        var available = Math.Clamp(ActualWidth - fixedWidth, 0, BadgeMaximumWidth);
        if (Math.Abs(available - _availableBadgeWidth) < 0.1)
            return;
        _availableBadgeWidth = available;
        RefreshOutputFolderBadge();
    }

    internal void RefreshOutputFolderBadge()
    {
        if (OutputFolderBadgeTextBlock is null)
            return;
        var padding = OutputFolderStateChip.Padding;
        var border = OutputFolderStateChip.BorderThickness;
        var chrome = padding.Left + padding.Right + border.Left + border.Right;
        if (chrome <= 0)
            chrome = 20;
        double Measure(string text) => MeasureText(text, OutputFolderBadgeTextBlock);
        var text = OutputFolderBadgeDisplayService.BuildText(OutputFolder, Math.Max(0, _availableBadgeWidth - chrome), Measure);
        OutputFolderBadgeTextBlock.Text = text;
        var width = OutputFolderBadgeDisplayService.CalculateWidth(text, _availableBadgeWidth, chrome, Measure);
        OutputFolderStateChipButton.Width = width;
        OutputFolderStateChip.Width = width;
    }

    private static double MeasureText(string text, TextBlock control) => MeasureText(text,
        control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch, control.FontSize,
        control.Foreground, VisualTreeHelper.GetDpi(control).PixelsPerDip);
    private static double MeasureText(string text, FontFamily family, FontStyle style, FontWeight weight,
        FontStretch stretch, double size, Brush foreground, double dpi) =>
        string.IsNullOrEmpty(text) ? 0 : new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, style, weight, stretch), size, foreground, dpi).WidthIncludingTrailingWhitespace;
}
