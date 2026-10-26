namespace BookStitch.Services;

public enum AudioInputFormatSupportLevel
{
    Standard,
    Legacy
}

public sealed record AudioInputFormat(
    string Extension,
    string DisplayName,
    AudioInputFormatSupportLevel SupportLevel,
    bool AllowLocalImport,
    bool AllowDataDiscImport);

public static class AudioInputFormatCatalog
{
    private static readonly AudioInputFormat[] Formats =
    [
        new(".mp3", "MP3", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".m4a", "M4A", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".m4b", "M4B", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".aac", "AAC", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".wav", "WAV", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".flac", "FLAC", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".wma", "WMA", AudioInputFormatSupportLevel.Legacy, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".ogg", "OGG", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".oga", "OGA", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true),
        new(".opus", "Opus", AudioInputFormatSupportLevel.Standard, AllowLocalImport: true, AllowDataDiscImport: true)
    ];

    public static IReadOnlyList<AudioInputFormat> All { get; } = Array.AsReadOnly(Formats);

    public static IReadOnlyList<string> LocalImportExtensions { get; } = Array.AsReadOnly(
        Formats
            .Where(format => format.AllowLocalImport)
            .Select(format => format.Extension)
            .ToArray());

    public static IReadOnlyList<string> DataDiscImportExtensions { get; } = Array.AsReadOnly(
        Formats
            .Where(format => format.AllowDataDiscImport)
            .Select(format => format.Extension)
            .ToArray());

    public static string OpenFileDialogFilter { get; } =
        "Audiodateien|" +
        string.Join(";", LocalImportExtensions.Select(extension => "*" + extension)) +
        "|Alle Dateien|*.*";

    public static string SupportedInputDisplayText { get; } =
        string.Join(", ", Formats.Select(format => format.DisplayName));

    public static bool IsLocalImportExtension(string? extension) =>
        IsAllowedExtension(extension, format => format.AllowLocalImport);

    public static bool IsDataDiscImportExtension(string? extension) =>
        IsAllowedExtension(extension, format => format.AllowDataDiscImport);

    private static bool IsAllowedExtension(string? extension, Func<AudioInputFormat, bool> predicate)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var normalized = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : "." + extension;

        return Formats.Any(format =>
            predicate(format) &&
            string.Equals(format.Extension, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
