using BookStitch.Models;
using System.IO;
using System.Threading;

namespace BookStitch.Services;

public sealed class GlobalSettingsSaveFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

public sealed class GlobalSettingsManager : IDisposable
{
    public static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(350);

    private readonly Func<AppSettings> _loadSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly IGlobalSettingsSaveScheduler _saveScheduler;
    private readonly TimeSpan _debounceDelay;
    private AppSettings? _current;
    private bool _saveInProgress;
    private bool _isDisposed;

    public GlobalSettingsManager(SettingsService settingsService)
        : this(
            GetLoadSettings(settingsService),
            GetSaveSettings(settingsService),
            new SynchronizationContextGlobalSettingsSaveScheduler(SynchronizationContext.Current),
            DefaultDebounceDelay)
    {
    }

    internal GlobalSettingsManager(
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        IGlobalSettingsSaveScheduler saveScheduler,
        TimeSpan debounceDelay)
    {
        _loadSettings = loadSettings ?? throw new ArgumentNullException(nameof(loadSettings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _saveScheduler = saveScheduler ?? throw new ArgumentNullException(nameof(saveScheduler));
        if (debounceDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));

        _debounceDelay = debounceDelay;
    }

    public event EventHandler<GlobalSettingsSaveFailedEventArgs>? SaveFailed;

    public event EventHandler? GenreOptionsChanged;

    public void SetGenreGroups(bool? libraryTags = null, bool? categories = null)
    {
        if (libraryTags.HasValue)
        {
            Current.IncludeGenreLibraryTags = libraryTags.Value;
            if (!libraryTags.Value && !Current.IncludeGenreCategories)
                Current.IncludeGenreCategories = true;
        }
        if (categories.HasValue)
        {
            Current.IncludeGenreCategories = categories.Value;
            if (!categories.Value && !Current.IncludeGenreLibraryTags)
                Current.IncludeGenreLibraryTags = true;
        }
        GenreOptionsChanged?.Invoke(this, EventArgs.Empty);
        SaveImmediate();
    }

    public void SetGenreListAlwaysEnglish(bool value)
    {
        if (Current.GenreListAlwaysEnglish == value) return;
        Current.GenreListAlwaysEnglish = value;
        GenreOptionsChanged?.Invoke(this, EventArgs.Empty);
        SaveImmediate();
    }

    public void SetPrivateGenres(bool value)
    {
        if (Current.UsePrivateGenreList == value) return;
        Current.UsePrivateGenreList = value;
        GenreOptionsChanged?.Invoke(this, EventArgs.Empty);
        SaveImmediate();
    }

    public event EventHandler? DriveCircleChanged;

    public bool DriveCircleEnabled
    {
        get => Current.ExperimentalDriveRoundEnabled;
        set
        {
            if (Current.ExperimentalDriveRoundEnabled == value) return;
            Current.ExperimentalDriveRoundEnabled = value;
            DriveCircleChanged?.Invoke(this, EventArgs.Empty);
            SaveImmediate();
        }
    }

    public AppSettings Current
    {
        get
        {
            ThrowIfDisposed();
            return _current ??= _loadSettings();
        }
    }

    public bool HasPendingSave { get; private set; }

    public Exception? LastSaveError { get; private set; }

    public bool SaveImmediate()
    {
        ThrowIfDisposed();
        _saveScheduler.Cancel();
        HasPendingSave = true;
        return TryPersistCurrent();
    }

    public void SaveDebounced()
    {
        ThrowIfDisposed();
        HasPendingSave = true;
        _saveScheduler.Schedule(_debounceDelay, FlushScheduledSave);
    }

    public bool Flush()
    {
        ThrowIfDisposed();
        _saveScheduler.Cancel();
        return !HasPendingSave || TryPersistCurrent();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        Flush();
        _saveScheduler.Dispose();
        _isDisposed = true;
    }

    private void FlushScheduledSave()
    {
        if (!_isDisposed)
            Flush();
    }

    private bool TryPersistCurrent()
    {
        if (_saveInProgress)
            return false;

        _saveInProgress = true;
        try
        {
            _saveSettings(Current);
            HasPendingSave = false;
            LastSaveError = null;
            return true;
        }
        catch (IOException exception)
        {
            HandleExpectedSaveFailure(exception);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            HandleExpectedSaveFailure(exception);
            return false;
        }
        finally
        {
            _saveInProgress = false;
        }
    }

    private void HandleExpectedSaveFailure(Exception exception)
    {
        var shouldNotify = LastSaveError is null;
        LastSaveError = exception;
        HasPendingSave = true;
        if (shouldNotify)
            SaveFailed?.Invoke(this, new GlobalSettingsSaveFailedEventArgs(exception));
    }

    private static Func<AppSettings> GetLoadSettings(SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        return settingsService.Load;
    }

    private static Action<AppSettings> GetSaveSettings(SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        return settingsService.Save;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}

internal interface IGlobalSettingsSaveScheduler : IDisposable
{
    void Schedule(TimeSpan delay, Action callback);

    void Cancel();
}

internal sealed class SynchronizationContextGlobalSettingsSaveScheduler(
    SynchronizationContext? synchronizationContext) : IGlobalSettingsSaveScheduler
{
    private readonly object _sync = new();
    private SynchronizationContext? _synchronizationContext = synchronizationContext;
    private Timer? _timer;
    private long _generation;
    private bool _isDisposed;

    public void Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            _synchronizationContext ??= SynchronizationContext.Current
                ?? throw new InvalidOperationException(
                    "Debounced global-settings saves must be requested from a synchronization context.");
            var generation = ++_generation;
            _timer?.Dispose();
            _timer = new Timer(
                _ => PostIfCurrent(generation, callback),
                null,
                delay,
                Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _generation++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void PostIfCurrent(long generation, Action callback)
    {
        lock (_sync)
        {
            if (_isDisposed || generation != _generation)
                return;

            _timer?.Dispose();
            _timer = null;
        }

        _synchronizationContext!.Post(_ => RunIfCurrent(generation, callback), null);
    }

    private void RunIfCurrent(long generation, Action callback)
    {
        lock (_sync)
        {
            if (_isDisposed || generation != _generation)
                return;
        }

        callback();
    }
}
