using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BookStitch.Services;

/// <summary>
/// Subclasses the UI thread's existing top-level windows, including WPF's hidden
/// Application parking window. WPF otherwise calls Shutdown during QUERYENDSESSION.
/// Install from Application.OnStartup, after Run has created that parking window.
/// </summary>
internal sealed class WindowsSessionEndMonitor : IDisposable
{
    internal const uint QueryEndSession = 0x0011;
    internal const uint EndSession = 0x0016;
    private const uint NcDestroy = 0x0082;
    private const long Logoff = 0x80000000L;
    private const long CloseApp = 1;
    private readonly SubclassProc _callback;
    private readonly List<nint> _windows = [];
    private readonly Action<bool> _confirmed;
    private readonly Action<Exception> _failed;
    private bool _completed;

    public WindowsSessionEndMonitor(Action<bool> confirmed, Action<Exception> failed)
    {
        _confirmed = confirmed;
        _failed = failed;
        _callback = WindowProc;
        try
        {
            Exception? registrationError = null;
            EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
            {
                if (!SetWindowSubclass(hwnd, _callback, 1, 0))
                {
                    registrationError = new Win32Exception(Marshal.GetLastWin32Error());
                    return false;
                }
                _windows.Add(hwnd);
                return true;
            }, 0);
            if (registrationError is not null) throw registrationError;
            if (_windows.Count == 0) throw new InvalidOperationException("No UI-thread session window is available.");
        }
        catch { Dispose(); throw; }
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        // No mutation, cancellation, dispatcher work or marker on a query/denial.
        if (message == QueryEndSession) return 1;
        if (message == EndSession)
        {
            if (wParam != 0 && !_completed && (lParam.ToInt64() & CloseApp) == 0)
            {
                _completed = true;
                try { _confirmed((lParam.ToInt64() & Logoff) != 0); }
                catch (Exception error)
                {
                    try { _failed(error); }
                    catch { System.Diagnostics.Trace.TraceError("Confirmed session-end callback failed: {0}", error); }
                }
            }
            return 0;
        }
        if (message == NcDestroy)
        {
            RemoveWindowSubclass(hwnd, _callback, id);
            _windows.Remove(hwnd);
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        foreach (var hwnd in _windows.ToArray()) RemoveWindowSubclass(hwnd, _callback, 1);
        _windows.Clear();
    }

    private delegate nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumWindowProc(nint hwnd, nint data);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindowProc callback, nint data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);
}
