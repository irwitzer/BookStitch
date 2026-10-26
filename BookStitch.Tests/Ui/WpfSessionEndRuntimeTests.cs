using BookStitch.Services;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_session_subclass_precedes_Wpf_query_handler_and_only_confirms_true_once(bool logoff)
    {
        RunOnStaThread(() =>
        {
            var wpfQueries = 0;
            var confirmations = new List<bool>();
            var errors = new List<Exception>();
            using var parking = new HwndSource(new HwndSourceParameters("Session-end parking test") { WindowStyle = 0 });
            parking.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) =>
            {
                // Same ordering boundary as Application's HwndWrapper query handler.
                if (message == WindowsSessionEndMonitor.QueryEndSession) wpfQueries++;
                return 0;
            });
            using var monitor = new WindowsSessionEndMonitor(confirmations.Add, errors.Add);
            var flags = logoff ? new nint(unchecked((int)0x80000000)) : 0;
            Assert.Equal(new nint(1), SendSessionTestMessage(parking.Handle, WindowsSessionEndMonitor.QueryEndSession, 0, flags));
            Assert.Empty(confirmations);
            SendSessionTestMessage(parking.Handle, WindowsSessionEndMonitor.EndSession, 0, flags);
            Assert.Empty(confirmations);
            Assert.Equal(0, wpfQueries);
            foreach (var message in new uint[] { 0x02B1, 0x0218 }) // WTSSESSION_CHANGE / POWERBROADCAST
                SendSessionTestMessage(parking.Handle, message, 0, 0);
            Assert.Empty(confirmations);
            SendSessionTestMessage(parking.Handle, WindowsSessionEndMonitor.EndSession, 1, 1); // Restart Manager CLOSEAPP
            Assert.Empty(confirmations);
            SendSessionTestMessage(parking.Handle, WindowsSessionEndMonitor.EndSession, 1, flags);
            SendSessionTestMessage(parking.Handle, WindowsSessionEndMonitor.EndSession, 1, flags);
            Assert.Equal([logoff], confirmations);
            Assert.Empty(errors);
        });
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendSessionTestMessage(nint hwnd, uint message, nint wParam, nint lParam);
}
