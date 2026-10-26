using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace BookStitch.Controls;

/// <summary>Standard progress bar with a temporary developer-only presentation overlay.</summary>
public sealed class PreviewProgressBar : ProgressBar
{
    private UIElement? _previewVisibilityTarget;

    public PreviewProgressBar() => Unloaded += (_, _) => StopPreview();

    public void StartPreview(int seconds, bool gold, UIElement? visibilityTarget = null)
    {
        if (seconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(seconds));

        StopPreview();
        _previewVisibilityTarget = visibilityTarget;
        if (visibilityTarget is not null)
        {
            var visibility = new ObjectAnimationUsingKeyFrames();
            visibility.KeyFrames.Add(new DiscreteObjectKeyFrame(Visibility.Visible, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            visibilityTarget.BeginAnimation(VisibilityProperty, visibility);
        }
        var foreground = new ObjectAnimationUsingKeyFrames();
        var brush = new SolidColorBrush(gold ? Color.FromRgb(0xD9, 0xA4, 0x41) : Color.FromRgb(0x16, 0x85, 0x8A));
        brush.Freeze();
        foreground.KeyFrames.Add(new DiscreteObjectKeyFrame(brush, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        BeginAnimation(ForegroundProperty, foreground);
        BeginAnimation(IsIndeterminateProperty, HoldBoolean(false));
        BeginAnimation(ValueProperty, new DoubleAnimation(Minimum, Maximum, TimeSpan.FromSeconds(seconds))
        {
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    public void StopPreview()
    {
        BeginAnimation(ValueProperty, null);
        BeginAnimation(ForegroundProperty, null);
        BeginAnimation(IsIndeterminateProperty, null);
        _previewVisibilityTarget?.BeginAnimation(VisibilityProperty, null);
        _previewVisibilityTarget = null;
    }

    private static BooleanAnimationUsingKeyFrames HoldBoolean(bool value)
    {
        var animation = new BooleanAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteBooleanKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        return animation;
    }
}
