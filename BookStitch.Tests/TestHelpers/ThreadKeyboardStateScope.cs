using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace BookStitch.Tests.TestHelpers;

/// <summary>
/// Supplies consistent thread-local key states for routed keyboard events. In particular,
/// ButtonBase also reads modifier and mouse-button states outside the KeyEventArgs.
/// SetKeyboardState does not change physical input or another thread's input-state table.
/// </summary>
internal sealed class ThreadKeyboardStateScope : IDisposable
{
    private readonly byte[] _saved = new byte[256];
    private readonly byte[] _current = new byte[256];
    private readonly int _thread = Environment.CurrentManagedThreadId;

    public ThreadKeyboardStateScope()
    {
        if (!GetKeyboardState(_saved)) throw new Win32Exception(Marshal.GetLastWin32Error());
        for (var i = 0; i < _current.Length; i++) _current[i] = (byte)(_saved[i] & 1);
        Apply(_current);
    }

    public void SetPressed(Key key, bool pressed)
    {
        var index = KeyInterop.VirtualKeyFromKey(key);
        _current[index] = (byte)((_current[index] & 1) | (pressed ? 0x80 : 0));
        Apply(_current);
    }

    public void Dispose() => Apply(_saved);

    private void Apply(byte[] state)
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Keyboard state must be restored on its owning thread.");
        if (!SetKeyboardState(state)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);
}
