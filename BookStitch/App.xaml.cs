using BookStitch.Models;
using BookStitch.Dialog;
using BookStitch.Services;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace BookStitch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private WindowsSessionEndMonitor? _sessionEndMonitor;
    private const string AppUserModelId = "BookStitch.BookStitch";
    private readonly ProjectEnvironmentSelectionService _projectEnvironmentSelectionService = new();
    private readonly ApplicationRestartService _applicationRestartService = new();
    private BookStitchEnvironmentContext? _activeEnvironmentContext;
    private DiagnosticLogService? _diagnosticLogService;
    private PerformanceTraceService? _performanceTraceService;
    private PerformanceReportStorageService? _performanceStorageService;
    private PerformanceReportArchiveService? _performanceArchiveService;
    private PerformanceToolContextService? _performanceToolContextService;
    private Task<PerformanceToolSnapshot>? _performanceFfmpegContextTask;
    private Task<PerformanceToolSnapshot>? _performanceFfprobeContextTask;
    private PerformanceEnvironmentSnapshot? _performanceEnvironmentSnapshot;
    private AppSettings? _performanceSettings;
    private bool _performanceDiagnosticsEnabledAtStartup;
    private string? _performanceProjectRootFolder;
    private readonly Dictionary<string, PerformanceDriveRole> _performanceDriveRoles = new(StringComparer.OrdinalIgnoreCase);
    private Guid _performanceSessionId;
    private PerformanceOperationScope _startupPerformanceScope;
    private bool _fatalDialogShown;

    internal ProjectEnvironmentSelectionService ProjectEnvironmentSelectionService => _projectEnvironmentSelectionService;
    internal ApplicationRestartService ApplicationRestartService => _applicationRestartService;
    internal PerformanceTraceService? PerformanceTraceService => _performanceTraceService;
    internal DiagnosticLogService? DiagnosticLogService => _diagnosticLogService;
    internal string AppliedUiLanguageId { get; private set; } = UiLanguageService.System;
    internal BookStitchEnvironmentContext ActiveEnvironmentContext =>
        _activeEnvironmentContext ??= new BookStitchEnvironmentResolver(_projectEnvironmentSelectionService).Resolve();

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceService.TryAcquire();
        if (_singleInstance is null)
        {
            var language = new SettingsService(ActiveEnvironmentContext).ReadUiLanguageAtStartup();
            var culture = UiLanguageService.Resolve(language, CultureInfo.CurrentUICulture);
            ShowStandaloneMessage(LocalizationText.Get("Startup.AlreadyRunning.Title", culture),
                LocalizationText.Get("Startup.AlreadyRunning.Message", culture), AppDialogKind.Information);
            Shutdown();
            return;
        }
        // Application.Run has created its hidden parking HWND before dispatching OnStartup.
        // Intercept its query BEFORE WPF can call Application.Shutdown.
        Exception? sessionHookError = null;
        try
        {
            _sessionEndMonitor = new WindowsSessionEndMonitor(
                logoff => (MainWindow as MainWindow)?.PrepareForConfirmedSessionEnding(logoff),
                error => _diagnosticLogService?.WriteError("Confirmed session end failed", error));
        }
        catch (Exception error) { sessionHookError = error; } // Query fallback vetoes; never infer confirmation.
        var startupTimestamp = TimeProvider.System.GetTimestamp();

        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        var diagnosticsTimer = Stopwatch.StartNew();
        var windowsUiCulture = CultureInfo.CurrentUICulture;
        var settings = InitializeDiagnostics(ActiveEnvironmentContext);
        if (sessionHookError is not null)
            _diagnosticLogService?.WriteError("Session-end hook unavailable; uncoordinated queries will be declined", sessionHookError);
        AppliedUiLanguageId = UiLanguageService.Normalize(settings?.UiLanguage);
        UiLanguageService.ApplyAtStartup(settings?.UiLanguage, windowsUiCulture);
        foreach (var key in LocalizationText.GetKeys())
            Resources[key] = LocalizationText.Get(key);
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(UiLanguageService.German)));
        diagnosticsTimer.Stop();

        TryStartPerformanceDiagnostics(settings, startupTimestamp);
        RegisterGlobalExceptionHandlers();
        DeveloperToolsCommands.Register();

        var preWindowElapsed = TimeProvider.System.GetElapsedTime(
            startupTimestamp,
            TimeProvider.System.GetTimestamp());

        _diagnosticLogService?.WriteApplicationEvent(
            "APPLICATION START",
            $"BookStitch wurde gestartet. Version: {GetType().Assembly.GetName().Version}");
        _diagnosticLogService?.WritePerformanceEvent(
            "APPLICATION PRE-WINDOW STARTUP",
            preWindowElapsed,
            $"Diagnostics={diagnosticsTimer.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ms");

        if (!string.IsNullOrWhiteSpace(_performanceProjectRootFolder))
        {
            var projectFolders = WorkFolderStructure.FromRoot(_performanceProjectRootFolder);
            var rollbackFailures = new ProjectExtensionRollbackService().RecoverInterruptedAttempts(
                [projectFolders.LocalProjectsFolder, projectFolders.Mp3DiscProjectsFolder, projectFolders.AudioDiscProjectsFolder]);
            foreach (var failure in rollbackFailures)
                _diagnosticLogService?.WriteApplicationEvent("PROJECT ATTEMPT RECOVERY FAILED", failure);
            if (rollbackFailures.Count > 0)
                ShowStandaloneMessage(LocalizationText.Get("ProjectAttempt.Recovery.Heading"),
                    LocalizationText.Get("ProjectAttempt.Recovery.Message"), AppDialogKind.Warning);
            var completion = new DeferredProjectCompletionService(_performanceProjectRootFolder,
                ActiveEnvironmentContext.Environment.ToString()).ProcessStartup();
            _diagnosticLogService?.WriteApplicationEvent("DEFERRED PROJECT COMPLETION", completion.Detail);
            if (completion.NeedsAttention)
                ShowStandaloneMessage(LocalizationText.Get("DeferredCompletion.Attention.Heading"),
                    LocalizationText.Get("DeferredCompletion.Attention.Message"), AppDialogKind.Warning);
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnosticLogService?.WriteApplicationEvent("APPLICATION EXIT", $"BookStitch wurde beendet. ExitCode: {e.ApplicationExitCode}");
        CompletePerformanceDiagnosticsSession(
            e.ApplicationExitCode == 0
                ? PerformanceOutcome.Success
                : PerformanceOutcome.Failed);
        UnregisterGlobalExceptionHandlers();
        _sessionEndMonitor?.Dispose();
        _sessionEndMonitor = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);

        try
        {
            _applicationRestartService.TryRelaunchOnExit();
        }
        catch (Exception ex)
        {
            _diagnosticLogService?.WriteApplicationEvent("APPLICATION RESTART FAILED", ex.Message);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Fallback only: the native subclass acknowledges QUERY without invoking WPF.
        // Never let an uncoordinated query prematurely call Shutdown or cancel workers.
        _diagnosticLogService?.WriteApplicationEvent("UNCOORDINATED SESSION QUERY", e.ReasonSessionEnding.ToString());
        e.Cancel = true;
        base.OnSessionEnding(e);
        e.Cancel = true;
    }

    private AppSettings? InitializeDiagnostics(BookStitchEnvironmentContext environmentContext)
    {
        try
        {
            // Read-only startup snapshot: mutable runtime ownership begins with
            // MainWindowFoundationComposition.GlobalSettingsManager.
            var settingsService = new SettingsService(environmentContext);
            var settings = settingsService.Load();
            _performanceDiagnosticsEnabledAtStartup = new DebugSettingsService(settingsService)
                .Load()
                .PerformanceDiagnosticsEnabled && DeveloperToolsAccessState.IsEnabledFor(settings, environmentContext.Environment);
            var projectRootFolder = string.IsNullOrWhiteSpace(settings.WorkingFolder)
                ? settingsService.ProjectRootFolder
                : settings.WorkingFolder;

            _diagnosticLogService = new DiagnosticLogService(projectRootFolder);
            _performanceSettings = settings;
            _performanceProjectRootFolder = projectRootFolder;
            return settings;
        }
        catch
        {
            // Der Anwendungsstart darf nicht allein am Diagnose-Unterbau scheitern.
            return null;
        }
    }

    private void TryStartPerformanceDiagnostics(AppSettings? settings, long startupTimestamp)
    {
        if (settings is null || !_performanceDiagnosticsEnabledAtStartup)
            return;

        try
        {
            var startupNowTimestamp = TimeProvider.System.GetTimestamp();
            var startupUtc = TimeProvider.System.GetUtcNow() -
                             TimeProvider.System.GetElapsedTime(startupTimestamp, startupNowTimestamp);
            var traceService = CreatePerformanceTraceService(startupUtc);
            var sessionStarted = traceService.TryStartSession(
                new PerformanceSessionStartContext
                {
                    StartReason = PerformanceSessionStartReason.ApplicationStartup,
                    ApplicationVersion = GetType().Assembly.GetName().Version?.ToString() ?? string.Empty,
                    BuildConfiguration = GetBuildConfiguration(),
                    SelectedParallelJobs = settings.SelectedParallelJobs
                },
                startedTimestamp: startupTimestamp,
                startedUtc: startupUtc,
                sessionId: _performanceSessionId == Guid.Empty ? null : _performanceSessionId);

            if (!sessionStarted)
                return;

            _performanceTraceService = traceService;
            PerformanceMeasurement.ApplicationTrace = traceService;
            _startupPerformanceScope = traceService.StartOperation(
                "app.startup",
                startedTimestamp: startupTimestamp);
        }
        catch
        {
            _performanceTraceService = null;
            _startupPerformanceScope = default;
            PerformanceMeasurement.ApplicationTrace = null;
        }
    }

    internal void SetPerformanceDiagnosticsEnabled(bool enabled, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        enabled = enabled && DeveloperToolsAccessState.IsEnabledFor(settings, ActiveEnvironmentContext.Environment);

        try
        {
            if (!enabled)
            {
                var traceService = _performanceTraceService;
                if (traceService is null)
                    return;

                traceService.SetEnabled(false);
                if (traceService.ActiveScopeCount == 0)
                    CompletePerformanceDiagnosticsSession(PerformanceOutcome.Success);
                return;
            }

            var existingTraceService = _performanceTraceService;
            if (existingTraceService is not null && existingTraceService.ActiveSessionId.HasValue)
            {
                existingTraceService.SetEnabled(true);
                return;
            }

            _performanceSettings = settings;
            var startedUtc = TimeProvider.System.GetUtcNow();
            var traceServiceToStart = CreatePerformanceTraceService(startedUtc);
            if (!traceServiceToStart.TryStartSession(
                    new PerformanceSessionStartContext
                    {
                        StartReason = PerformanceSessionStartReason.UserEnabled,
                        ApplicationVersion = GetType().Assembly.GetName().Version?.ToString() ?? string.Empty,
                        BuildConfiguration = GetBuildConfiguration(),
                        SelectedParallelJobs = settings.SelectedParallelJobs
                    },
                    startedUtc: startedUtc,
                    sessionId: _performanceSessionId == Guid.Empty ? null : _performanceSessionId))
            {
                return;
            }

            _performanceTraceService = traceServiceToStart;
            PerformanceMeasurement.ApplicationTrace = traceServiceToStart;
        }
        catch
        {
            // Performance-Diagnostik ist rein diagnostisch und darf UI-Aktionen nie beeinträchtigen.
        }
    }

    internal PerformanceOperationScope StartPerformanceOperation(
        string operationName,
        PerformanceProjectType projectType = PerformanceProjectType.Unknown,
        PerformanceWorkloadSnapshot? workload = null)
    {
        var traceService = _performanceTraceService;
        if (traceService is null)
            return default;

        try
        {
            return traceService.StartOperation(
                operationName,
                projectType: projectType,
                workload: workload);
        }
        catch
        {
            return default;
        }
    }

    internal PerformancePhaseScope StartPerformancePhase(Guid operationId, string phaseName)
    {
        var traceService = _performanceTraceService;
        if (traceService is null)
            return default;

        try
        {
            return traceService.StartPhase(operationId, phaseName);
        }
        catch
        {
            return default;
        }
    }

    internal PerformanceExternalProcessContext? CreatePerformanceExternalProcessContext(
        Guid operationId,
        Guid? phaseId = null,
        string? purpose = null)
    {
        var traceService = _performanceTraceService;
        if (traceService is null || !traceService.IsEnabled || operationId == Guid.Empty)
            return null;

        try
        {
            return new PerformanceExternalProcessContext(traceService, operationId, phaseId, purpose);
        }
        catch
        {
            return null;
        }
    }

    internal void UpdatePerformanceWorkload(
        Guid operationId,
        PerformanceWorkloadSnapshot workload)
    {
        if (operationId == Guid.Empty || workload is null)
            return;

        try
        {
            _performanceTraceService?.UpdateOperationWorkload(operationId, workload);
        }
        catch
        {
            // Performance-Kontext darf den Fachworkflow nie beeinflussen.
        }
    }

    internal void RecordPerformanceDriveRole(
        Guid operationId,
        string? path,
        PerformanceDriveRole roles)
    {
        if (operationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(path) ||
            roles == PerformanceDriveRole.None)
        {
            return;
        }

        var traceService = _performanceTraceService;
        if (traceService is null)
            return;

        try
        {
            var driveId = PerformanceDriveContextService.CreateDriveIdForPath(path);
            if (driveId != Guid.Empty)
            {
                traceService.RecordDriveRoleReference(operationId, driveId, roles);
                var root = DiscDriveConfigurationService.NormalizeRootPath(path);
                if (!string.IsNullOrWhiteSpace(root))
                {
                    _performanceDriveRoles.TryGetValue(root, out var existingRoles);
                    _performanceDriveRoles[root] = existingRoles | roles;
                }
            }
        }
        catch
        {
            // Nur Drive-ID und Rolle sind Performance-Kontext; Fehler bleiben diagnostisch isoliert.
        }
    }

    internal void TryFinalizeDisabledPerformanceSession()
    {
        try
        {
            var traceService = _performanceTraceService;
            if (traceService is null || traceService.IsEnabled || traceService.ActiveScopeCount != 0)
                return;

            CompletePerformanceDiagnosticsSession(PerformanceOutcome.Success);
        }
        catch
        {
            // Runtime-Abschaltung der Performance-Diagnostik darf keinen Workflow beeinflussen.
        }
    }

    internal void CompletePerformanceStartup()
    {
        if (!_startupPerformanceScope.IsActive)
            return;

        try
        {
            _startupPerformanceScope.Complete();
        }
        catch
        {
            // Performance-Diagnostik darf den sichtbaren Anwendungsstart nie beeinflussen.
        }
        finally
        {
            _startupPerformanceScope = default;
        }
    }

    private void CompletePerformanceDiagnosticsSession(PerformanceOutcome outcome)
    {
        var traceService = _performanceTraceService;
        if (traceService is null)
            return;

        try
        {
            PopulatePerformanceSessionContext(traceService);
            var report = traceService.CompleteSession(outcome);
            if (report is not null)
            {
                report = new PerformanceStatisticsService().Apply(report);
                PersistPerformanceReport(report);
            }
        }
        catch (Exception ex)
        {
            _diagnosticLogService?.WriteError("Performance session finalization failed during shutdown", ex);
        }
        finally
        {
            CompletePerformanceJournalBestEffort();
            PerformanceMeasurement.ApplicationTrace = null;
            _performanceTraceService = null;
            _performanceStorageService = null;
            _performanceArchiveService = null;
            _performanceToolContextService = null;
            _performanceFfmpegContextTask = null;
            _performanceFfprobeContextTask = null;
            _performanceEnvironmentSnapshot = null;
            _performanceDriveRoles.Clear();
            _performanceSessionId = Guid.Empty;
            _startupPerformanceScope = default;
        }
    }

    private PerformanceTraceService CreatePerformanceTraceService(DateTimeOffset startedUtc)
    {
        var sessionId = Guid.NewGuid();
        _performanceSessionId = sessionId;
        _performanceDriveRoles.Clear();
        _performanceStorageService = null;
        _performanceArchiveService = null;
        _performanceToolContextService = new PerformanceToolContextService();
        try { _performanceEnvironmentSnapshot = new PerformanceEnvironmentContextService().Capture(); } catch { _performanceEnvironmentSnapshot = null; }
        try
        {
            _performanceFfmpegContextTask = _performanceToolContextService.CaptureAsync(_performanceSettings?.FfmpegPath);
            _performanceFfprobeContextTask = _performanceToolContextService.CaptureAsync(_performanceSettings?.FfprobePath);
        }
        catch
        {
            _performanceFfmpegContextTask = null;
            _performanceFfprobeContextTask = null;
        }

        if (!string.IsNullOrWhiteSpace(_performanceProjectRootFolder))
        {
            try
            {
                _performanceStorageService = new PerformanceReportStorageService(
                    _performanceProjectRootFolder,
                    sessionId,
                    startedUtc);
                _performanceArchiveService = new PerformanceReportArchiveService(_performanceProjectRootFolder);
                _performanceArchiveService.CleanupOldSessions();
            }
            catch
            {
                _performanceStorageService = null;
                _performanceArchiveService = null;
            }
        }

        return new PerformanceTraceService(
            enabled: true,
            journalEventSink: journalEvent => _performanceStorageService?.TryWriteJournalEvent(journalEvent));
    }

    private void PopulatePerformanceSessionContext(PerformanceTraceService traceService)
    {
        var environment = _performanceEnvironmentSnapshot;
        PerformanceToolSnapshot? ffmpeg = null;
        PerformanceToolSnapshot? ffprobe = null;
        IReadOnlyList<PerformanceDriveSnapshot> drives = [];

        try
        {
            if (_performanceFfmpegContextTask?.IsCompletedSuccessfully == true)
                ffmpeg = _performanceFfmpegContextTask.Result;
            if (_performanceFfprobeContextTask?.IsCompletedSuccessfully == true)
                ffprobe = _performanceFfprobeContextTask.Result;
        }
        catch { }

        try
        {
            var pathRoles = _performanceDriveRoles
                .Select(item => new PerformanceDrivePathRole(item.Key, item.Value))
                .ToList();
            drives = new PerformanceDriveContextService().Capture(pathRoles, _performanceSettings);
        }
        catch { }

        traceService.SetSessionContext(environment, ffmpeg, ffprobe, drives);
    }

    private void PersistPerformanceReport(PerformanceSessionReport report)
    {
        var archive = _performanceArchiveService;
        var storage = _performanceStorageService;
        if (archive is null || storage is null)
            return;

        try
        {
            archive.WriteReportsAsync(storage.SessionFolder, report).GetAwaiter().GetResult();
        }
        catch
        {
            // Final report persistence remains diagnostic-only.
        }
    }

    private void CompletePerformanceJournalBestEffort()
    {
        var storage = _performanceStorageService;
        if (storage is null)
            return;

        try
        {
            storage.CompleteJournalAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Bounded best effort: shutdown must not wait indefinitely for diagnostics I/O.
        }
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
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
            : LocalizationText.Format("App.FatalError.LogHint", _diagnosticLogService.LogFilePath);

        try
        {
            ShowStandaloneMessage(
                LocalizationText.Get("App.FatalError.Title"),
                LocalizationText.Format("App.FatalError.Message", logHint),
                AppDialogKind.Error);
        }
        catch
        {
            // Auch ein fehlerhafter Dialog darf die abschließende Beendigung nicht blockieren.
        }

        Shutdown(-1);
    }

    private static void ShowStandaloneMessage(string title, string message, AppDialogKind kind)
    {
        // Before StartupUri creates the main window, closing this sole dialog must not request shutdown.
        var application = Current;
        var previousMode = application.ShutdownMode;
        var previousMainWindow = application.MainWindow;
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var dialog = new AppMessageDialog(title, title, message, kind, details: null, buttons: null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true
            };
            dialog.ShowDialog();
        }
        finally
        {
            application.MainWindow = previousMainWindow;
            application.ShutdownMode = previousMode;
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);
}
