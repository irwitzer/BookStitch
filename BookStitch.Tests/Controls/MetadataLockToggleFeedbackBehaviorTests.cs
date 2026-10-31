using BookStitch.Tests.TestHelpers;
using BookStitch.Controls;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Controls;

[Collection(WpfRuntimeCollection.Name)]
public sealed class MetadataLockToggleFeedbackBehaviorTests
{
    [Fact]
    public void AcceptedLock_RemainsLockedWithoutRejectionPulse()
    {
        RunOnStaThread(() =>
        {
            var source = new LockStateProbe(initialValue: false, rejectLockRequests: false);
            var toggle = CreateToggle(source);

            Activate(toggle);

            Assert.True(source.IsLocked);
            Assert.True(toggle.IsChecked == true);
            Assert.False(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));
        });
    }

    [Fact]
    public void RejectedLock_ReturnsToUnlockedAndPulsesThenResets()
    {
        RunOnStaThread(() =>
        {
            var source = new LockStateProbe(initialValue: false, rejectLockRequests: true);
            var toggle = CreateToggle(source);

            Activate(toggle);

            Assert.False(source.IsLocked);
            Assert.True(toggle.IsChecked == false);
            Assert.True(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));

            WaitForDispatcher(TimeSpan.FromMilliseconds(330));

            Assert.False(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));
        });
    }

    [Fact]
    public void Unlock_CompletesWithoutRejectionPulse()
    {
        RunOnStaThread(() =>
        {
            var source = new LockStateProbe(initialValue: true, rejectLockRequests: false);
            var toggle = CreateToggle(source);

            Activate(toggle);

            Assert.False(source.IsLocked);
            Assert.True(toggle.IsChecked == false);
            Assert.False(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));
        });
    }

    [Fact]
    public void RepeatedRejectedLocks_ExtendLatestPulseWithoutOlderReset()
    {
        RunOnStaThread(() =>
        {
            var source = new LockStateProbe(initialValue: false, rejectLockRequests: true);
            var toggle = CreateToggle(source);

            Activate(toggle);
            Assert.True(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));

            WaitForDispatcher(TimeSpan.FromMilliseconds(120));
            Activate(toggle);
            WaitForDispatcher(TimeSpan.FromMilliseconds(160));

            Assert.True(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));

            WaitForDispatcher(TimeSpan.FromMilliseconds(150));
            Assert.False(MetadataLockToggleFeedbackBehavior.GetIsRejectedFeedbackActive(toggle));
        });
    }

    private static TestToggleButton CreateToggle(LockStateProbe source)
    {
        var toggle = new TestToggleButton();
        BindingOperations.SetBinding(
            toggle,
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(LockStateProbe.IsLocked))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        MetadataLockToggleFeedbackBehavior.SetIsEnabled(toggle, true);
        toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        return toggle;
    }

    private static void Activate(TestToggleButton toggle)
    {
        MetadataLockToggleFeedbackBehavior.CaptureActivationState(toggle);
        toggle.InvokeClick();
        DrainDispatcher();
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitForDispatcher(TimeSpan delay)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher.CurrentDispatcher)
        {
            Interval = delay
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action,
        [System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        WpfTestDispatcher.Run(action, 5, testName);

    private sealed class TestToggleButton : ToggleButton
    {
        public void InvokeClick() => OnClick();
    }

    private sealed class LockStateProbe : INotifyPropertyChanged
    {
        private readonly bool _rejectLockRequests;
        private bool _isLocked;

        public LockStateProbe(bool initialValue, bool rejectLockRequests)
        {
            _isLocked = initialValue;
            _rejectLockRequests = rejectLockRequests;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (value && _rejectLockRequests)
                    return;
                if (_isLocked == value)
                    return;

                _isLocked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
            }
        }
    }
}
