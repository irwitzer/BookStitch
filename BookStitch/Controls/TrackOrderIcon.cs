using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BookStitch.Controls;

public enum TrackOrderDirection { Up, Down, Top, Bottom }

/// <summary>Identical icon geometry compared as native drawing or DPI-specific PNG.</summary>
public sealed class TrackOrderIcon : Control
{
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(TrackOrderDirection), typeof(TrackOrderIcon),
        new FrameworkPropertyMetadata(TrackOrderDirection.Up, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty VectorModeProperty = DependencyProperty.Register(
        nameof(VectorMode), typeof(bool), typeof(TrackOrderIcon),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(string), typeof(TrackOrderIcon),
        new FrameworkPropertyMetadata("Dark", FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Dictionary<string, BitmapSource> Bitmaps = [];
    public TrackOrderDirection Direction { get => (TrackOrderDirection)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public bool VectorMode { get => (bool)GetValue(VectorModeProperty); set => SetValue(VectorModeProperty, value); }
    public string Theme { get => (string)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (VectorMode)
        {
            dc.PushTransform(new ScaleTransform(ActualWidth / 32, ActualHeight / 32));
            dc.DrawDrawing(CreateDrawing(Direction, Foreground));
            dc.Pop();
            return;
        }
        var desired = ActualWidth * VisualTreeHelper.GetDpi(this).DpiScaleX;
        var size = new[] { 32, 40, 48, 56, 64, 80, 96 }.MinBy(value => Math.Abs(value - desired));
        var key = $"{Theme}-{Direction}-{size}";
        if (!Bitmaps.TryGetValue(key, out var bitmap))
        {
            bitmap = new BitmapImage(new Uri($"pack://application:,,,/BookStitch;component/Assets/Icons/TrackOrder/Classic/{key}.png"));
            bitmap.Freeze();
            Bitmaps.Add(key, bitmap);
        }
        dc.DrawImage(bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    public static DrawingGroup CreateDrawing(TrackOrderDirection direction, Brush brush)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var circlePen = new Pen(brush, 1.5);
            var arrowPen = new Pen(brush, 1.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawEllipse(null, circlePen, new Point(16, 16), 14, 14);
            if (direction is TrackOrderDirection.Down or TrackOrderDirection.Bottom)
                dc.PushTransform(new RotateTransform(180, 16, 16));
            var top = direction is TrackOrderDirection.Top or TrackOrderDirection.Bottom;
            var geometry = Geometry.Parse(top ? "M16,23 L16,12 M11,17 L16,12 L21,17 M10,8 L22,8" : "M16,23 L16,9 M10,15 L16,9 L22,15");
            dc.DrawGeometry(null, arrowPen, geometry);
            if (direction is TrackOrderDirection.Down or TrackOrderDirection.Bottom) dc.Pop();
        }
        drawing.Freeze();
        return drawing;
    }
}
