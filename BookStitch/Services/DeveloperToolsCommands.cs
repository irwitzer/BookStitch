using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Services;

public static class DeveloperToolsCommands
{
    private static bool _registered;

    public static DeveloperToolsAccessState AccessState { get; } = new();
    public static DeveloperAppearancePreview AppearancePreview { get; } = new(AccessState);

    static DeveloperToolsCommands() => AccessState.PropertyChanged += (_, _) =>
        CommandManager.InvalidateRequerySuggested();

    public static RoutedUICommand OpenDeveloperToolsWindow { get; } = new(
        "Developer Tools",
        nameof(OpenDeveloperToolsWindow),
        typeof(DeveloperToolsCommands));

    public static readonly DependencyProperty QuickMenuEnabledProperty = DependencyProperty.RegisterAttached(
        "QuickMenuEnabled",
        typeof(bool),
        typeof(DeveloperToolsCommands),
        new FrameworkPropertyMetadata(false));

    public static void SetQuickMenuEnabled(DependencyObject element, bool value) =>
        element.SetValue(QuickMenuEnabledProperty, value);

    public static bool GetQuickMenuEnabled(DependencyObject element) =>
        (bool)element.GetValue(QuickMenuEnabledProperty);

    public static void Register()
    {
        if (_registered)
            return;

        CommandManager.RegisterClassCommandBinding(
            typeof(Window),
            new CommandBinding(OpenDeveloperToolsWindow, ExecuteOpenDeveloperToolsWindow, CanExecuteOpenDeveloperToolsWindow));

        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.MouseRightButtonUpEvent,
            new MouseButtonEventHandler(DeveloperToolsButton_MouseRightButtonUp),
            handledEventsToo: true);

        _registered = true;
    }

    private static void CanExecuteOpenDeveloperToolsWindow(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = AccessState.IsEnabled && Application.Current?.MainWindow is BookStitch.MainWindow;
        e.Handled = true;
    }

    private static void ExecuteOpenDeveloperToolsWindow(object sender, ExecutedRoutedEventArgs e)
    {
        RestartDeveloperToolsWindow();
        e.Handled = true;
    }

    private static void DeveloperToolsButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!AccessState.IsEnabled || sender is not Button button || !GetQuickMenuEnabled(button))
            return;

        if (Application.Current?.MainWindow is not BookStitch.MainWindow mainWindow)
            return;

        e.Handled = true;
        mainWindow.OpenDeveloperQuickMenu(button);
    }

    private static void RestartDeveloperToolsWindow()
    {
        if (!AccessState.IsEnabled || Application.Current?.MainWindow is not BookStitch.MainWindow mainWindow)
            return;

        mainWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(mainWindow.RestartDeveloperToolsWindowForModalAccess));
    }
}
