using BookStitch.Models;
using System.ComponentModel;

namespace BookStitch.Services;

public enum DeveloperToolsVisibilityChoice { Show, Hide, Conceal }

/// <summary>Owns the environment default, persistent override and deliberate discovery of developer access.</summary>
public sealed class DeveloperToolsAccessState : INotifyPropertyChanged
{
    private AppSettings _settings = new();
    private BookStitchEnvironment _environment = BookStitchEnvironment.Production;
    private Func<bool> _save = () => false;
    private int _discoveryClicks;

    public event PropertyChangedEventHandler? PropertyChanged;

    // IsVisible controls wrench affordances, never the Developer Tools window.

    public bool IsEnabled => IsEnabledFor(_settings, _environment);

    public static bool IsEnabledFor(AppSettings settings, BookStitchEnvironment environment) =>
        settings.DeveloperToolsSelectorUnlocked || (settings.DeveloperToolsEnabled ?? (settings.DeveloperToolsVisible == true || environment != BookStitchEnvironment.Production));

    public bool IsVisible
    {
        get => IsEnabled && IsVisibleFor(_settings, _environment);
        set
        {
            if (!IsSelectorAvailable) return;
            var previous = _settings.DeveloperToolsVisible;
            var previousEnabled = _settings.DeveloperToolsEnabled;
            var wasEnabled = IsEnabled;
            _settings.DeveloperToolsVisible = value;
            _settings.DeveloperToolsEnabled = value || wasEnabled;
            if (!TrySave())
            {
                _settings.DeveloperToolsVisible = previous;
                _settings.DeveloperToolsEnabled = previousEnabled;
                Notify();
                return;
            }
            Notify();
        }
    }

    public static bool IsVisibleFor(AppSettings settings, BookStitchEnvironment environment) =>
        IsEnabledFor(settings, environment) && (settings.DeveloperToolsVisible ?? environment != BookStitchEnvironment.Production);

    public bool IsSelectorAvailable => _settings.DeveloperToolsSelectorUnlocked || IsEnabled;

    public bool CanConceal => true;

    // Conceal is an action, never a persisted selection value.
    public DeveloperToolsVisibilityChoice VisibilityChoice
    {
        get => IsVisible ? DeveloperToolsVisibilityChoice.Show : DeveloperToolsVisibilityChoice.Hide;
        set
        {
            if (value != DeveloperToolsVisibilityChoice.Conceal)
            {
                IsVisible = value == DeveloperToolsVisibilityChoice.Show;
                return;
            }
            var visible = _settings.DeveloperToolsVisible;
            var enabled = _settings.DeveloperToolsEnabled;
            var unlocked = _settings.DeveloperToolsSelectorUnlocked;
            _settings.DeveloperToolsVisible = false;
            _settings.DeveloperToolsEnabled = false;
            _settings.DeveloperToolsSelectorUnlocked = false;
            if (TrySave()) _discoveryClicks = 0;
            else
            {
                _settings.DeveloperToolsVisible = visible;
                _settings.DeveloperToolsEnabled = enabled;
                _settings.DeveloperToolsSelectorUnlocked = unlocked;
                Notify();
                return;
            }
            Notify();
        }
    }

    public void Initialize(AppSettings settings, BookStitchEnvironment environment, Func<bool> save)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(save);
        _settings = settings;
        _environment = environment;
        _save = save;
        _discoveryClicks = 0;
        Notify();
    }

    public void RegisterDiscoveryClick()
    {
        if (IsSelectorAvailable || ++_discoveryClicks < 10)
            return;
        var enabled = _settings.DeveloperToolsEnabled;
        var visible = _settings.DeveloperToolsVisible;
        _settings.DeveloperToolsSelectorUnlocked = true;
        _settings.DeveloperToolsEnabled = true;
        _settings.DeveloperToolsVisible = false;
        if (!TrySave())
        {
            _settings.DeveloperToolsSelectorUnlocked = false;
            _settings.DeveloperToolsEnabled = enabled;
            _settings.DeveloperToolsVisible = visible;
        }
        Notify();
    }

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectorAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibilityChoice)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConceal)));
    }

    private bool TrySave()
    {
        try { return _save(); }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Developer tools preference could not be saved: {0}", error);
            return false;
        }
    }
}
