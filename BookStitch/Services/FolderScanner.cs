using System.IO;
using System.Text.RegularExpressions;
using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;

namespace BookStitch.Services;

public enum TrackNumberPreference
{
    EmbeddedTag,
    FileName
}

public sealed class FolderScanner
{
    private static readonly Regex LeadingNumberTrackRegex = new(
        @"^\s*(?<number>\d{1,4})(?<suffix>[a-zA-Z]?)(?=$|[\s._\-\)])",
        RegexOptions.Compiled);

    private readonly NaturalStringComparer _naturalComparer = new();
    private readonly TagMetadataReader _tagMetadataReader;
    private readonly FileSystemMetadataReader _fileSystemMetadataReader;
    private readonly TrackAutomaticOrderApplicationService _automaticOrderApplicationService;

    public FolderScanner()
        : this(new TagMetadataReader(), new FileSystemMetadataReader())
    {
    }

    public FolderScanner(
        TagMetadataReader tagMetadataReader,
        FileSystemMetadataReader fileSystemMetadataReader)
    {
        _tagMetadataReader = tagMetadataReader;
        _fileSystemMetadataReader = fileSystemMetadataReader;
        _automaticOrderApplicationService = new TrackAutomaticOrderApplicationService();
    }

    public List<TrackInfo> Scan(
        string rootFolder,
        TrackNumberPreference trackNumberPreference = TrackNumberPreference.EmbeddedTag,
        Func<string, bool>? includeFile = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootFolder))
            return [];

        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<TrackInfo>();
        foreach (var file in SafeFileSystemEnumeration.EnumerateFiles(rootFolder, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AudioInputFormatCatalog.IsLocalImportExtension(Path.GetExtension(file)) ||
                !(includeFile?.Invoke(file) ?? true)) continue;
            try { files.Add(CreateTrackInfo(rootFolder, file, trackNumberPreference)); }
            catch (Exception ex) when (SafeFileSystemEnumeration.IsFileSystemError(ex))
            {
                System.Diagnostics.Trace.TraceWarning("Track scan skipped {0}: {1}", file, ex.Message);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();

        _automaticOrderApplicationService.CalculateAndApply(files, TrackOrderProjectType.LocalFolder);
        files = files
            .OrderBy(track => track.AutomaticOrderPosition ?? int.MaxValue)
            .ThenBy(track => track.RelativeFolder, _naturalComparer)
            .ThenBy(track => track.FileName, _naturalComparer)
            .ToList();

        for (var i = 0; i < files.Count; i++)
            files[i].Index = i + 1;

        AddWarnings(files);

        return files;
    }

    public Task<List<TrackInfo>> ScanAsync(
        string rootFolder,
        TrackNumberPreference trackNumberPreference = TrackNumberPreference.EmbeddedTag,
        Func<string, bool>? includeFile = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(rootFolder, trackNumberPreference, includeFile, cancellationToken), cancellationToken);

    private TrackInfo CreateTrackInfo(
        string rootFolder,
        string filePath,
        TrackNumberPreference trackNumberPreference)
    {
        var fileSystem = _fileSystemMetadataReader.Read(rootFolder, filePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileSystem.FullPath);
        var projectRelativeFolder = BuildProjectRelativeFolder(rootFolder, fileSystem.RelativeFolder);

        var discNumberFromFolder = TryExtractDiscNumber(fileSystem.RelativeFolder);
        var trackNumberFromFile = TryExtractTrackNumber(fileNameWithoutExtension);

        var tagMetadata = _tagMetadataReader.Read(filePath);
        var originalTags = tagMetadata.Original;
        var tagTitle = originalTags.Title;
        var artist = originalTags.Authors.FirstOrDefault()
                     ?? originalTags.Performers.FirstOrDefault()
                     ?? "";
        var tagDiscNumber = ToPositiveInt(originalTags.DiscNumber);
        var tagDiscTotal = ToPositiveInt(originalTags.DiscTotal);
        var tagTrackNumber = ToPositiveInt(originalTags.TrackNumber);
        var tagTrackTotal = ToPositiveInt(originalTags.TrackTotal);
        var duration = tagMetadata.TechnicalDurationFallback is { } fallbackDuration
            ? FormatDuration(fallbackDuration)
            : "";
        var durationTicks = tagMetadata.TechnicalDurationFallback?.Ticks;
        var bitrate = tagMetadata.TechnicalBitrateKbpsFallback;
        var channels = tagMetadata.TechnicalChannelsFallback;

        var finalDiscNumber = tagDiscNumber ?? discNumberFromFolder;
        var finalTrackNumber = trackNumberPreference == TrackNumberPreference.FileName
            ? trackNumberFromFile ?? tagTrackNumber
            : tagTrackNumber ?? trackNumberFromFile;

        var result = new TrackInfo
        {
            FilePath = fileSystem.FullPath,
            FileName = fileSystem.FileName,
            RelativeFolder = fileSystem.RelativeFolder,
            ProjectRelativeFolder = projectRelativeFolder,
            Extension = fileSystem.Extension.TrimStart('.').ToUpperInvariant(),
            SourceDiscNumber = tagDiscNumber,
            SourceDiscTotal = tagDiscTotal,
            SourceTrackNumber = tagTrackNumber,
            SourceTrackTotal = tagTrackTotal,
            DiscNumber = finalDiscNumber,
            TrackNumber = finalTrackNumber,
            TagTitle = tagTitle,
            Artist = artist,
            SourceMetadataFields = SourceMetadataCaptureService.BuildCanonicalFields(originalTags),
            ChapterTitle = string.Empty,
            Duration = duration,
            DurationTicks = durationTicks,
            BitrateKbps = bitrate,
            SampleRateHz = tagMetadata.TechnicalSampleRateHzFallback,
            Channels = channels,
            SizeMb = Math.Round(fileSystem.SizeBytes / 1024d / 1024d, 2)
        };

        if (tagMetadata.Failed)
            result.SetWarning(TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags), TrackWarningCodes.UnreadableTags);

        return result;
    }

    private static int? ToPositiveInt(uint value) =>
        value is > 0 and <= int.MaxValue ? (int)value : null;

    private static void AddWarnings(List<TrackInfo> tracks)
    {
        if (tracks.Count == 0)
            return;

        foreach (var track in tracks)
        {
            var warnings = new List<string>();
            var codes = track.WarningCodes;

            if (!string.IsNullOrWhiteSpace(track.Warning))
                warnings.Add(track.Warning);

            if (track.TrackNumber is null)
                warnings.Add(LocalizationText.Get("WarningMissingTrackNumber"));

            track.SetWarning(string.Join("; ", warnings.Distinct(StringComparer.OrdinalIgnoreCase)), codes);
        }

        ApplyMissingTagTitleWarnings(tracks);

        AddDuplicateTrackNumberWarnings(tracks);
        AddMissingTrackNumberWarnings(tracks);
    }

    internal static void ApplyMissingTagTitleWarnings(IReadOnlyList<TrackInfo> tracks)
    {
        var warningText = TrackWarningPresentation.Format(TrackWarningCodes.MissingTagTitle);

        foreach (var group in tracks.GroupBy(track => track.DiscNumber))
        {
            var warnMissingTitles = group.Any(track => !string.IsNullOrWhiteSpace(track.TagTitle));
            foreach (var track in group)
            {
                var warnings = track.Warning
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(warning => !string.Equals(warning, warningText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var codes = track.WarningCodes & ~TrackWarningCodes.MissingTagTitle;

                if (warnMissingTitles && string.IsNullOrWhiteSpace(track.TagTitle))
                {
                    warnings.Add(warningText);
                    codes |= TrackWarningCodes.MissingTagTitle;
                }

                track.SetWarning(string.Join("; ", warnings.Distinct(StringComparer.OrdinalIgnoreCase)), codes);
            }
        }
    }

    private static void AddDuplicateTrackNumberWarnings(List<TrackInfo> tracks)
    {
        var duplicateTracks = tracks
            .Where(track => track.TrackNumber is not null)
            .GroupBy(track => new
            {
                Disc = track.DiscNumber,
                Track = track.TrackNumber
            })
            .Where(group => group.Count() > 1);

        foreach (var group in duplicateTracks)
        {
            var suffixes = group
                .Select(track => GetTrackNumberSuffix(Path.GetFileNameWithoutExtension(track.FileName)))
                .ToList();

            var allHaveDistinctSuffixes = suffixes.All(suffix => !string.IsNullOrWhiteSpace(suffix))
                                          && suffixes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == suffixes.Count;

            if (allHaveDistinctSuffixes)
                continue;

            var warning = group.Key.Disc is null
                ? LocalizationText.Get("WarningDuplicateTrackNumber")
                : string.Format(LocalizationText.Get("WarningDuplicateTrackNumberDisc"), group.Key.Disc);

            foreach (var track in group)
                AppendWarning(track, warning);
        }
    }

    private static void AddMissingTrackNumberWarnings(List<TrackInfo> tracks)
    {
        var groups = tracks
            .Where(track => track.TrackNumber is not null)
            .GroupBy(track => track.DiscNumber);

        foreach (var group in groups)
        {
            var numbers = group
                .Select(track => track.TrackNumber!.Value)
                .Distinct()
                .Order()
                .ToList();

            if (numbers.Count < 3)
                continue;

            var min = numbers.First();
            var max = numbers.Last();

            // Avoid noisy warnings for partial selections such as Track 20-23 only.
            if (min > 2)
                continue;

            var missing = Enumerable
                .Range(min, max - min + 1)
                .Except(numbers)
                .Take(5)
                .ToList();

            if (missing.Count == 0)
                continue;

            var missingText = string.Join(", ", missing.Select(number => number.ToString("000")));
            var warning = string.Format(LocalizationText.Get(missing.Count == 1
                ? "WarningSingleTrackGap" : "WarningTrackGapRange"), missingText);

            var firstTrack = group.OrderBy(track => track.Index).FirstOrDefault();
            if (firstTrack is not null)
                AppendWarning(firstTrack, warning);
        }
    }

    private static void AppendWarning(TrackInfo track, string warning)
    {
        var codes = track.WarningCodes;
        if (string.IsNullOrWhiteSpace(track.Warning))
            track.SetWarning(warning, codes);
        else if (!track.Warning.Contains(warning, StringComparison.OrdinalIgnoreCase))
            track.SetWarning(track.Warning + "; " + warning, codes);
    }

    private static int? TryExtractDiscNumber(string relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder))
            return null;

        var folderParts = relativeFolder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var folder in folderParts.Reverse())
        {
            var match = ChapterMarkerCatalog.ExplicitDiscNumberRegex.Match(folder);

            if (match.Success && int.TryParse(match.Groups["number"].Value, out var number))
                return number;
        }

        return null;
    }

    private static string BuildProjectRelativeFolder(string rootFolder, string relativeFolder)
    {
        var rootName = Path.GetFileName(
            Path.GetFullPath(rootFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
            rootName = rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.IsNullOrWhiteSpace(relativeFolder) || relativeFolder == "."
            ? rootName
            : Path.Combine(rootName, relativeFolder);
    }

    private static int? TryExtractTrackNumber(string fileNameWithoutExtension)
    {
        var match = ChapterMarkerCatalog.LeadingExplicitTrackNumberRegex.Match(fileNameWithoutExtension);

        if (!match.Success)
            match = LeadingNumberTrackRegex.Match(fileNameWithoutExtension);

        if (match.Success && int.TryParse(match.Groups["number"].Value, out var number))
            return number;

        return null;
    }

    private static string GetTrackNumberSuffix(string fileNameWithoutExtension)
    {
        var match = ChapterMarkerCatalog.LeadingExplicitTrackNumberRegex.Match(fileNameWithoutExtension);

        if (!match.Success)
            match = LeadingNumberTrackRegex.Match(fileNameWithoutExtension);

        if (!match.Success)
            return "";

        return match.Groups["suffix"].Value.Trim();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
