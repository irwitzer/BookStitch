using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.TestHelpers;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfRuntimeCollection : ICollectionFixture<WpfTestDispatcherLifetime>
{
    public const string Name = "WPF dispatcher and desktop state";
}

public sealed class WpfTestDispatcherLifetime : IDisposable
{
    public void Dispose() => WpfTestDispatcher.Shutdown();
}

/// <summary>One Application and one live STA dispatcher for the WPF collection.</summary>
internal static class WpfTestDispatcher
{
    private static readonly object Gate = new();
    private static readonly Lazy<Host> Instance = new(() => new Host());

    public static void Run(Action action, int timeoutSeconds = 20, [CallerMemberName] string testName = "")
    {
        // A UI callback is already owned by the outer invocation. Do not wait for
        // its caller's gate or reset state inside a nested test helper.
        if (Instance.IsValueCreated && Instance.Value.Dispatcher.CheckAccess())
        {
            if (Instance.Value.Fault is { } fault)
                throw new InvalidOperationException("The WPF dispatcher is unusable after an earlier failure.", fault);
            action();
            return;
        }
        lock (Gate)
        {
            var host = Instance.Value;
            if (host.Fault is not null)
                throw new InvalidOperationException("The WPF dispatcher is unusable after an earlier failure. Restart the test process.", host.Fault);
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            var operation = host.Dispatcher.InvokeAsync(() =>
            {
                var previousCulture = CultureInfo.CurrentCulture;
                var previousUiCulture = CultureInfo.CurrentUICulture;
                Exception? failure = null;
                host.Stage = $"{testName}: setup";
                host.Unhandled = null;
                host.Active = true;
                try
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = uiCulture;
                    ResetState();
                    host.Stage = $"{testName}: test body";
                    action();
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    host.Stage = $"{testName}: cleanup";
                    try
                    {
                        var leaked = Application.Current.Windows.Cast<Window>().ToArray();
                        if (leaked.Length != 0 && failure is null && host.Unhandled is null)
                            failure = new InvalidOperationException("Test left windows open: " + DescribeWindows());
                        CloseWindows();
                        CultureInfo.CurrentCulture = culture;
                        CultureInfo.CurrentUICulture = uiCulture;
                        ResetState();
                    }
                    catch (Exception error) { failure ??= error; host.Fault = error; }
                    CultureInfo.CurrentCulture = previousCulture;
                    CultureInfo.CurrentUICulture = previousUiCulture;
                    host.Stage = $"{testName}: finished";
                    host.Active = false;
                }
                if ((failure ?? host.Unhandled) is { } errorToReport)
                    ExceptionDispatchInfo.Capture(errorToReport).Throw();
            }, DispatcherPriority.Normal);

            using var deadline = new CancellationTokenSource();
            var expired = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), deadline.Token);
            var finished = Task.WhenAny(operation.Task, expired).GetAwaiter().GetResult();
            deadline.Cancel();
            if (finished != operation.Task)
            {
                var error = new TimeoutException($"WPF test exceeded {timeoutSeconds}s. Last stage: {host.Stage}. " +
                    "The host is quarantined; subsequent WPF tests must not reuse its state.");
                host.Fault = error;
                host.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Console.Error.WriteLine(error.Message + " Open windows: " + DescribeWindows());
                    try { CloseWindows(); }
                    finally { host.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
                }), DispatcherPriority.Send);
                Task.WhenAny(operation.Task, Task.Delay(TimeSpan.FromSeconds(2))).GetAwaiter().GetResult();
                throw error;
            }
            operation.Task.GetAwaiter().GetResult();
        }
    }

    public static void WaitUntil(Func<bool> condition, string description, int timeoutSeconds = 5)
    {
        if (condition()) return;
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (condition() || elapsed.Elapsed >= TimeSpan.FromSeconds(timeoutSeconds)) frame.Continue = false;
        };
        try
        {
            timer.Start();
            Dispatcher.PushFrame(frame);
            if (!condition())
                throw new TimeoutException($"WPF condition not reached: {description}. Open windows: {DescribeWindows()}");
        }
        finally { timer.Stop(); }
    }

    internal static void Shutdown()
    {
        if (!Instance.IsValueCreated) return;
        var host = Instance.Value;
        if (!host.Dispatcher.HasShutdownStarted)
            host.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (!host.Thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("WPF dispatcher did not shut down. Last stage: " + host.Stage);
        if (host.LateFailure is { } error)
            throw new InvalidOperationException("A WPF callback failed outside its test. Last stage: " + host.Stage, error);
    }

    private static string DescribeWindows() => string.Join(", ", Application.Current.Windows.Cast<Window>()
        .Select(window => $"{window.GetType().Name}(visible={window.IsVisible}, active={window.IsActive})"));

    private static void CloseWindows()
    {
        foreach (var window in Application.Current.Windows.Cast<Window>().Reverse().ToArray())
        {
            if (window is MetadataDialog metadata) metadata.WarnBeforeDiscardingConceptDrafts = false;
            window.Close();
        }
        Application.Current.MainWindow = null;
    }

    private static void ResetState()
    {
        EnsureApplicationResources();
        Mouse.Capture(null);
        Keyboard.ClearFocus();
        DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
        DeveloperToolsCommands.AppearancePreview.Clear();
        AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings());
        foreach (var key in LocalizationText.GetKeys()) Application.Current.Resources[key] = LocalizationText.Get(key);
    }

    internal static void EnsureApplicationResources()
    {
        var application = Application.Current;
        application.Dispatcher.VerifyAccess();
        foreach (var theme in new[] { "AppearanceTheme", "WindowChromeStyles", "NavigationDialogStyles", "ProjectTypeVisuals", "ChapterSymbols" })
        {
            var source = $"/BookStitch;component/Themes/{theme}.xaml";
            if (!application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source?.OriginalString == source))
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source, UriKind.Relative) });
        }
        foreach (var key in LocalizationText.GetKeys()) application.Resources[key] = LocalizationText.Get(key);
    }

    private sealed class Host
    {
        public readonly Thread Thread;
        public readonly Dispatcher Dispatcher;
        public volatile Exception? Fault;
        public Exception? Unhandled;
        public Exception? LateFailure;
        public bool Active;
        public volatile string Stage = "starting";

        public Host()
        {
            var started = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread = new Thread(() =>
            {
                try
                {
                    var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    EnsureApplicationResources();
                    dispatcher.UnhandledException += (_, args) =>
                    {
                        Unhandled ??= args.Exception;
                        args.Handled = true;
                        if (!Active)
                        {
                            Fault = LateFailure = args.Exception;
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                        }
                        try { CloseWindows(); }
                        catch (Exception cleanupError)
                        {
                            Fault = new AggregateException(args.Exception, cleanupError);
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                        }
                    };
                    started.TrySetResult(dispatcher);
                    System.Windows.Threading.Dispatcher.Run();
                }
                catch (Exception error) { Fault = error; started.TrySetException(error); }
            }) { IsBackground = true, Name = "BookStitch shared WPF test dispatcher" };
            Thread.SetApartmentState(ApartmentState.STA);
            Thread.Start();
            Dispatcher = started.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
    }
}
