using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BookStitch.Presentation;

public sealed class DeveloperImageChoice : INotifyPropertyChanged
{
    private bool _matches = true;
    private readonly string _label;
    public string Id { get; }
    public AppearanceMode? Theme { get; }
    public string DisplayName => _matches ? _label : $"{_label} ({LocalizationText.Get("Developer.Images.Mismatch")})";
    public double Opacity => _matches ? 1 : 0.5;
    public event PropertyChangedEventHandler? PropertyChanged;
    public DeveloperImageChoice(string id, string label, AppearanceMode? theme) => (Id, _label, Theme) = (id, label, theme);
    public void Refresh(AppearanceMode mode)
    {
        _matches = Theme is null || Theme == mode;
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new(nameof(Opacity)));
    }
}

/// <summary>One generic selector for any registered image role; all state belongs to the session owner.</summary>
public sealed class DeveloperImagePreviewRow : INotifyPropertyChanged
{
    private readonly DeveloperAppearancePreview _preview;
    public AppearanceAssetRole Role { get; }
    public string Label => LocalizationText.Get($"Developer.Images.Role.{Role}");
    public ObservableCollection<DeveloperImageChoice> Choices { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public string SelectedId
    {
        get => _preview.GetImageId(Role) ?? "";
        set { if (value is not null && value != SelectedId) _preview.SetImage(Role, value); }
    }
    public DeveloperImagePreviewRow(AppearanceAssetRole role, DeveloperAppearancePreview preview)
    {
        (Role, _preview) = (role, preview);
        Choices.Add(new("", LocalizationText.Get("Developer.Images.Auto"), null));
        foreach (var variant in AppearanceAssetCatalog.Variants.Where(variant => variant.Role == role))
        {
            var family = variant.Theme switch
            {
                AppearanceMode.Dark => LocalizationText.Get("Developer.Images.Dark"),
                AppearanceMode.Light => LocalizationText.Get("Developer.Images.Ivory"),
                _ => LocalizationText.Get("Developer.Images.Universal")
            };
            var label = variant.DesignName is not null ? $"{variant.DesignName} · {family}" : variant.LabelKey is null ? family : $"{LocalizationText.Get(variant.LabelKey)} · {family}";
            Choices.Add(new(variant.Id, label, variant.Theme));
        }
    }
    public void Refresh(AppearanceMode mode)
    {
        foreach (var choice in Choices) choice.Refresh(mode);
        PropertyChanged?.Invoke(this, new(nameof(SelectedId)));
    }
}
