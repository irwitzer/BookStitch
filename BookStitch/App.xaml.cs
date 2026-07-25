using BookStitch.Services;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BookStitch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string AppUserModelId = "BookStitch.BookStitch";
    private DiagnosticLogService? _diagnosticLogService;
    private bool _fatalDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        InitializeDiagnostics();
        RegisterGlobalExceptionHandlers();
        _diagnosticLogService?.WriteApplicationEvent("APPLICATION START", $"BookStitch wurde gestartet. Version: {GetType().Assembly.GetName().Version}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnosticLogService?.WriteApplicationEvent("APPLICATION EXIT", $"BookStitch wurde beendet. ExitCode: {e.ApplicationExitCode}");
        UnregisterGlobalExceptionHandlers();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _diagnosticLogService?.WriteApplicationEvent("SESSION ENDING", e.ReasonSessionEnding.ToString());

        if (MainWindow is MainWindow mainWindow)
            mainWindow.PrepareForSessionEnding();

        base.OnSessionEnding(e);
    }

    private void InitializeDiagnostics()
    {
        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var projectRootFolder = string.IsNullOrWhiteSpace(settings.WorkingFolder)
                ? settingsService.ProjectRootFolder
                : settings.WorkingFolder;

            _diagnosticLogService = new DiagnosticLogService(projectRootFolder);
        }
        catch
        {
            // Der Anwendungsstart darf nicht allein am Diagnose-Unterbau scheitern.
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void UnregisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException -= App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _diagnosticLogService?.WriteError("Unbehandelte Ausnahme im WPF-Dispatcher", e.Exception);
        e.Handled = true;
        ShowFatalErrorAndShutdown();
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _diagnosticLogService?.WriteError("Unbehandelte Prozessausnahme", exception);
        else
            _diagnosticLogService?.WriteApplicationEvent("UNHANDLED ERROR", e.ExceptionObject?.ToString() ?? "Unbekannte Ausnahme");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _diagnosticLogService?.WriteError("Unbeobachtete Task-Ausnahme", e.Exception);
        e.SetObserved();
    }

    private void ShowFatalErrorAndShutdown()
    {
        if (_fatalDialogShown)
            return;

        _fatalDialogShown = true;

        var logHint = _diagnosticLogService is null
            ? string.Empty
            : $"\n\nEin Diagnoseprotokoll wurde gespeichert unter:\n{_diagnosticLogService.LogFilePath}";

        try
        {
            MessageBox.Show(
                "BookStitch musste wegen eines unerwarteten Fehlers beendet werden." +
                logHint +
                "\n\nBitte starte BookStitch erneut.",
                "BookStitch - Unerwarteter Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Auch ein fehlerhafter Dialog darf die abschließende Beendigung nicht blockieren.
        }

        Shutdown(-1);
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);
}
