using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BookStitch.Controls;

public sealed class DriveCircleRenameEventArgs(string stableId, string userName) : EventArgs
{
    public string StableId { get; } = stableId;
    public string UserName { get; } = userName;
}

public sealed class DriveCircleDragEventArgs(string stableId) : EventArgs
{
    public string StableId { get; } = stableId;
}

public partial class DriveCircleSlot : UserControl
{
    public static readonly DependencyProperty StableIdProperty = DependencyProperty.Register(
        nameof(StableId), typeof(string), typeof(DriveCircleSlot), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DriveLabelProperty = DependencyProperty.Register(
        nameof(DriveLabel), typeof(string), typeof(DriveCircleSlot), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty UserNameProperty = DependencyProperty.Register(
        nameof(UserName), typeof(string), typeof(DriveCircleSlot), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register(
        nameof(IsConnected), typeof(bool), typeof(DriveCircleSlot), new PropertyMetadata(true));
    public static readonly DependencyProperty IsDriveEnabledProperty = DependencyProperty.Register(
        nameof(IsDriveEnabled), typeof(bool), typeof(DriveCircleSlot), new PropertyMetadata(true));
    public static readonly DependencyProperty IsDraggedProperty = DependencyProperty.Register(
        nameof(IsDragged), typeof(bool), typeof(DriveCircleSlot), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPointerPressedProperty = DependencyProperty.Register(
        nameof(IsPointerPressed), typeof(bool), typeof(DriveCircleSlot), new PropertyMetadata(false));
    public static readonly DependencyProperty InfoTitleProperty = DependencyProperty.Register(
        nameof(InfoTitle), typeof(string), typeof(DriveCircleSlot), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty InfoBodyProperty = DependencyProperty.Register(
        nameof(InfoBody), typeof(string), typeof(DriveCircleSlot), new PropertyMetadata(string.Empty));

    private Point _mouseDownPoint;
    private bool _dragCandidate;
    private bool _dragStarted;
    private bool _isEditing;

    public DriveCircleSlot() => InitializeComponent();

    public event EventHandler<DriveCircleRenameEventArgs>? RenameCommitted;
    public event EventHandler? ToggleEnabledRequested;
    public event EventHandler<DriveCircleDragEventArgs>? DragRequested;

    public string StableId { get => (string)GetValue(StableIdProperty); set => SetValue(StableIdProperty, value); }
    public string DriveLabel { get => (string)GetValue(DriveLabelProperty); set => SetValue(DriveLabelProperty, value); }
    public string UserName { get => (string)GetValue(UserNameProperty); set => SetValue(UserNameProperty, value); }
    public bool IsConnected { get => (bool)GetValue(IsConnectedProperty); set => SetValue(IsConnectedProperty, value); }
    public bool IsDriveEnabled { get => (bool)GetValue(IsDriveEnabledProperty); set => SetValue(IsDriveEnabledProperty, value); }
    public bool IsDragged { get => (bool)GetValue(IsDraggedProperty); set => SetValue(IsDraggedProperty, value); }
    public bool IsPointerPressed { get => (bool)GetValue(IsPointerPressedProperty); set => SetValue(IsPointerPressedProperty, value); }
    public string InfoTitle { get => (string)GetValue(InfoTitleProperty); set => SetValue(InfoTitleProperty, value); }
    public string InfoBody { get => (string)GetValue(InfoBodyProperty); set => SetValue(InfoBodyProperty, value); }

    public bool IsEditing => _isEditing;

    public void BeginRename()
    {
        if (_isEditing)
            return;
        _isEditing = true;
        _dragCandidate = false;
        DisplayNameTextBlock.Visibility = Visibility.Collapsed;
        NameEditor.Text = UserName;
        NameEditor.Visibility = Visibility.Visible;
        NameEditor.Focus();
        NameEditor.SelectAll();
    }

    public void CancelRename()
    {
        if (!_isEditing)
            return;
        _isEditing = false;
        NameEditor.Visibility = Visibility.Collapsed;
        DisplayNameTextBlock.Visibility = Visibility.Visible;
    }

    private void CommitRename()
    {
        if (!_isEditing)
            return;
        var name = (NameEditor.Text ?? string.Empty).Trim();
        _isEditing = false;
        NameEditor.Visibility = Visibility.Collapsed;
        DisplayNameTextBlock.Visibility = Visibility.Visible;
        RenameCommitted?.Invoke(this, new DriveCircleRenameEventArgs(StableId, name));
    }

    private void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isEditing || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
            return;
        _dragCandidate = true;
        _dragStarted = false;
        IsPointerPressed = true;
        _mouseDownPoint = e.GetPosition(this);
        Focus();
    }

    private void Slot_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragCandidate || _isEditing || e.LeftButton != MouseButtonState.Pressed)
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _mouseDownPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _mouseDownPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragCandidate = false;
        _dragStarted = true;
        IsPointerPressed = false;
        DragRequested?.Invoke(this, new DriveCircleDragEventArgs(StableId));
    }

    private void Slot_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        IsPointerPressed = false;
        if (_dragCandidate && !_dragStarted && !_isEditing)
        {
            _dragCandidate = false;
            BeginRename();
            e.Handled = true;
        }
        _dragCandidate = false;
        _dragStarted = false;
    }

    private void Slot_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
            return;

        e.Handled = true;
        _dragCandidate = false;
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("BookStitchContextMenuStyle")
        };
        var rename = new MenuItem
        {
            Header = LocalizationText.Get("Settings.Drive.Rename"),
            Style = (Style)FindResource("BookStitchContextMenuItemStyle")
        };
        rename.Click += (_, _) => BeginRename();
        var toggle = new MenuItem
        {
            Header = LocalizationText.Get(IsDriveEnabled ? "Settings.Drive.Disable" : "Settings.Drive.Enable"),
            Style = (Style)FindResource("BookStitchContextMenuItemStyle")
        };
        toggle.Click += (_, _) => ToggleEnabledRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(rename);
        menu.Items.Add(new Separator
        {
            Style = (Style)FindResource("BookStitchContextMenuSeparatorStyle")
        });
        menu.Items.Add(toggle);
        ContextMenu = menu;
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void Slot_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && !_isEditing)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    private void NameEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelRename();
            e.Handled = true;
        }
    }

    private void NameEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isEditing)
            CommitRename();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
