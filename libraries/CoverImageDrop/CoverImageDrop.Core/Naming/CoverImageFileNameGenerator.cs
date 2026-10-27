using System.Globalization;
using System.IO;
using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Naming;

public sealed class CoverImageFileNameGenerator
{
    public const int MaxFileNameCharacters = 255;

    private static readonly HashSet<string> KnownImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".bmp",
        ".tif",
        ".tiff",
        ".ico"
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "CLOCK$",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private static readonly HashSet<char> InvalidWindowsFileNameCharacters = new()
    {
        '<', '>', ':', '"', '/', '\\', '|', '?', '*'
    };

    private readonly TimeProvider _timeProvider;

    public CoverImageFileNameGenerator(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CoverImageNamingResult Generate(
        string resultExtension,
        CoverImageNamingOptions? options = null)
    {
        options ??= new CoverImageNamingOptions();
        var extension = NormalizeExtension(resultExtension);
        var reasons = new List<string>();

        string? requestedName = options.Strategy switch
        {
            CoverImageNamingStrategy.Timestamp => null,
            CoverImageNamingStrategy.HostProvidedName => options.HostProvidedName,
            CoverImageNamingStrategy.OriginalFileName => options.OriginalFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Strategy, "Unknown naming strategy.")
        };

        string baseName;
        if (options.Strategy == CoverImageNamingStrategy.Timestamp)
        {
            baseName = CreateTimestampBaseName();
        }
        else if (string.IsNullOrWhiteSpace(requestedName))
        {
            reasons.Add("MissingRequestedNameReplacedWithTimestamp");
            baseName = CreateTimestampBaseName();
        }
        else
        {
            baseName = PrepareRequestedBaseName(requestedName, reasons);
            if (string.IsNullOrEmpty(baseName))
            {
                reasons.Add("EmptyNameReplacedWithTimestamp");
                baseName = CreateTimestampBaseName();
            }
        }

        baseName = EnforceFileNameComponentLimit(baseName, extension, reasons);
        var fileName = baseName + extension;
        IReadOnlyList<CoverImageIssue> issues = reasons.Count == 0
            ? Array.Empty<CoverImageIssue>()
            : new[]
            {
                new CoverImageIssue(
                    CoverImageIssueCode.FileNameAdjusted,
                    CoverImageIssueSeverity.Info,
                    new Dictionary<string, object?>
                    {
                        ["Strategy"] = options.Strategy.ToString(),
                        ["RequestedName"] = requestedName,
                        ["FinalFileName"] = fileName,
                        ["Reasons"] = reasons.ToArray()
                    })
            };

        return new CoverImageNamingResult(fileName, baseName, extension, issues);
    }

    private string CreateTimestampBaseName()
    {
        return _timeProvider
            .GetLocalNow()
            .ToString("yyyy.MM.dd HH.mm.ss.fff", CultureInfo.InvariantCulture);
    }

    private static string PrepareRequestedBaseName(string requestedName, List<string> reasons)
    {
        var candidate = requestedName;
        var trimmed = candidate.TrimEnd(' ', '.');
        if (!string.Equals(trimmed, candidate, StringComparison.Ordinal))
        {
            reasons.Add("TrailingDotsOrSpacesRemoved");
            candidate = trimmed;
        }

        var extension = Path.GetExtension(candidate);
        if (!string.IsNullOrEmpty(extension) && KnownImageExtensions.Contains(extension))
        {
            candidate = candidate[..^extension.Length];
            reasons.Add("KnownImageExtensionRemoved");
        }

        var chars = candidate.ToCharArray();
        var replacedInvalidCharacter = false;
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] < 32 || InvalidWindowsFileNameCharacters.Contains(chars[index]))
            {
                chars[index] = '_';
                replacedInvalidCharacter = true;
            }
        }

        candidate = new string(chars);
        if (replacedInvalidCharacter)
        {
            reasons.Add("InvalidCharactersReplaced");
        }

        var trimmedAfterReplacement = candidate.TrimEnd(' ', '.');
        if (!string.Equals(trimmedAfterReplacement, candidate, StringComparison.Ordinal))
        {
            reasons.Add("TrailingDotsOrSpacesRemoved");
            candidate = trimmedAfterReplacement;
        }

        if (candidate.Length > 0 && IsReservedDeviceName(candidate))
        {
            candidate = "_" + candidate;
            reasons.Add("ReservedDeviceNameAdjusted");
        }

        return candidate;
    }

    private static string EnforceFileNameComponentLimit(
        string baseName,
        string extension,
        List<string> reasons)
    {
        var maxBaseNameLength = MaxFileNameCharacters - extension.Length;
        if (maxBaseNameLength <= 0)
        {
            throw new ArgumentException(
                $"Result extension leaves no room within the Windows file-name component limit of {MaxFileNameCharacters} characters.",
                nameof(extension));
        }

        if (baseName.Length <= maxBaseNameLength)
        {
            return baseName;
        }

        var truncated = baseName[..maxBaseNameLength].TrimEnd(' ', '.');
        if (truncated.Length == 0)
        {
            throw new ArgumentException(
                "Requested file name cannot be reduced to a valid Windows file name.",
                nameof(baseName));
        }

        reasons.Add("NameTruncatedToWindowsComponentLimit");
        return truncated;
    }

    private static bool IsReservedDeviceName(string candidate)
    {
        var dotIndex = candidate.IndexOf('.');
        var firstSegment = dotIndex >= 0 ? candidate[..dotIndex] : candidate;
        return ReservedDeviceNames.Contains(firstSegment);
    }

    private static string NormalizeExtension(string resultExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resultExtension);

        var extension = resultExtension.Trim();
        while (extension.StartsWith(".", StringComparison.Ordinal))
        {
            extension = extension[1..];
        }

        if (extension.Length == 0 || extension.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException(
                "Result extension must contain only ASCII letters or digits.",
                nameof(resultExtension));
        }

        return "." + extension.ToLowerInvariant();
    }
}
