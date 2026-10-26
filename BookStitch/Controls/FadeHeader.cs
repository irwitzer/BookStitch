using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

/// <summary>
/// Reusable overlay header whose content remains opaque while a configurable
/// lower fade lets scrolled content disappear smoothly beneath it.
/// </summary>
public sealed class FadeHeader : ContentControl
{
    public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register(
        nameof(HeaderHeight),
        typeof(GridLength),
        typeof(FadeHeader),
        new FrameworkPropertyMetadata(new GridLength(32), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FadeHeightProperty = DependencyProperty.Register(
        nameof(FadeHeight),
        typeof(GridLength),
        typeof(FadeHeader),
        new FrameworkPropertyMetadata(new GridLength(24), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public GridLength HeaderHeight
    {
        get => (GridLength)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public GridLength FadeHeight
    {
        get => (GridLength)GetValue(FadeHeightProperty);
        set => SetValue(FadeHeightProperty, value);
    }
}
