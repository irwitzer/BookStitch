using System.IO;

namespace BookStitch.Services;

public static class WindowsOutputPathService
{
    // Keep output compatible with Windows tools that still use MAX_PATH.
    public const int MaximumPathLength = 259;
    public const int MaximumSegmentLength = 255;

    public static bool IsReservedName(string segment)
    {
        var stem = segment.Split('.')[0].TrimEnd(' ', '.');
        if (new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
            .Contains(stem, StringComparer.OrdinalIgnoreCase)) return true;
        return stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            "123456789¹²³".Contains(stem[3]);
    }

    public static string CreateConflictCandidate(string folder, string prefix, string name, string extension, string suffix = "")
    {
        var fullFolder = Path.GetFullPath(folder);
        var budget = Math.Min(MaximumSegmentLength, MaximumPathLength - fullFolder.Length - 1)
            - prefix.Length - suffix.Length - extension.Length;
        if (budget < 1) throw new IOException(LocalizationText.Format("Export.InvalidOutputPath", folder));
        var trimmed = name.Length > budget ? name[..budget].TrimEnd(' ', '.') : name;
        var candidate = Path.Combine(folder, prefix + trimmed + suffix + extension);
        if (!IsValid(candidate, allowExistingDirectory: true)) throw new IOException(LocalizationText.Format("Export.InvalidOutputPath", candidate));
        return candidate;
    }

    public static bool IsValid(string path, bool allowExistingDirectory = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            // Check raw segments before Windows canonicalization strips trailing dots/spaces.
            if (path.Split(['\\', '/']).Any(segment => segment.EndsWith(' ') ||
                segment.EndsWith('.') || segment.Length > MaximumSegmentLength)) return false;
            var fullPath = Path.GetFullPath(path);
            if (fullPath.Length > MaximumPathLength || (!allowExistingDirectory && Directory.Exists(fullPath))) return false;
            var root = Path.GetPathRoot(fullPath) ?? "";
            var segments = fullPath[root.Length..].Split(['\\', '/']);
            if (segments.Any(segment => segment.Length == 0 || segment.Length > MaximumSegmentLength ||
                segment.EndsWith(' ') || segment.EndsWith('.') || IsReservedName(segment) ||
                segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)))) return false;
            for (var parent = Path.GetDirectoryName(fullPath); !string.IsNullOrEmpty(parent);
                parent = Path.GetDirectoryName(parent))
                if (File.Exists(parent)) return false;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
