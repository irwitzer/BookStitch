using System.Globalization;
using System.IO;

namespace BookStitch.Services;

public sealed class ProjectFolderNameService
{
    public string CreateUniqueProjectFolder(
        string projectsFolder,
        string displayName,
        string fallbackName,
        DateTime? localNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectsFolder);

        var projectsRoot = Path.GetFullPath(projectsFolder);
        Directory.CreateDirectory(projectsRoot);

        var safeName = MakeSafeFileName(displayName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = MakeSafeFileName(fallbackName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Projekt";

        var timestamp = (localNow ?? DateTime.Now)
            .ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture);
        var baseFolderName = $"{safeName}_{timestamp}";

        var candidate = Path.Combine(projectsRoot, baseFolderName);
        if (!Directory.Exists(candidate))
            return candidate;

        for (var suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(projectsRoot, $"{baseFolderName}_{suffix}");
            if (!Directory.Exists(candidate))
                return candidate;
        }
    }

    public static string MakeSafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Trim()
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray());

        return string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
