using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Controls;

public static class MetadataLockToggleFeedbackBehavior
{
    private const int RejectionPulseMilliseconds = 250;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(MetadataLockToggleFeedbackBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IsRejectedFeedbackActiveProperty = DependencyProperty.RegisterAttached(
        "IsRejectedFeedbackActive",
        typeof(bool),
        typeof(MetadataLockToggleFeedbackBehavior),
        new PropertyMetadata(false));

    private static readonly DependencyProperty PendingActivationStateProperty = DependencyProperty.RegisterAttached(
        "PendingActivationState",
        typeof(bool?),
        typeof(MetadataLockToggleFeedbackBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty RejectionPulseVersionProperty = DependencyProperty.RegisterAttached(
        "RejectionPulseVersion",
        typeof(int),
        typeof(MetadataLockToggleFeedbackBehavior),
        new PropertyMetadata(0));

    private static readonly DependencyProperty RejectionPulseTimerProperty = DependencyProperty.RegisterAttached(
        "RejectionPulseTimer",
        typeof(DispatcherTimer),
        typeof(MetadataLockToggleFeedbackBehavior),
        new PropertyMetadata(null));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsRejectedFeedbackActive(DependencyObject element, bool value) =>
        element.SetValue(IsRejectedFeedbackActiveProperty, value);

    public static bool GetIsRejectedFeedbackActive(DependencyObject element) =>
        (bool)element.GetValue(IsRejectedFeedbackActiveProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ToggleButton toggleButton)
            return;

        if ((bool)e.OldValue)
        {
            toggleButton.PreviewMouseLeftButtonDown -= ToggleButton_PreviewMouseLeftButtonDown;
            toggleButton.PreviewKeyDown -= ToggleButton_PreviewKeyDown;
            toggleButton.Click -= ToggleButton_Click;
        }

        if ((bool)e.NewValue)
        {
            toggleButton.PreviewMouseLeftButtonDown += ToggleButton_PreviewMouseLeftButtonDown;
            toggleButton.PreviewKeyDown += ToggleButton_PreviewKeyDown;
            toggleButton.Click += ToggleButton_Click;
            return;
        }

        toggleButton.ClearValue(PendingActivationStateProperty);
        CancelRejectionPulse(toggleButton);
    }

    private static void ToggleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
            CaptureActivationState(toggleButton);
    }

    private static void ToggleButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ToggleButton toggleButton)
            return;

        if (e.Key == Key.Space)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == ModifierKeys.Alt)
                return;

            CaptureActivationState(toggleButton);
            return;
        }

        if (e.Key == Key.Enter)
            CaptureActivationState(toggleButton);
    }

    // Capture before ToggleButton.OnToggle changes IsChecked. The later Click handler then
    // has a stable distinction between a requested lock (false -> true) and an unlock.
    internal static void CaptureActivationState(ToggleButton toggleButton)
    {
        if (!GetIsEnabled(toggleButton))
            return;

        toggleButton.SetValue(PendingActivationStateProperty, toggleButton.IsChecked);
    }

    private static void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton || !GetIsEnabled(toggleButton))
            return;

        var stateBeforeActivation = (bool?)toggleButton.GetValue(PendingActivationStateProperty);
        toggleButton.ClearValue(PendingActivationStateProperty);

        // Only a user activation that started unlocked can be a rejected lock request.
        // Successful unlocks therefore never enter the rejection path.
        if (stateBeforeActivation != false)
            return;

        _ = toggleButton.Dispatcher.BeginInvoke(
            DispatcherPriority.DataBind,
            () => EvaluateRequestedLock(toggleButton));
    }

    private static void EvaluateRequestedLock(ToggleButton toggleButton)
    {
        if (!GetIsEnabled(toggleButton))
            return;

        BindingExpression? bindingExpression = toggleButton.GetBindingExpression(ToggleButton.IsCheckedProperty);
        if (bindingExpression is null)
            return;

        // The existing TwoWay setter remains the only authority. The pre-activation snapshot
        // tells us that true was requested; this round-trip asks the bound source for the
        // authoritative result without reimplementing any lockability rule here.
        bindingExpression.UpdateSource();
        bindingExpression.UpdateTarget();

        if (toggleButton.IsChecked != true)
            StartRejectionPulse(toggleButton);
    }

    private static void StartRejectionPulse(ToggleButton toggleButton)
    {
        StopPulseTimer(toggleButton);

        var version = (int)toggleButton.GetValue(RejectionPulseVersionProperty) + 1;
        toggleButton.SetValue(RejectionPulseVersionProperty, version);
        toggleButton.SetValue(IsRejectedFeedbackActiveProperty, true);

        var timer = new DispatcherTimer(DispatcherPriority.Normal, toggleButton.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(RejectionPulseMilliseconds)
        };

        EventHandler handler = null!;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;

            if ((int)toggleButton.GetValue(RejectionPulseVersionProperty) != version)
                return;

            toggleButton.SetValue(IsRejectedFeedbackActiveProperty, false);
            if (ReferenceEquals(toggleButton.GetValue(RejectionPulseTimerProperty), timer))
                toggleButton.ClearValue(RejectionPulseTimerProperty);
        };

        timer.Tick += handler;
        toggleButton.SetValue(RejectionPulseTimerProperty, timer);
        timer.Start();
    }

    private static void CancelRejectionPulse(ToggleButton toggleButton)
    {
        StopPulseTimer(toggleButton);
        toggleButton.SetValue(
            RejectionPulseVersionProperty,
            (int)toggleButton.GetValue(RejectionPulseVersionProperty) + 1);
        toggleButton.SetValue(IsRejectedFeedbackActiveProperty, false);
    }

    private static void StopPulseTimer(ToggleButton toggleButton)
    {
        if (toggleButton.GetValue(RejectionPulseTimerProperty) is DispatcherTimer timer)
            timer.Stop();

        toggleButton.ClearValue(RejectionPulseTimerProperty);
    }
}
