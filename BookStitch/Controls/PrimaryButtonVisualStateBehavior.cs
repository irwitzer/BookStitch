using BookStitch.Services;
using System.Windows;

namespace BookStitch.Controls;

public static class PrimaryButtonVisualStateBehavior
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(PrimaryButtonVisualState),
        typeof(PrimaryButtonVisualStateBehavior),
        new FrameworkPropertyMetadata(PrimaryButtonVisualState.Primary));

    public static void SetState(DependencyObject element, PrimaryButtonVisualState value) =>
        element.SetValue(StateProperty, value);

    public static PrimaryButtonVisualState GetState(DependencyObject element) =>
        (PrimaryButtonVisualState)element.GetValue(StateProperty);
}
