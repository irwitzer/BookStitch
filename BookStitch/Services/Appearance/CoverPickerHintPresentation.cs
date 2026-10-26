using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BookStitch.Services;

public sealed record CoverPickerHintMetrics(
    double HorizontalPadding,
    double FontSize,
    double VerticalPadding)
{
    public double BannerHeight => FontSize + (2 * VerticalPadding);
}

public sealed record CoverPickerHintPresentationPaletteDefinition(
    AppearanceMode Mode,
    VerticalAlignment VerticalAlignment,
    FontWeight FontWeight);

public static class CoverPickerHintPresentation
{
    public static CoverPickerHintMetrics Calculate(double coverWidth)
    {
        var width = double.IsFinite(coverWidth) && coverWidth > 0 ? coverWidth : 0;
        return new CoverPickerHintMetrics(
            Math.Clamp(width * 0.04, 6, 16),
            Math.Clamp(width * 0.045, 10, 14),
            Math.Clamp(width * 0.015, 2, 6));
    }

    public static CoverPickerHintPresentationPaletteDefinition GetPalette(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Light => Light,
        _ => Dark
    };

    public static CoverPickerHintPresentationPaletteDefinition Dark { get; } = new(
        AppearanceMode.Dark,
        VerticalAlignment.Bottom,
        FontWeights.Normal);

    public static CoverPickerHintPresentationPaletteDefinition Light { get; } = new(
        AppearanceMode.Light,
        VerticalAlignment.Bottom,
        FontWeights.Normal);
}

public sealed class CoverPickerHintMetricConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value is double actualWidth ? actualWidth : 0;
        var metrics = CoverPickerHintPresentation.Calculate(width);
        return parameter?.ToString() switch
        {
            "HorizontalMargin" => new Thickness(metrics.HorizontalPadding, 0, metrics.HorizontalPadding, 0),
            "FontSize" => metrics.FontSize,
            "BannerHeight" => metrics.BannerHeight,
            _ => DependencyProperty.UnsetValue
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
