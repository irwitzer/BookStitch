namespace BookStitch.Services;

public sealed record ExportSettingChange(
    string Value,
    bool HasChanged);

public static class ExportSettingsNormalizationService
{
    public static string NormalizeOutputExtension(string? value) =>
        value is ".m4a" or ".m4b"
            ? value
            : ".m4a";

    public static string NormalizeFileNameTemplate(string? value) =>
        value?.Trim() ?? string.Empty;

    public static string NormalizePreset(
        string value,
        IReadOnlyCollection<string> availablePresets,
        string defaultPreset) =>
        availablePresets.Contains(value)
            ? value
            : defaultPreset;

    public static ExportSettingChange CreatePresetChange(
        string currentValue,
        string incomingValue,
        IReadOnlyCollection<string> availablePresets,
        string defaultPreset)
    {
        var normalized = NormalizePreset(
            incomingValue,
            availablePresets,
            defaultPreset);

        return CreateChange(currentValue, normalized);
    }

    public static ExportSettingChange CreateOutputExtensionChange(
        string currentValue,
        string? incomingValue)
    {
        var normalized = NormalizeOutputExtension(incomingValue);
        return CreateChange(currentValue, normalized);
    }

    public static ExportSettingChange CreateFileNameTemplateChange(
        string currentValue,
        string? incomingValue)
    {
        var normalized = NormalizeFileNameTemplate(incomingValue);
        return CreateChange(currentValue, normalized);
    }

    private static ExportSettingChange CreateChange(
        string currentValue,
        string normalizedValue) =>
        new(
            normalizedValue,
            !string.Equals(
                currentValue,
                normalizedValue,
                StringComparison.Ordinal));
}
