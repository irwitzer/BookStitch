using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BookStitch.Dialog;

public static class DialogWindowBehavior
{
    public static readonly DependencyProperty IsDragRegionProperty = DependencyProperty.RegisterAttached(
        "IsDragRegion",
        typeof(bool),
        typeof(DialogWindowBehavior),
        new PropertyMetadata(false, OnIsDragRegionChanged));


    public static readonly DependencyProperty DragRegionHeightProperty = DependencyProperty.RegisterAttached(
        "DragRegionHeight",
        typeof(double),
        typeof(DialogWindowBehavior),
        new PropertyMetadata(double.PositiveInfinity));

    public static void SetDragRegionHeight(DependencyObject element, double value) =>
        element.SetValue(DragRegionHeightProperty, value);

    public static double GetDragRegionHeight(DependencyObject element) =>
        (double)element.GetValue(DragRegionHeightProperty);

    public static void SetIsDragRegion(DependencyObject element, bool value) =>
        element.SetValue(IsDragRegionProperty, value);

    public static bool GetIsDragRegion(DependencyObject element) =>
        (bool)element.GetValue(IsDragRegionProperty);

    private static void OnIsDragRegionChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not UIElement element)
            return;

        element.PreviewMouseLeftButtonDown -= DragRegion_PreviewMouseLeftButtonDown;
        if (e.NewValue is true)
            element.PreviewMouseLeftButtonDown += DragRegion_PreviewMouseLeftButtonDown;
    }

    private static void DragRegion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || e.ClickCount != 1)
            return;

        if (sender is not UIElement dragElement)
            return;

        var dragRegionHeight = GetDragRegionHeight(dragElement);
        if (e.GetPosition(dragElement).Y > dragRegionHeight)
            return;

        if (HasInteractiveAncestor(e.OriginalSource as DependencyObject, dragElement))
            return;

        var window = Window.GetWindow(dragElement);
        if (window is null)
            return;

        try
        {
            window.DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // Der Maustaster wurde zwischen Ereignis und DragMove bereits losgelassen.
        }
    }

    private static bool HasInteractiveAncestor(DependencyObject? current, DependencyObject? dragRegion)
    {
        while (current is not null && !ReferenceEquals(current, dragRegion))
        {
            if (current is ButtonBase)
                return true;

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return current switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(current),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => null
        };
    }
}

