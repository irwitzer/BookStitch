using BookStitch.Models;

namespace BookStitch.Services;

public sealed class DeveloperModeSettingsService
{
    public void Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.ShowPipelineStateDebug || settings.ForceShowFfmpegSetupButton)
            settings.ShowDeveloperTab = true;
    }

    public void SetDeveloperMode(AppSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ShowDeveloperTab = enabled;
        if (!enabled)
        {
            settings.ShowPipelineStateDebug = false;
            settings.ForceShowFfmpegSetupButton = false;
        }
    }

    public void SetPipelineStateDebugVisible(AppSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ShowPipelineStateDebug = enabled;
        if (enabled)
            settings.ShowDeveloperTab = true;
    }

    public void SetFfmpegSetupButtonAlwaysVisible(AppSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.ForceShowFfmpegSetupButton = enabled;
        if (enabled)
            settings.ShowDeveloperTab = true;
    }
}
