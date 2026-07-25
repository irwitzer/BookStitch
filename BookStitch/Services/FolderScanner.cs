using System.IO;
using System.Text.RegularExpressions;
using BookStitch.Models;

namespace BookStitch.Services;

public enum TrackNumberPreference
{
    EmbeddedTag,
    FileName
}

public sealed class FolderScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a",
        ".m4b",
        ".aac",
        ".wav",
        ".flac",
        ".wma"
    };

    private static readonly Regex DiscRegex = new(
        @"(?i)\b(?:cd|disc|disk|teil|part|volume|vol\.?)\s*[-_ ]*(?<number>\d{1,3})\b",
        RegexOptions.Compiled);

    private static readonly Regex LeadingNumberTrackRegex = new(
        @"^\s*(?<number>\d{1,4})(?<suffix>[a-zA-Z]?)(?=$|[\s._\-\)])",
        RegexOptions.Compiled);

    private static readonly Regex NamedTrackRegex = new(
        @"(?i)^\s*(?:track|trk|kapitel|chapter|ch\.?|titel|title)\s*[-_. ]*(?<number>\d{1,4})(?<suffix>[a-zA-Z]?)(?=$|[\s._\-\)])",
        RegexOptions.Compiled);

    private static readonly Regex LeadingTrackNumberRegex = new(
        @"^\s*\d{1,4}[a-zA-Z]?\s*[-_. ]*",
        RegexOptions.Compiled);

    private static readonly Regex LeadingNamedTrackNumberRegex = new(
        @"(?i)^\s*(?:track|trk|kapitel|chapter|ch\.?|titel|title)\s*[-_. ]*\d{1,4}[a-zA-Z]?\s*[-_. ]*",
        RegexOptions.Compiled);

    private static readonly Regex MultiSpaceRegex = new(
        @"\s+",
        RegexOptions.Compiled);

    private static readonly Regex GenericChapterRegex = new(
        @"(?i)^(track|audio track|kapitel|chapter|titel)\s*\d*$",
        RegexOptions.Compiled);

    private readonly NaturalStringComparer _naturalComparer = new();

    public List<TrackInfo> Scan(
        string rootFolder,
        TrackNumberPreference trackNumberPreference = TrackNumberPreference.EmbeddedTag,
        Func<string, bool>? includeFile = null)
    {
        if (!Directory.Exists(rootFolder))
            return [];

        var files = Directory
            .EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file)))
            .Where(file => includeFile?.Invoke(file) ?? true)
            .Select(file => CreateTrackInfo(rootFolder, file, trackNumberPreference))
            .OrderBy(track => track.DiscNumber ?? int.MaxValue)
            .ThenBy(track => track.RelativeFolder, _naturalComparer)
            .ThenBy(track => track.TrackNumber ?? int.MaxValue)
            .ThenBy(track => GetTrackNumberSuffix(Path.GetFileNameWithoutExtension(track.FileName)), _naturalComparer)
            .ThenBy(track => track.FileName, _naturalComparer)
            .ToList();

        for (var i = 0; i < files.Count; i++)
            files[i].Index = i + 1;

        AddWarnings(files);

        return files;
    }

    private static TrackInfo CreateTrackInfo(
        string rootFolder,
        string filePath,
        TrackNumberPreference trackNumberPreference)
    {
        var fileInfo = new FileInfo(filePath);
        var relativePath = Path.GetRelativePath(rootFolder, filePath);
        var relativeFolder = Path.GetDirectoryName(relativePath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

        var discNumberFromFolder = TryExtractDiscNumber(relativeFolder);
        var trackNumberFromFile = TryExtractTrackNumber(fileNameWithoutExtension);

        var tagTitle = "";
        var artist = "";
        int? tagDiscNumber = null;
        int? tagTrackNumber = null;
        var duration = "";
        int? bitrate = null;
        int? channels = null;
        long? durationTicks = null;
        var tagReadFailed = false;

        try
        {
            using var tagFile = TagLib.File.Create(filePath);

            tagTitle = tagFile.Tag.Title?.Trim() ?? "";

            artist = tagFile.Tag.FirstAlbumArtist?.Trim()
                     ?? tagFile.Tag.FirstPerformer?.Trim()
                     ?? "";

            if (tagFile.Tag.Disc > 0)
                tagDiscNumber = (int)tagFile.Tag.Disc;

            if (tagFile.Tag.Track > 0)
                tagTrackNumber = (int)tagFile.Tag.Track;

            if (tagFile.Properties.Duration > TimeSpan.Zero)
            {
                duration = FormatDuration(tagFile.Properties.Duration);
                durationTicks = tagFile.Properties.Duration.Ticks;
            }

            if (tagFile.Properties.AudioBitrate > 0)
                bitrate = tagFile.Properties.AudioBitrate;

            if (tagFile.Properties.AudioChannels > 0)
                channels = tagFile.Properties.AudioChannels;
        }
        catch
        {
            tagReadFailed = true;
        }

        var finalDiscNumber = tagDiscNumber ?? discNumberFromFolder;
        var finalTrackNumber = trackNumberPreference == TrackNumberPreference.FileName
            ? trackNumberFromFile ?? tagTrackNumber
            : tagTrackNumber ?? trackNumberFromFile;

        var chapterTitle = BuildChapterTitle(tagTitle, fileNameWithoutExtension, finalTrackNumber);

        var result = new TrackInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            RelativeFolder = relativeFolder,
            Extension = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            DiscNumber = finalDiscNumber,
            TrackNumber = finalTrackNumber,
            TagTitle = tagTitle,
            Artist = artist,
            ChapterTitle = chapterTitle,
            Duration = duration,
            DurationTicks = durationTicks,
            BitrateKbps = bitrate,
            Channels = channels,
            SizeMb = Math.Round(fileInfo.Length / 1024d / 1024d, 2)
        };

        if (tagReadFailed)
            result.Warning = "Tags konnten nicht gelesen werden";

        return result;
    }

    private static void AddWarnings(List<TrackInfo> tracks)
    {
        if (tracks.Count == 0)
            return;

        foreach (var track in tracks)
        {
            var warnings = new List<string>();

            if (!string.IsNullOrWhiteSpace(track.Warning))
                warnings.Add(track.Warning);

            if (track.TrackNumber is null)
                warnings.Add("Keine Tracknummer erkannt");

            if (IsGenericChapterTitle(track.ChapterTitle))
                warnings.Add("Kapitelname wirkt generisch");

            if (string.IsNullOrWhiteSpace(track.TagTitle))
                warnings.Add("Kein Tag-Titel");

            track.Warning = string.Join("; ", warnings.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        AddDuplicateTrackNumberWarnings(tracks);
        AddMissingTrackNumberWarnings(tracks);
        AddDuplicateChapterTitleWarnings(tracks);
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
                ? "Doppelte Tracknummer"
                : $"Doppelte Tracknummer auf Disc {group.Key.Disc}";

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
            var suffix = missing.Count == 1 ? "fehlt möglicherweise" : "fehlen möglicherweise";
            var warning = $"Tracknummer {missingText} {suffix}";

            var firstTrack = group.OrderBy(track => track.Index).FirstOrDefault();
            if (firstTrack is not null)
                AppendWarning(firstTrack, warning);
        }
    }

    private static void AddDuplicateChapterTitleWarnings(List<TrackInfo> tracks)
    {
        var duplicateChapters = tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.ChapterTitle))
            .Where(track => !IsGenericChapterTitle(track.ChapterTitle))
            .GroupBy(track => new
            {
                track.DiscNumber,
                Title = NormalizeChapterTitle(track.ChapterTitle)
            })
            .Where(group => !string.IsNullOrWhiteSpace(group.Key.Title) && group.Count() > 1);

        foreach (var group in duplicateChapters)
        {
            if (IsNumberedChapterDuplicateGroup(group))
                continue;

            foreach (var track in group)
                AppendWarning(track, "Doppelter Kapitelname");
        }
    }

    private static void AppendWarning(TrackInfo track, string warning)
    {
        if (string.IsNullOrWhiteSpace(track.Warning))
            track.Warning = warning;
        else if (!track.Warning.Contains(warning, StringComparison.OrdinalIgnoreCase))
            track.Warning += "; " + warning;
    }

    private static string BuildChapterTitle(string tagTitle, string fileNameWithoutExtension, int? trackNumber)
    {
        if (!string.IsNullOrWhiteSpace(tagTitle) && !IsGenericChapterTitle(tagTitle))
            return CleanText(tagTitle);

        var cleaned = CleanFileNameTitle(fileNameWithoutExtension);

        if (string.IsNullOrWhiteSpace(cleaned))
            return trackNumber is null ? "Kapitel" : $"Kapitel {trackNumber:000}";

        return cleaned;
    }

    private static string CleanFileNameTitle(string fileNameWithoutExtension)
    {
        var cleaned = fileNameWithoutExtension.Trim();

        cleaned = LeadingNamedTrackNumberRegex.Replace(cleaned, "");
        cleaned = LeadingTrackNumberRegex.Replace(cleaned, "");
        cleaned = cleaned.Replace('_', ' ');
        cleaned = MultiSpaceRegex.Replace(cleaned, " ");
        cleaned = cleaned.Trim(' ', '-', '_', '.', ',');

        return cleaned;
    }

    private static string CleanText(string text)
    {
        var cleaned = text.Trim();
        cleaned = cleaned.Replace('_', ' ');
        cleaned = MultiSpaceRegex.Replace(cleaned, " ");
        return cleaned.Trim();
    }

    private static string NormalizeChapterTitle(string chapterTitle)
    {
        return CleanText(chapterTitle).ToUpperInvariant();
    }

    private static bool IsNumberedChapterDuplicateGroup(IEnumerable<TrackInfo> tracks)
    {
        return tracks.All(track => StartsWithChapterNumber(track.ChapterTitle));
    }

    private static bool StartsWithChapterNumber(string chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return false;

        return LeadingNumberTrackRegex.IsMatch(chapterTitle) || NamedTrackRegex.IsMatch(chapterTitle);
    }

    private static bool IsGenericChapterTitle(string chapterTitle)
    {
        if (string.IsNullOrWhiteSpace(chapterTitle))
            return true;

        return GenericChapterRegex.IsMatch(chapterTitle.Trim());
    }

    private static int? TryExtractDiscNumber(string relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(relativeFolder))
            return null;

        var folderParts = relativeFolder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var folder in folderParts.Reverse())
        {
            var match = DiscRegex.Match(folder);

            if (match.Success && int.TryParse(match.Groups["number"].Value, out var number))
                return number;
        }

        return null;
    }

    private static int? TryExtractTrackNumber(string fileNameWithoutExtension)
    {
        var match = NamedTrackRegex.Match(fileNameWithoutExtension);

        if (!match.Success)
            match = LeadingNumberTrackRegex.Match(fileNameWithoutExtension);

        if (match.Success && int.TryParse(match.Groups["number"].Value, out var number))
            return number;

        return null;
    }

    private static string GetTrackNumberSuffix(string fileNameWithoutExtension)
    {
        var match = NamedTrackRegex.Match(fileNameWithoutExtension);

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
