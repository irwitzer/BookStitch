using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Controls;

/// <summary>
/// Opt-in smooth mouse-wheel scrolling for ordinary ScrollViewer instances.
/// The behavior is intentionally not enabled globally so large virtualized lists
/// can be evaluated separately before adopting animated scrolling.
/// </summary>
public static class SmoothMouseWheelScrollBehavior
{
    private const double WheelDeltaPerNotch = 120.0;
    private static readonly ConditionalWeakTable<ScrollViewer, AnimationState> States = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothMouseWheelScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty DurationMillisecondsProperty = DependencyProperty.RegisterAttached(
        "DurationMilliseconds",
        typeof(int),
        typeof(SmoothMouseWheelScrollBehavior),
        new PropertyMetadata(140));

    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step",
        typeof(double),
        typeof(SmoothMouseWheelScrollBehavior),
        new PropertyMetadata(72.0));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static int GetDurationMilliseconds(DependencyObject element) => (int)element.GetValue(DurationMillisecondsProperty);
    public static void SetDurationMilliseconds(DependencyObject element, int value) => element.SetValue(DurationMillisecondsProperty, value);

    public static double GetStep(DependencyObject element) => (double)element.GetValue(StepProperty);
    public static void SetStep(DependencyObject element, double value) => element.SetValue(StepProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ScrollViewer viewer)
            return;

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        viewer.Unloaded -= OnViewerUnloaded;

        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.Unloaded += OnViewerUnloaded;
            return;
        }

        CancelAnimation(viewer, removeState: true);
    }

    private static void OnViewerUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer viewer)
            CancelAnimation(viewer, removeState: true);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Delta == 0 || viewer.ScrollableHeight <= 0)
            return;

        var state = States.GetValue(viewer, static _ => new AnimationState());
        var step = Math.Max(1.0, GetStep(viewer));
        var targetBase = state.IsAnimating ? state.TargetOffset : viewer.VerticalOffset;
        var wheelNotches = e.Delta / WheelDeltaPerNotch;
        var targetOffset = Math.Clamp(
            targetBase - (wheelNotches * step),
            0.0,
            viewer.ScrollableHeight);

        if (Math.Abs(targetOffset - targetBase) < 0.01)
            return;

        e.Handled = true;

        var duration = Math.Clamp(GetDurationMilliseconds(viewer), 0, 1000);
        StartAnimation(viewer, state, targetOffset, duration);
    }

    private static void StartAnimation(
        ScrollViewer viewer,
        AnimationState state,
        double targetOffset,
        int durationMilliseconds)
    {
        state.Cancellation?.Cancel();

        if (durationMilliseconds <= 0)
        {
            state.TargetOffset = targetOffset;
            viewer.ScrollToVerticalOffset(targetOffset);
            return;
        }

        var cancellation = new CancellationTokenSource();
        state.Cancellation = cancellation;
        state.TargetOffset = targetOffset;

        _ = AnimateAsync(
            viewer,
            state,
            viewer.VerticalOffset,
            targetOffset,
            durationMilliseconds,
            cancellation);
    }

    private static async Task AnimateAsync(
        ScrollViewer viewer,
        AnimationState state,
        double startOffset,
        double targetOffset,
        int durationMilliseconds,
        CancellationTokenSource cancellation)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                var progress = Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / durationMilliseconds, 0.0, 1.0);
                var remaining = 1.0 - progress;
                var easedProgress = 1.0 - (remaining * remaining * remaining);
                var offset = startOffset + ((targetOffset - startOffset) * easedProgress);
                viewer.ScrollToVerticalOffset(offset);

                if (progress >= 1.0)
                    break;

                await Task.Delay(16, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer wheel impulse superseded this animation.
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(state.Cancellation, cancellation))
                state.Cancellation = null;
        }
    }

    private static void CancelAnimation(ScrollViewer viewer, bool removeState)
    {
        if (!States.TryGetValue(viewer, out var state))
            return;

        state.Cancellation?.Cancel();
        if (removeState)
            States.Remove(viewer);
    }

    private sealed class AnimationState
    {
        public CancellationTokenSource? Cancellation { get; set; }
        public double TargetOffset { get; set; }
        public bool IsAnimating => Cancellation is not null;
    }
}
