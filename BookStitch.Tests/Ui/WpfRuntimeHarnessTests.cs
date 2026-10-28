using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Runtime_harness_returns_awaited_ui_work_to_the_owning_dispatcher()
    {
        RunOnStaThread(() =>
        {
            var ownerThread = Environment.CurrentManagedThreadId;
            Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);

            async Task<int> ResumeOnUiThread()
            {
                await Task.Yield();
                return Environment.CurrentManagedThreadId;
            }

            var continuation = ResumeOnUiThread();
            Assert.False(continuation.IsCompleted);
            DrainDispatcher();
            Assert.True(continuation.IsCompleted);
            Assert.Equal(ownerThread, continuation.GetAwaiter().GetResult());
        });
    }
}
