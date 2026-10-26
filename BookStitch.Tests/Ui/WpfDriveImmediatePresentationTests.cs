using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_drive_cards_render_before_blocked_diagnosis_without_writes_or_online_claims(bool enabled)
    {
        RunOnStaThread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var settings = StoredDriveSettings(enabled);
            var before = JsonSerializer.Serialize(settings.DiscDriveOrder);
            var saves = 0;
            using var manager = new GlobalSettingsManager(() => settings, _ => saves++,
                new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
            var probe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var control = new SettingsDriveControl(_ => { calls++; return probe.Task; });
            control.Initialize(manager);
            var panel = Assert.IsType<StackPanel>(control.FindName("DiscDriveSlotPanel"));
            var cards = panel.Children.Cast<DriveCircleSlot>().ToArray();
            Assert.Equal(new[] { "G:", "X:" }, cards.Select(card => card.DriveLabel));
            Assert.Equal(new[] { "Archive", "Reader" }, cards.Select(card => card.UserName));
            Assert.False(cards[0].IsDriveEnabled);
            Assert.True(cards[1].IsDriveEnabled);
            Assert.All(cards, card =>
            {
                Assert.False(card.IsConnected);
                Assert.Contains(LocalizationText.Get("Disc.Drive.Checking"), card.InfoBody);
                Assert.Contains(LocalizationText.Get("Disc.Drive.Checking"), AutomationProperties.GetName(card));
                Assert.Same(Application.Current.FindResource("DriveCircleSlotBorderStyle"),
                    Assert.IsType<Border>(card.Content).Style);
            });
            Assert.Equal(enabled ? 1d : 0.62d, panel.Opacity);
            Assert.Equal(before, JsonSerializer.Serialize(settings.DiscDriveOrder));
            Assert.Equal(0, saves);
            Assert.Equal(1, calls);
            var window = new Window { Content = control, ShowInTaskbar = false };
            try
            {
                window.Show();
                probe.SetResult([FixtureDrive(@"X:\", "serial:X"), FixtureDrive(@"H:\", "serial:H")]);
                PumpDriveFixtureUntil(() => panel.Children.Count == 3);
                cards = panel.Children.Cast<DriveCircleSlot>().ToArray();
                Assert.Equal(new[] { "G:", "X:", "H:" }, cards.Select(card => card.DriveLabel));
                Assert.False(cards[0].IsConnected);
                Assert.True(cards[1].IsConnected);
                Assert.True(cards[2].IsConnected);
                Assert.Equal("Archive", cards[0].UserName);
                Assert.False(cards[0].IsDriveEnabled);
                Assert.Equal("Reader", cards[1].UserName);
                Assert.Equal(enabled ? 1d : 0.62d, panel.Opacity);
                Assert.Equal(1, saves);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Superseded_probe_and_queued_update_cannot_overwrite_latest_preferences_or_reset(bool reset)
    {
        RunOnStaThread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var settings = StoredDriveSettings(true);
            var saves = 0;
            using var manager = new GlobalSettingsManager(() => settings, _ => saves++,
                new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
            var oldProbe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var currentProbe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var control = new SettingsDriveControl(_ => ++calls == 1 ? oldProbe.Task : currentProbe.Task);
            var window = new Window { Content = control, ShowInTaskbar = false };
            try
            {
                window.Show();
                control.Initialize(manager);
                var superseded = control.RefreshDriveCircleListAsync(saveSettings: true);
                var configuration = new DiscDriveConfigurationService();
                configuration.MoveDriveToIndex(settings, "serial:X", 0);
                configuration.RenameDrive(settings, "serial:X", "Changed name");
                configuration.SetEnabled(settings, "serial:X", false);
                var latest = control.RefreshDriveCircleListAsync(saveSettings: true, reset: reset);
                oldProbe.SetResult([FixtureDrive(@"Z:\", "serial:Z")]);
                PumpDriveFixtureUntil(() => calls == 2);
                Assert.Equal(0, saves);
                Assert.Equal("serial:X", settings.DiscDriveOrder[0].StableId);
                Assert.DoesNotContain(settings.DiscDriveOrder, item => item.StableId == "serial:Z");
                Assert.True(superseded.IsCompletedSuccessfully);
                currentProbe.SetResult([FixtureDrive(@"X:\", "serial:X")]);
                PumpDriveFixtureUntil(() => latest.IsCompleted);
                Assert.True(latest.IsCompletedSuccessfully);
                Assert.Equal(2, calls); // No hardware query for the obsolete queued refresh.
                Assert.Equal(1, saves);
                var cards = Assert.IsType<StackPanel>(control.FindName("DiscDriveSlotPanel")).Children.Cast<DriveCircleSlot>().ToArray();
                Assert.Equal("serial:X", cards[0].StableId);
                Assert.True(cards[0].IsConnected);
                if (reset)
                {
                    Assert.Single(cards);
                    Assert.Equal(string.Empty, cards[0].UserName);
                    Assert.True(cards[0].IsDriveEnabled);
                }
                else
                {
                    Assert.Equal(2, cards.Length);
                    Assert.Equal("Changed name", cards[0].UserName);
                    Assert.False(cards[0].IsDriveEnabled);
                    Assert.Equal("Archive", cards[1].UserName);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Empty_configuration_stays_empty_until_diagnosis_and_reinitialization_discards_old_owner()
    {
        RunOnStaThread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var firstSettings = StoredDriveSettings(true);
            var secondSettings = new AppSettings();
            var saves = 0;
            using var firstManager = new GlobalSettingsManager(() => firstSettings, _ => saves++,
                new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
            using var secondManager = new GlobalSettingsManager(() => secondSettings, _ => saves++,
                new TestGlobalSettingsSaveScheduler(), GlobalSettingsManager.DefaultDebounceDelay);
            var oldProbe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var currentProbe = new TaskCompletionSource<IReadOnlyList<DiscDriveInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var control = new SettingsDriveControl(_ => ++calls == 1 ? oldProbe.Task : currentProbe.Task);
            control.Initialize(firstManager);
            control.Initialize(secondManager);
            var panel = Assert.IsType<StackPanel>(control.FindName("DiscDriveSlotPanel"));
            Assert.Empty(panel.Children);
            Assert.Equal(LocalizationText.Get("UiKeineCDDVDLaufwerkeErkannt"),
                Assert.IsType<TextBlock>(control.FindName("DiscDriveRoundHintTextBlock")).Text);
            oldProbe.SetResult([FixtureDrive(@"Z:\", "serial:Z")]);
            PumpDriveFixtureUntil(() => calls == 2);
            Assert.Empty(secondSettings.DiscDriveOrder);
            Assert.Equal(0, saves);
            currentProbe.SetResult([FixtureDrive(@"H:\", "serial:H")]);
            PumpDriveFixtureUntil(() => panel.Children.Count == 1);
            Assert.Equal("serial:H", Assert.Single(secondSettings.DiscDriveOrder).StableId);
            Assert.Equal(2, firstSettings.DiscDriveOrder.Count);
            Assert.Equal(1, saves);
        });
    }

    private static AppSettings StoredDriveSettings(bool enabled) => new()
    {
        ExperimentalDriveRoundEnabled = enabled,
        DiscDriveOrder =
        [
            new() { StableId = "serial:X", DriveRoot = @"X:\", UserName = "Reader", Order = 4, IsEnabled = true },
            new() { StableId = "serial:G", DriveRoot = @"G:\", UserName = "Archive", Order = 1, IsEnabled = false }
        ]
    };

    private static DiscDriveInfo FixtureDrive(string root, string id) => new(root, root[..2], false,
        string.Empty, DiscMediaKind.Empty, "Fixture reader", StableId: id, IdentityKind: DiscDriveIdentityKind.StorageSerialNumber);

    private static void PumpDriveFixtureUntil(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!completed() && DateTime.UtcNow < deadline)
        {
            DrainDispatcher();
            Thread.Sleep(1);
        }
        Assert.True(completed(), "Controlled drive refresh did not reach its expected state.");
    }
}
