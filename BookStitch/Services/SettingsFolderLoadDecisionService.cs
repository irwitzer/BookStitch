namespace BookStitch.Services;

public sealed record SettingsFolderLoadDecision(
    string Value,
    bool ShouldApply);

public static class SettingsFolderLoadDecisionService
{
    public static SettingsFolderLoadDecision CreateOutputFolderDecision(
        string? value,
        bool directoryExists)
    {
        var normalized = value ?? string.Empty;

        return new SettingsFolderLoadDecision(
            normalized,
            !string.IsNullOrWhiteSpace(normalized) &&
            directoryExists);
    }

    public static SettingsFolderLoadDecision CreateSelectedFolderDecision(
        string? value)
    {
        var normalized = value ?? string.Empty;

        return new SettingsFolderLoadDecision(
            normalized,
            !string.IsNullOrWhiteSpace(normalized));
    }
}