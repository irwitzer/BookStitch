using BookStitch.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Controls;

public partial class DiscCountInputControl : UserControl
{
    private Window? _host;
    public DiscCountInputControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _host = Window.GetWindow(this);
            if (_host is not null) _host.PreviewMouseWheel += Host_MouseWheel;
        };
        Unloaded += (_, _) =>
        {
            if (_host is not null) _host.PreviewMouseWheel -= Host_MouseWheel;
            _host = null;
        };
    }
    private void Host_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled && !IsMouseOver && IsKeyboardFocusWithin)
            Input_MouseWheel(sender, e);
    }
    public DiscCountInputState State => (DiscCountInputState)DataContext;
    public void FocusInput() { Input.Focus(); Input.SelectAll(); }
    private string ProposedText(string text) => Input.Text.Remove(Input.SelectionStart, Input.SelectionLength).Insert(Input.SelectionStart, text);
    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !State.AcceptsInput(ProposedText(e.Text));
    private void Input_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !State.AcceptsInput(ProposedText(text)))
            e.CancelCommand();
    }
    private void Step(int delta)
    {
        Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        State.Step(delta);
        Input.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        Input.SelectAll();
    }
    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        Step(e.Key == Key.Up ? 1 : -1);
        e.Handled = true;
    }
    private void Input_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0) return;
        Step(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }
    private void Increase_Click(object sender, RoutedEventArgs e) => Step(1);
    private void Decrease_Click(object sender, RoutedEventArgs e) => Step(-1);
}
