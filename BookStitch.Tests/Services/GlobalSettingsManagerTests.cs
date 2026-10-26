using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class GlobalSettingsManagerTests
{
    [Fact]
    public void Genre_views_observe_one_setting_immediately_and_group_switches_cannot_disable_both()
    {
        var settings = new AppSettings { DefaultGenre = "Unverändert" };
        using var manager = CreateManager(() => settings, _ => { });
        var seen = new List<(bool Tags, bool Categories, bool Private, bool English)>();
        manager.GenreOptionsChanged += (_, _) => seen.Add((settings.IncludeGenreLibraryTags,
            settings.IncludeGenreCategories, settings.UsePrivateGenreList, settings.GenreListAlwaysEnglish));
        manager.SetGenreGroups(libraryTags: false);
        Assert.Equal((false, true, false, false), seen.Last());
        manager.SetGenreGroups(categories: false);
        Assert.Equal((true, false, false, false), seen.Last());
        manager.SetPrivateGenres(true);
        Assert.Equal((true, false, true, false), seen.Last());
        manager.SetGenreListAlwaysEnglish(true);
        Assert.Equal((true, false, true, true), seen.Last());
        manager.SetPrivateGenres(false);
        Assert.False(settings.UsePrivateGenreList);
        Assert.Equal("Unverändert", settings.DefaultGenre);
    }

    [Fact]
    public void DriveCircle_notifies_all_views_synchronously_before_persisting_and_ignores_same_value()
    {
        var order = new List<string>();
        using var manager = CreateManager(() => new AppSettings(), _ => order.Add("save"));
        var views = new bool[3];
        for (var i = 0; i < views.Length; i++)
        {
            var index = i;
            manager.DriveCircleChanged += (_, _) =>
            {
                views[index] = manager.DriveCircleEnabled;
                order.Add($"view{index}");
            };
        }
        manager.DriveCircleEnabled = true;
        Assert.All(views, value => Assert.True(value));
        Assert.Equal(new[] { "view0", "view1", "view2", "save" }, order);
        order.Clear();
        manager.DriveCircleEnabled = true;
        Assert.Empty(order);
        manager.DriveCircleEnabled = false;
        Assert.All(views, value => Assert.False(value));
        Assert.Equal(new[] { "view0", "view1", "view2", "save" }, order);
    }

    [Fact]
    public void DriveCircle_toggle_uses_and_persists_the_same_global_instance()
    {
        var settings = new AppSettings();
        var writes = new List<bool>();
        using var manager = CreateManager(() => settings, current => writes.Add(current.ExperimentalDriveRoundEnabled));
        manager.DriveCircleEnabled = true;
        Assert.True(settings.ExperimentalDriveRoundEnabled);
        manager.DriveCircleEnabled = false;
        Assert.False(settings.ExperimentalDriveRoundEnabled);
        Assert.Equal(new[] { true, false }, writes);
    }

    [Fact]
    public void Current_LoadsOnceAndImmediateSavePersistsTheCanonicalInstance()
    {
        var loadCount = 0;
        var settings = new AppSettings();
        AppSettings? savedSettings = null;
        using var manager = CreateManager(
            () =>
            {
                loadCount++;
                return settings;
            },
            current => savedSettings = current);

        var first = manager.Current;
        var second = manager.Current;

        Assert.Same(settings, first);
        Assert.Same(first, second);
        Assert.Equal(1, loadCount);

        Assert.True(manager.SaveImmediate());
        Assert.Same(settings, savedSettings);
        Assert.False(manager.HasPendingSave);
    }

    [Fact]
    public void DebouncedSave_CoalescesChangesAndFlushPersistsLatestStateOnce()
    {
        var scheduler = new ManualSaveScheduler();
        var settings = new AppSettings();
        var savedGenres = new List<string>();
        using var manager = CreateManager(
            () => settings,
            current => savedGenres.Add(current.DefaultGenre),
            scheduler);

        manager.Current.DefaultGenre = "First";
        manager.SaveDebounced();
        manager.Current.DefaultGenre = "Final";
        manager.SaveDebounced();

        Assert.True(manager.HasPendingSave);
        Assert.Empty(savedGenres);
        Assert.True(manager.Flush());
        Assert.Equal(["Final"], savedGenres);
        Assert.False(manager.HasPendingSave);

        scheduler.RunPending();
        Assert.Single(savedGenres);
    }

    [Fact]
    public void ExpectedSaveFailure_RemainsPendingNotifiesOnceAndCanBeRetried()
    {
        var settings = new AppSettings();
        var saveAttempts = 0;
        var failureNotifications = 0;
        using var manager = CreateManager(
            () => settings,
            _ =>
            {
                saveAttempts++;
                if (saveAttempts < 3)
                    throw new IOException("temporarily unavailable");
                if (saveAttempts == 4)
                    throw new InvalidOperationException("unexpected failure");
            });
        manager.SaveFailed += (_, _) => failureNotifications++;

        Assert.False(manager.SaveImmediate());
        Assert.True(manager.HasPendingSave);
        Assert.IsType<IOException>(manager.LastSaveError);
        Assert.Equal(1, failureNotifications);

        Assert.False(manager.Flush());
        Assert.Equal(1, failureNotifications);
        Assert.True(manager.Flush());
        Assert.False(manager.HasPendingSave);
        Assert.Null(manager.LastSaveError);
        Assert.Equal(3, saveAttempts);

        Assert.Throws<InvalidOperationException>(() => manager.SaveImmediate());
        Assert.True(manager.HasPendingSave);
        Assert.Equal(1, failureNotifications);
    }

    private static GlobalSettingsManager CreateManager(
        Func<AppSettings> load,
        Action<AppSettings> save,
        ManualSaveScheduler? scheduler = null) =>
        new(load, save, scheduler ?? new ManualSaveScheduler(), TimeSpan.FromMilliseconds(350));

    private sealed class ManualSaveScheduler : IGlobalSettingsSaveScheduler
    {
        private Action? _pending;

        public void Schedule(TimeSpan delay, Action callback)
        {
            Assert.True(delay > TimeSpan.Zero);
            _pending = callback;
        }

        public void Cancel() => _pending = null;

        public void Dispose() => Cancel();

        public void RunPending()
        {
            var callback = _pending;
            _pending = null;
            callback?.Invoke();
        }
    }
}
