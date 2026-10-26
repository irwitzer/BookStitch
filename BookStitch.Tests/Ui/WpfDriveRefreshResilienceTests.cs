using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Drive_refresh_failure_is_localized_preserves_preferences_and_allows_retry(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                var configuration = new ConfiguredDiscDrive { StableId = "serial:STORED", DriveRoot = @"G:\", UserName = "Archive", IsEnabled = false };
                var settings = new AppSettings { DiscDriveOrder = [configuration] };
                var saves = 0;
                using var manager = new GlobalSettingsManager(() => settings, _ => saves++,
                    new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
                var fail = true;
                var control = new SettingsDriveControl(_ => fail
                    ? Task.FromException<IReadOnlyList<DiscDriveInfo>>(new System.IO.IOException("diagnostic-only error"))
                    : Task.FromResult<IReadOnlyList<DiscDriveInfo>>([]));
                control.Initialize(manager);
                DrainDispatcher();
                var hint = Assert.IsType<TextBlock>(control.FindName("DiscDriveRoundHintTextBlock"));
                Assert.Equal(LocalizationText.Get("Settings.Drive.RefreshFailed"), hint.Text);
                Assert.DoesNotContain("diagnostic-only", hint.Text);
                Assert.Same(configuration, Assert.Single(settings.DiscDriveOrder));
                Assert.Equal("Archive", configuration.UserName);
                Assert.False(configuration.IsEnabled);
                Assert.Equal(0, saves);
                Assert.True(Assert.IsType<Button>(control.FindName("DriveRoundUpdateButton")).IsEnabled);
                Assert.True(Assert.IsType<Button>(control.FindName("DriveRoundRefreshButton")).IsEnabled);

                fail = false;
                var retry = control.RefreshDriveCircleListAsync(saveSettings: true);
                DrainDispatcher();
                Assert.True(retry.IsCompletedSuccessfully);
                Assert.Equal(1, saves);
                Assert.Same(configuration, Assert.Single(settings.DiscDriveOrder));
                Assert.Equal("Archive", configuration.UserName);
                Assert.Equal(3, hint.Text.Split(Environment.NewLine).Length);
            }
            finally { CultureInfo.CurrentUICulture = previous; EnsureApplicationResources(); }
        });
    }

    [Fact]
    public void Unloading_drive_settings_cancels_queued_refresh_and_discards_late_probe_results_then_can_reload()
    {
        RunOnStaThread(() =>
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            var settings = new AppSettings
            {
                DiscDriveOrder = [new ConfiguredDiscDrive { StableId = "serial:STORED", UserName = "Archive", DriveRoot = @"G:\" }]
            };
            var saves = 0;
            var calls = 0;
            using var manager = new GlobalSettingsManager(() => settings, _ => saves++,
                new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
            var probe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken probeToken = default;
            var control = new SettingsDriveControl(token =>
            {
                probeToken = token;
                calls++;
                return probe.Task; // Simulate a driver that returns even after cancellation.
            });
            var first = new Window { Content = control, ShowInTaskbar = false };
            first.Show();
            control.Initialize(manager);
            var queued = control.RefreshDriveCircleListAsync(saveSettings: true, reset: true);
            Assert.Equal(1, calls);
            first.Close();
            DrainDispatcher();
            Assert.True(probeToken.IsCancellationRequested);
            var cancellationDeadline = DateTime.UtcNow.AddSeconds(2);
            while (!queued.IsCompleted && DateTime.UtcNow < cancellationDeadline)
            {
                DrainDispatcher();
                System.Threading.Thread.Sleep(1);
            }
            Assert.True(queued.IsCompletedSuccessfully);
            probe.SetResult([]);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!Assert.IsType<Button>(control.FindName("DriveRoundUpdateButton")).IsEnabled && DateTime.UtcNow < deadline)
            {
                DrainDispatcher();
                System.Threading.Thread.Sleep(1);
            }
            Assert.True(Assert.IsType<Button>(control.FindName("DriveRoundUpdateButton")).IsEnabled);
            DrainDispatcher();
            Assert.Equal(0, saves);
            Assert.Equal("Archive", Assert.Single(settings.DiscDriveOrder).UserName);
            first.Content = null;
            var second = new Window { Content = control, ShowInTaskbar = false };
            try
            {
                second.Show();
                DrainDispatcher();
                Assert.Equal(2, calls);
                Assert.False(probeToken.IsCancellationRequested);
                Assert.Equal(3, Assert.IsType<TextBlock>(control.FindName("DiscDriveRoundHintTextBlock")).Text.Split(Environment.NewLine).Length);
            }
            finally { second.Close(); }
        });
    }
}
