namespace BookStitch.Services;

public static class MetadataPanelTogglePresentationService
{
    public static MetadataPanelTogglePresentation Create(bool isExpanded) =>
        isExpanded
            ? new MetadataPanelTogglePresentation(
                Glyph: UiSymbolResources.MetadataPanelExpanded,
                IsExpanded: true)
            : new MetadataPanelTogglePresentation(
                Glyph: UiSymbolResources.MetadataPanelCollapsed,
                IsExpanded: false);
}

public sealed record MetadataPanelTogglePresentation(
    string Glyph,
    bool IsExpanded);