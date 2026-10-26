using BookStitch.Services;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DialogOwnerFocusServiceTests
{
    [Fact]
    public void ModalDialogService_ExternalActionDoesNotQueueOwnerReactivation()
    {
        RunOnStaThread(() =>
        {
            var activations = 0;
            var service = new DialogOwnerFocusService(_ => activations++);
            var owner = new Window();
            var dialog = new Window();
            try
            {
                owner.Show();
                dialog.Loaded += (_, _) => dialog.DialogResult = true;
                Assert.True(ModalDialogService.ShowDialog(dialog, owner, service, restoreOwnerFocus: false));
                // An external window can take focus immediately after the result.
                // Draining pending dispatcher work must not reactivate BookStitch.
                PumpDispatcher();
                Assert.Equal(0, activations);
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                owner.Close();
            }
        });
    }

    [Fact]
    public void RestoreOwnerAfterModalDialog_ActivatesVisibleOwnerOnce()
    {
        RunOnStaThread(() =>
        {
            var activations = 0;
            var service = new DialogOwnerFocusService(_ => activations++);
            var owner = new Window();

            try
            {
                owner.Show();

                service.RestoreOwnerAfterModalDialog(owner);
                PumpDispatcherUntil(() => activations == 1);

                Assert.Equal(1, activations);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void RestoreOwnerAfterModalDialog_IgnoresMissingOrInvisibleOwner()
    {
        RunOnStaThread(() =>
        {
            var activations = 0;
            var service = new DialogOwnerFocusService(_ => activations++);

            service.RestoreOwnerAfterModalDialog(null);
            service.RestoreOwnerAfterModalDialog(new Window());
            PumpDispatcher();

            Assert.Equal(0, activations);
        });
    }

    [Fact]
    public void RestoreOwnerAfterModalDialog_DoesNotPropagateActivationErrors()
    {
        RunOnStaThread(() =>
        {
            var activations = 0;
            var service = new DialogOwnerFocusService(_ =>
            {
                activations++;
                throw new InvalidOperationException("activation failed");
            });
            var owner = new Window();

            try
            {
                owner.Show();

                service.RestoreOwnerAfterModalDialog(owner);
                PumpDispatcherUntil(() => activations == 1);

                Assert.Equal(1, activations);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void ModalDialogService_RestoresOwnerAfterDialogCloses()
    {
        RunOnStaThread(() =>
        {
            var activations = 0;
            var service = new DialogOwnerFocusService(_ => activations++);
            var owner = new Window();
            var dialog = new Window();

            try
            {
                owner.Show();
                dialog.Loaded += (_, _) => dialog.DialogResult = false;

                var result = ModalDialogService.ShowDialog(dialog, owner, service);
                PumpDispatcherUntil(() => activations == 1);

                Assert.False(result);
                Assert.Equal(1, activations);
                Assert.Same(owner, dialog.Owner);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();

                owner.Close();
            }
        });
    }

    private static void PumpDispatcherUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            PumpDispatcher();
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            new Action(() => frame.Continue = false),
            DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        })
        {
            IsBackground = true,
            Name = "BookStitch dialog owner focus test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(
            thread.Join(TimeSpan.FromSeconds(20)),
            "The WPF dialog owner focus test thread did not finish within 20 seconds.");

        if (capturedException is not null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();
    }
}
