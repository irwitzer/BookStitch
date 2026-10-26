using BookStitch.Models;
using System.Windows;

namespace BookStitch.Services;

public sealed class DebugWindowPlacementService
{
    private readonly DebugSettings _settings;
    private readonly DebugSettingsService _settingsService;

    public DebugWindowPlacementService(DebugSettings settings, DebugSettingsService settingsService)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public void Attach(Window window, DebugWindowPlacementTarget target)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (IsRememberEnabled(target))
            ApplySavedPlacement(window, target);
        else if (target == DebugWindowPlacementTarget.DeveloperTools)
            CenterDeveloperTools(window);

        window.Closing += (_, _) => SavePlacementIfEnabled(window, target);
    }

    public bool IsRememberEnabled(DebugWindowPlacementTarget target) => target switch
    {
        DebugWindowPlacementTarget.DeveloperTools => _settings.RememberDeveloperToolsWindowPlacement,
        DebugWindowPlacementTarget.GlobalSettings => _settings.RememberGlobalSettingsWindowPlacement,
        DebugWindowPlacementTarget.ProjectSetup => _settings.RememberProjectSetupDialogPlacement,
        DebugWindowPlacementTarget.MetadataDialog => _settings.RememberConceptDialogPlacement,
        DebugWindowPlacementTarget.ResumeProject => _settings.RememberResumeProjectDialogPlacement,
        _ => false
    };

    public void SetRememberEnabled(DebugWindowPlacementTarget target, bool enabled)
    {
        switch (target)
        {
            case DebugWindowPlacementTarget.DeveloperTools:
                _settings.RememberDeveloperToolsWindowPlacement = enabled;
                break;
            case DebugWindowPlacementTarget.GlobalSettings:
                _settings.RememberGlobalSettingsWindowPlacement = enabled;
                break;
            case DebugWindowPlacementTarget.ProjectSetup:
                _settings.RememberProjectSetupDialogPlacement = enabled;
                break;
            case DebugWindowPlacementTarget.MetadataDialog:
                _settings.RememberConceptDialogPlacement = enabled;
                break;
            case DebugWindowPlacementTarget.ResumeProject:
                _settings.RememberResumeProjectDialogPlacement = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }

        if (!enabled)
            ClearPlacement(target);

        _settingsService.Save(_settings);
    }

    private void ApplySavedPlacement(Window window, DebugWindowPlacementTarget target)
    {
        DebugSettingsService.Normalize(_settings);
        var placement = GetPlacement(target);

        if (placement.Width is double width)
            window.Width = ClampDimension(width, window.MinWidth, window.MaxWidth);
        if (placement.Height is double height)
            window.Height = ClampDimension(height, window.MinHeight, window.MaxHeight);

        var effectiveWidth = double.IsFinite(window.Width) && window.Width > 0 ? window.Width : Math.Max(window.MinWidth, 100);
        var effectiveHeight = double.IsFinite(window.Height) && window.Height > 0 ? window.Height : Math.Max(window.MinHeight, 100);

        if (placement.Left is double left &&
            placement.Top is double top &&
            IsVisibleOnVirtualDesktop(left, top, effectiveWidth, effectiveHeight))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
            return;
        }

        if (target == DebugWindowPlacementTarget.DeveloperTools)
            CenterDeveloperTools(window);
    }

    private void SavePlacementIfEnabled(Window window, DebugWindowPlacementTarget target)
    {
        if (!IsRememberEnabled(target))
            return;

        try
        {
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;

            var placement = new DebugWindowPlacement
            {
                Left = double.IsFinite(bounds.Left) ? bounds.Left : null,
                Top = double.IsFinite(bounds.Top) ? bounds.Top : null,
                Width = double.IsFinite(bounds.Width) && bounds.Width > 0 ? bounds.Width : null,
                Height = double.IsFinite(bounds.Height) && bounds.Height > 0 ? bounds.Height : null
            };

            SetPlacement(target, placement);
            _settingsService.Save(_settings);
        }
        catch
        {
            // Developer-only placement persistence must never block closing a window.
        }
    }

    private DebugWindowPlacement GetPlacement(DebugWindowPlacementTarget target) => target switch
    {
        DebugWindowPlacementTarget.DeveloperTools => new DebugWindowPlacement
        {
            Left = _settings.WindowLeft,
            Top = _settings.WindowTop,
            Width = _settings.WindowWidth,
            Height = _settings.WindowHeight
        },
        DebugWindowPlacementTarget.GlobalSettings => _settings.GlobalSettingsWindowPlacement,
        DebugWindowPlacementTarget.ProjectSetup => _settings.ProjectSetupDialogPlacement,
        DebugWindowPlacementTarget.MetadataDialog => _settings.ConceptDialogPlacement,
        DebugWindowPlacementTarget.ResumeProject => _settings.ResumeProjectDialogPlacement,
        _ => new DebugWindowPlacement()
    };

    private void SetPlacement(DebugWindowPlacementTarget target, DebugWindowPlacement placement)
    {
        switch (target)
        {
            case DebugWindowPlacementTarget.DeveloperTools:
                _settings.WindowLeft = placement.Left;
                _settings.WindowTop = placement.Top;
                _settings.WindowWidth = placement.Width ?? DebugSettings.DefaultWindowWidth;
                _settings.WindowHeight = placement.Height ?? DebugSettings.DefaultWindowHeight;
                break;
            case DebugWindowPlacementTarget.GlobalSettings:
                _settings.GlobalSettingsWindowPlacement = placement;
                break;
            case DebugWindowPlacementTarget.ProjectSetup:
                _settings.ProjectSetupDialogPlacement = placement;
                break;
            case DebugWindowPlacementTarget.MetadataDialog:
                _settings.ConceptDialogPlacement = placement;
                break;
            case DebugWindowPlacementTarget.ResumeProject:
                _settings.ResumeProjectDialogPlacement = placement;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    private void ClearPlacement(DebugWindowPlacementTarget target)
    {
        if (target == DebugWindowPlacementTarget.DeveloperTools)
        {
            _settings.WindowLeft = null;
            _settings.WindowTop = null;
            _settings.WindowWidth = DebugSettings.DefaultWindowWidth;
            _settings.WindowHeight = DebugSettings.DefaultWindowHeight;
            return;
        }

        SetPlacement(target, new DebugWindowPlacement());
    }

    private static double ClampDimension(double value, double minimum, double maximum)
    {
        var min = double.IsFinite(minimum) && minimum > 0 ? minimum : 100;
        var max = double.IsFinite(maximum) && maximum >= min ? Math.Min(maximum, 5000) : 5000;
        return Math.Clamp(value, min, max);
    }

    private static void CenterDeveloperTools(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        window.Left = workArea.Left + Math.Max(0, (workArea.Width - window.Width) / 2);
        window.Top = workArea.Top + Math.Max(0, (workArea.Height - window.Height) / 2);
    }

    private static bool IsVisibleOnVirtualDesktop(double left, double top, double width, double height)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var right = left + width;
        var bottom = top + height;

        var visibleWidth = Math.Min(right, virtualRight) - Math.Max(left, virtualLeft);
        var visibleHeight = Math.Min(bottom, virtualBottom) - Math.Max(top, virtualTop);
        return visibleWidth >= 80 && visibleHeight >= 80;
    }
}
