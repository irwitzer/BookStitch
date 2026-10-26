using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Controls;

public enum InfoPointVariant
{
    Default,
    OnPrimary
}

public partial class InfoPoint : UserControl
{
    private sealed class WindowPinnedInfoState
    {
        public WeakReference<InfoPoint>? Current { get; set; }
    }

    private static readonly ConditionalWeakTable<Window, WindowPinnedInfoState> PinnedByWindow = new();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InfoPoint), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(string), typeof(InfoPoint), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconScaleProperty = DependencyProperty.Register(
        nameof(IconScale), typeof(double), typeof(InfoPoint), new PropertyMetadata(1d));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(InfoPointVariant), typeof(InfoPoint), new PropertyMetadata(InfoPointVariant.Default));

    private readonly DispatcherTimer _transientCloseTimer;
    private bool _isPinned;
    private bool _isPointerOverButton;
    private bool _isPointerOverPopup;

    public InfoPoint()
    {
        InitializeComponent();
        _transientCloseTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _transientCloseTimer.Tick += TransientCloseTimer_Tick;
        Unloaded += InfoPoint_Unloaded;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Body
    {
        get => (string)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public double IconScale
    {
        get => (double)GetValue(IconScaleProperty);
        set => SetValue(IconScaleProperty, value);
    }

    public InfoPointVariant Variant
    {
        get => (InfoPointVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private void InfoButton_MouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerOverButton = true;
        CancelTransientClose();
        OpenPopup();
    }

    private void InfoButton_MouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerOverButton = false;
        ScheduleTransientClose();
    }

    private void InfoPopup_MouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerOverPopup = true;
        CancelTransientClose();
    }

    private void InfoPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerOverPopup = false;
        ScheduleTransientClose();
    }

    private void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPinned)
        {
            ClosePopup(restoreFocus: true);
            return;
        }

        CloseOtherPinnedInfoPoint();
        _isPinned = true;
        InfoButton.IsChecked = true;
        RegisterPinnedInfoPoint();
        AttachWindowInputHandlers();
        OpenPopup();
    }

    private void InfoPoint_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !InfoPopup.IsOpen)
            return;

        ClosePopup(restoreFocus: true);
        e.Handled = true;
    }

    private CustomPopupPlacement[] InfoPopup_CustomPopupPlacement(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        // Keep the popup visually attached above the info icon. A slight overlap
        // removes the dead gap between hover target and popup.
        return
        [
            new CustomPopupPlacement(
                new Point(0, -popupSize.Height + 2),
                PopupPrimaryAxis.Horizontal)
        ];
    }

    private void InfoPopup_Opened(object? sender, EventArgs e)
    {
        if (_isPinned)
            InfoButton.IsChecked = true;
    }

    private void InfoPopup_Closed(object? sender, EventArgs e)
    {
        CancelTransientClose();
        _isPointerOverPopup = false;
        if (_isPinned)
        {
            DetachWindowInputHandlers();
            ReleasePinnedInfoPoint();
        }

        _isPinned = false;
        InfoButton.IsChecked = false;
    }

    private void TransientCloseTimer_Tick(object? sender, EventArgs e)
    {
        CancelTransientClose();
        if (!_isPinned && !_isPointerOverButton && !_isPointerOverPopup)
            InfoPopup.IsOpen = false;
    }

    private void ScheduleTransientClose()
    {
        if (_isPinned)
            return;

        _transientCloseTimer.Stop();
        _transientCloseTimer.Start();
    }

    private void CancelTransientClose() => _transientCloseTimer.Stop();

    private void OpenPopup()
    {
        CancelTransientClose();
        InfoPopup.IsOpen = true;
    }

    private void ClosePopup(bool restoreFocus)
    {
        CancelTransientClose();
        if (_isPinned)
        {
            DetachWindowInputHandlers();
            ReleasePinnedInfoPoint();
        }

        _isPinned = false;
        InfoButton.IsChecked = false;
        InfoPopup.IsOpen = false;
        if (restoreFocus)
            InfoButton.Focus();
    }


    private void AttachWindowInputHandlers()
    {
        var window = Window.GetWindow(this);
        if (window is null)
            return;

        window.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
        window.PreviewMouseDown += OwnerWindow_PreviewMouseDown;
        window.PreviewKeyDown -= OwnerWindow_PreviewKeyDown;
        window.PreviewKeyDown += OwnerWindow_PreviewKeyDown;
    }

    private void DetachWindowInputHandlers()
    {
        var window = Window.GetWindow(this);
        if (window is null)
            return;

        window.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
        window.PreviewKeyDown -= OwnerWindow_PreviewKeyDown;
    }

    private void OwnerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isPinned || InfoButton.IsMouseOver)
            return;

        ClosePopup(restoreFocus: false);
    }

    private void OwnerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isPinned || e.Key != Key.Escape)
            return;

        ClosePopup(restoreFocus: true);
        e.Handled = true;
    }

    private void CloseOtherPinnedInfoPoint()
    {
        var window = Window.GetWindow(this);
        if (window is null)
            return;

        var state = PinnedByWindow.GetOrCreateValue(window);
        if (state.Current is not null &&
            state.Current.TryGetTarget(out var current) &&
            !ReferenceEquals(current, this))
        {
            current.ClosePopup(restoreFocus: false);
        }
    }

    private void RegisterPinnedInfoPoint()
    {
        var window = Window.GetWindow(this);
        if (window is null)
            return;

        PinnedByWindow.GetOrCreateValue(window).Current = new WeakReference<InfoPoint>(this);
    }

    private void ReleasePinnedInfoPoint()
    {
        var window = Window.GetWindow(this);
        if (window is null || !PinnedByWindow.TryGetValue(window, out var state))
            return;

        if (state.Current is not null &&
            state.Current.TryGetTarget(out var current) &&
            ReferenceEquals(current, this))
        {
            state.Current = null;
        }
    }

    private void InfoPoint_Unloaded(object sender, RoutedEventArgs e)
    {
        CancelTransientClose();
        DetachWindowInputHandlers();
        ReleasePinnedInfoPoint();
    }
}
