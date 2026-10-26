using BookStitch.Dialog;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BookStitch.Controls;

[TemplatePart(Name = MinimizeButtonPartName, Type = typeof(ButtonBase))]
[TemplatePart(Name = MaximizeRestoreButtonPartName, Type = typeof(ButtonBase))]
[TemplatePart(Name = CloseButtonPartName, Type = typeof(ButtonBase))]
public sealed class LargeResizableWindowHeader : Control
{
    // Include the same resize frame used by MainWindowStyle, including the modern padded border.
    public static Thickness MainResizeBorderThickness => new(7);
    public static double SystemCaptionHeight => SystemParameters.WindowCaptionHeight + MainResizeBorderThickness.Top;

    private const string MinimizeButtonPartName = "PART_MinimizeButton";
    private const string MaximizeRestoreButtonPartName = "PART_MaximizeRestoreButton";
    private const string CloseButtonPartName = "PART_CloseButton";

    private ButtonBase? _minimizeButton;
    private ButtonBase? _maximizeRestoreButton;
    private ButtonBase? _closeButton;

    public static readonly DependencyProperty TitleContentProperty = DependencyProperty.Register(
        nameof(TitleContent),
        typeof(object),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(null));

    public static readonly DependencyProperty TitleAreaWidthProperty = DependencyProperty.Register(
        nameof(TitleAreaWidth),
        typeof(GridLength),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(GridLength.Auto));

    public static readonly DependencyProperty CenterContentProperty = DependencyProperty.Register(
        nameof(CenterContent),
        typeof(object),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions),
        typeof(object),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ShowMinimizeProperty = DependencyProperty.Register(
        nameof(ShowMinimize),
        typeof(bool),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ShowMaximizeRestoreProperty = DependencyProperty.Register(
        nameof(ShowMaximizeRestore),
        typeof(bool),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowCloseProperty = DependencyProperty.Register(
        nameof(ShowClose),
        typeof(bool),
        typeof(LargeResizableWindowHeader),
        new PropertyMetadata(true));

    public object? TitleContent
    {
        get => GetValue(TitleContentProperty);
        set => SetValue(TitleContentProperty, value);
    }

    public GridLength TitleAreaWidth
    {
        get => (GridLength)GetValue(TitleAreaWidthProperty);
        set => SetValue(TitleAreaWidthProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public bool ShowMinimize
    {
        get => (bool)GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximizeRestore
    {
        get => (bool)GetValue(ShowMaximizeRestoreProperty);
        set => SetValue(ShowMaximizeRestoreProperty, value);
    }

    public bool ShowClose
    {
        get => (bool)GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    public event RoutedEventHandler? CloseRequested;

    public override void OnApplyTemplate()
    {
        if (_minimizeButton is not null)
            _minimizeButton.Click -= MinimizeButton_Click;
        if (_maximizeRestoreButton is not null)
            _maximizeRestoreButton.Click -= MaximizeRestoreButton_Click;
        if (_closeButton is not null)
            _closeButton.Click -= CloseButton_Click;

        base.OnApplyTemplate();

        _minimizeButton = GetTemplateChild(MinimizeButtonPartName) as ButtonBase;
        _maximizeRestoreButton = GetTemplateChild(MaximizeRestoreButtonPartName) as ButtonBase;
        _closeButton = GetTemplateChild(CloseButtonPartName) as ButtonBase;

        if (_minimizeButton is not null)
            _minimizeButton.Click += MinimizeButton_Click;
        if (_maximizeRestoreButton is not null)
            _maximizeRestoreButton.Click += MaximizeRestoreButton_Click;
        if (_closeButton is not null)
            _closeButton.Click += CloseButton_Click;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            DialogWindowBehavior.TryToggleMaximizeRestore(window);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, e);
}
