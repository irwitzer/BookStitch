using BookStitch.Models;
using System.Windows;

namespace BookStitch.Services;

public sealed class MainWindowRuntimeComposition
{
    private MainWindowRuntimeComposition(
        DiagnosticLogService diagnosticLogService,
        NotificationService notificationService)
    {
        DiagnosticLogService = diagnosticLogService;
        NotificationService = notificationService;
    }

    public DiagnosticLogService DiagnosticLogService { get; }
    public NotificationService NotificationService { get; }

    public static MainWindowRuntimeComposition Create(
        string projectRootFolder,
        Func<AppSettings> getSettings,
        Func<Window?> getWindow,
        BookStitchEnvironmentContext? environmentContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootFolder);
        ArgumentNullException.ThrowIfNull(getSettings);
        ArgumentNullException.ThrowIfNull(getWindow);

        var diagnosticLogService = new DiagnosticLogService(projectRootFolder);
        ProjectFolderLayoutDiagnostics.Configure(
            diagnosticLogService.WriteApplicationEvent,
            new ProjectFolderLayoutDiagnosticContext(
                environmentContext?.Environment.ToString() ?? "Unknown",
                projectRootFolder));

        return new MainWindowRuntimeComposition(
            diagnosticLogService,
            new NotificationService(
                new SoundNotificationService(getSettings),
                new WindowAttentionService(getSettings, getWindow),
                getSettings));
    }
}
