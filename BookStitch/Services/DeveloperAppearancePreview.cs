using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>Session-only overrides layered over the current regular settings; never owns persistence.</summary>
public sealed class DeveloperAppearancePreview
{
    private readonly DeveloperToolsAccessState _access;
    private readonly Dictionary<AppearanceAssetRole, string> _images = [];
    public event EventHandler? Changed;
    public string? BackgroundOverride { get; private set; }
    public string? TextColorOverride { get; private set; }

    public DeveloperAppearancePreview(DeveloperToolsAccessState access)
    {
        _access = access;
        access.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DeveloperToolsAccessState.IsEnabled)) return;
            if (!access.IsEnabled) Clear();
            else Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public AppSettings Resolve(AppSettings regular)
    {
        var effective = new AppSettings
        {
            BackgroundPreset = _access.IsEnabled ? BackgroundOverride ?? regular.BackgroundPreset :
                BackgroundPresetCatalog.NormalizeRegular(regular.BackgroundPreset, false).ToString(),
            TextColorPreset = _access.IsEnabled ? TextColorOverride ?? regular.TextColorPreset : regular.TextColorPreset
        };
        AppearanceSettingsService.Normalize(effective);
        return effective;
    }

    public void SetBackground(string? preset)
    {
        if (!_access.IsEnabled) return;
        BackgroundOverride = string.IsNullOrWhiteSpace(preset) ? null : BackgroundPresetCatalog.Normalize(preset).ToString();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetTextColor(string? preset)
    {
        if (!_access.IsEnabled) return;
        TextColorOverride = preset;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? GetImageId(AppearanceAssetRole role) => _access.IsEnabled ? _images.GetValueOrDefault(role) : null;
    public AppearanceAssetVariant? GetImage(AppearanceAssetRole role) =>
        AppearanceAssetCatalog.FindVariant(role, GetImageId(role));

    public void SetImage(AppearanceAssetRole role, string? id)
    {
        if (!_access.IsEnabled) return;
        if (AppearanceAssetCatalog.FindVariant(role, id) is null) _images.Remove(role);
        else _images[role] = id!;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyRegularSettingsChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Clear()
    {
        BackgroundOverride = null;
        TextColorOverride = null;
        _images.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
