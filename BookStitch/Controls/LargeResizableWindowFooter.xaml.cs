using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

public partial class LargeResizableWindowFooter : UserControl
{
    public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
        nameof(LeftContent), typeof(object), typeof(LargeResizableWindowFooter), new PropertyMetadata(null));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent), typeof(object), typeof(LargeResizableWindowFooter), new PropertyMetadata(null));

    public LargeResizableWindowFooter()
    {
        InitializeComponent();
    }

    public object? LeftContent
    {
        get => GetValue(LeftContentProperty);
        set => SetValue(LeftContentProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }
}
