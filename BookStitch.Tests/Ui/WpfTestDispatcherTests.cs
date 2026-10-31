using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

[Collection(WpfRuntimeCollection.Name)]
public sealed class WpfTestDispatcherTests
{
    [Fact]
    public void Controls_and_application_use_the_same_live_dispatcher_across_test_calls()
    {
        var threadId = 0;
        WpfTestDispatcher.Run(() =>
        {
            threadId = Environment.CurrentManagedThreadId;
            Assert.True(Application.Current.Dispatcher.CheckAccess());
            Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
        });
        WpfTestDispatcher.Run(() =>
        {
            Assert.Equal(threadId, Environment.CurrentManagedThreadId);
            Assert.False(Application.Current.Dispatcher.HasShutdownStarted);
            var window = new Window();
            try { Assert.Same(Application.Current.Dispatcher, window.Dispatcher); }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Nested_dispatcher_calls_keep_the_outer_tests_state_and_thread()
    {
        WpfTestDispatcher.Run(() =>
        {
            DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Development, () => true);
            var thread = Environment.CurrentManagedThreadId;
            WpfTestDispatcher.Run(() =>
            {
                Assert.Equal(thread, Environment.CurrentManagedThreadId);
                Assert.True(DeveloperToolsCommands.AccessState.IsEnabled);
            });
            Assert.True(DeveloperToolsCommands.AccessState.IsEnabled);
        });
    }

    [Fact]
    public void Failed_test_preserves_its_exception_and_restores_windows_culture_and_developer_state()
    {
        var callerCulture = CultureInfo.CurrentUICulture.Name;
        var error = Assert.Throws<InvalidOperationException>(() => WpfTestDispatcher.Run(() =>
        {
            DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Development, () => true);
            DeveloperToolsCommands.AppearancePreview.SetBackground("Black");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            new Window().Show();
            throw new InvalidOperationException("test assertion sentinel");
        }));
        Assert.Equal("test assertion sentinel", error.Message);
        WpfTestDispatcher.Run(() =>
        {
            Assert.Empty(Application.Current.Windows.Cast<Window>());
            Assert.Null(Application.Current.MainWindow);
            Assert.False(DeveloperToolsCommands.AccessState.IsEnabled);
            Assert.Null(DeveloperToolsCommands.AppearancePreview.BackgroundOverride);
            Assert.Equal(callerCulture, CultureInfo.CurrentUICulture.Name);
        });
    }

    [Fact]
    public void Queued_callback_failure_closes_modal_window_and_reaches_the_test_runner()
    {
        var error = Assert.Throws<InvalidOperationException>(() => WpfTestDispatcher.Run(() =>
        {
            var window = new Window();
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
                throw new InvalidOperationException("dispatcher assertion sentinel")));
            window.ShowDialog();
        }));
        Assert.Equal("dispatcher assertion sentinel", error.Message);
        WpfTestDispatcher.Run(() => Assert.Empty(Application.Current.Windows.Cast<Window>()));
    }

    [Fact]
    public void Leaked_window_fails_its_test_and_is_closed_before_the_next_call()
    {
        var error = Assert.Throws<InvalidOperationException>(() => WpfTestDispatcher.Run(() => { _ = new Window(); }));
        Assert.Contains("Test left windows open", error.Message);
        WpfTestDispatcher.Run(() => Assert.Empty(Application.Current.Windows.Cast<Window>()));
    }

    [Fact]
    public void Keyboard_event_scope_provides_consistent_key_state_and_restores_the_previous_scope()
    {
        WpfTestDispatcher.Run(() =>
        {
            using var outer = new ThreadKeyboardStateScope();
            outer.SetPressed(Key.LeftAlt, true);
            Assert.True(Keyboard.IsKeyDown(Key.LeftAlt));
            using (var inner = new ThreadKeyboardStateScope())
            {
                Assert.False(Keyboard.IsKeyDown(Key.LeftAlt));
                inner.SetPressed(Key.Space, true);
                Assert.True(Keyboard.IsKeyDown(Key.Space));
                inner.SetPressed(Key.Space, false);
                Assert.False(Keyboard.IsKeyDown(Key.Space));
            }
            Assert.True(Keyboard.IsKeyDown(Key.LeftAlt));
        });
    }

    [Fact]
    public void Condition_wait_pumps_pending_dispatcher_work()
    {
        WpfTestDispatcher.Run(() =>
        {
            var ready = false;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ready = true));
            WpfTestDispatcher.WaitUntil(() => ready, "queued binding update");
            Assert.True(ready);
        });
    }
}
