using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record SourceMetadataCaptureRequest(
    Guid SourceId,
    string SourcePath,
    TrackInfo? Track = null,
    AudioProbeInfo? ProbeInfo = null,
    string Evidence = "");

public sealed record SourceMetadataCaptureResult(
    Guid SourceId,
    bool Published,
    bool AlreadyExisted,
    IReadOnlyList<string> Diagnostics);

public sealed class SourceMetadataCaptureService
{
    private readonly SourceMetadataSnapshotStore _snapshotStore;
    private readonly TagMetadataReader _tagMetadataReader;

    public SourceMetadataCaptureService()
        : this(new SourceMetadataSnapshotStore(), new TagMetadataReader())
    {
    }

    public SourceMetadataCaptureService(
        SourceMetadataSnapshotStore snapshotStore,
        TagMetadataReader tagMetadataReader)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _tagMetadataReader = tagMetadataReader ?? throw new ArgumentNullException(nameof(tagMetadataReader));
    }

    public SourceMetadataCaptureResult PublishIfMissing(
        string projectFolder,
        SourceMetadataCaptureRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceId == Guid.Empty)
            throw new ArgumentException("A source metadata capture requires a stable SourceId.", nameof(request));

        if (_snapshotStore.TryLoad(projectFolder, request.SourceId) is not null &&
            _snapshotStore.HasCurrentCoverage(projectFolder, request.SourceId))
        {
            return new SourceMetadataCaptureResult(
                request.SourceId,
                Published: false,
                AlreadyExisted: true,
                Diagnostics: []);
        }

        var snapshot = Capture(request);
        _snapshotStore.Save(projectFolder, snapshot);
        return new SourceMetadataCaptureResult(
            request.SourceId,
            Published: true,
            AlreadyExisted: false,
            snapshot.ReadDiagnostics);
    }

    public SourceMetadataSnapshot Capture(SourceMetadataCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<string>();
        TagMetadataReadResult tagResult;
        EmbeddedCoverData? coverData = null;

        if (File.Exists(request.SourcePath))
        {
            tagResult = _tagMetadataReader.Read(request.SourcePath);
            diagnostics.AddRange(tagResult.Diagnostics);
            if (tagResult.Failed)
                diagnostics.Add("Tags konnten nicht gelesen werden; vorhandene technische Daten bleiben erhalten.");

            coverData = _tagMetadataReader.ReadFirstEmbeddedCover(request.SourcePath);
            if (tagResult.HasEmbeddedCover && coverData is null)
                diagnostics.Add("Eingebettetes Cover wurde gemeldet, konnte aber nicht als Cover-Evidence gelesen werden.");
        }
        else
        {
            tagResult = new TagMetadataReadResult(
                new OriginalTagMetadata(),
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: true);
            diagnostics.Add("Originalquelle nicht verfügbar; Snapshot enthält nur vorhandene Legacy-/Track-Evidence.");
        }

        return BuildSnapshot(request, tagResult, coverData, diagnostics);
    }

    public void BackfillMissingSnapshots(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var requests = BuildBackfillRequests(projectFolder)
            .GroupBy(request => request.SourceId)
            .Where(group => group.Key != Guid.Empty)
            .Select(group => SelectUnambiguousRequest(group.ToList()))
            .Where(request => request is not null)
            .Cast<SourceMetadataCaptureRequest>()
            .ToList();

        foreach (var request in requests)
            PublishIfMissing(projectFolder, request);
    }

    public static SourceMetadataSnapshot BuildSnapshot(
        SourceMetadataCaptureRequest request,
        TagMetadataReadResult tagResult,
        EmbeddedCoverData? coverData,
        IReadOnlyList<string>? initialDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tagResult);

        var diagnostics = new List<string>();
        if (initialDiagnostics is not null)
            diagnostics.AddRange(initialDiagnostics);

        if (!string.IsNullOrWhiteSpace(request.Evidence))
            diagnostics.Add("Capture-Evidence: " + request.Evidence.Trim());

        var probeInfo = request.ProbeInfo;
        if (probeInfo is { Success: false } && !string.IsNullOrWhiteSpace(probeInfo.ErrorMessage))
            diagnostics.Add("ffprobe: " + probeInfo.ErrorMessage.Trim());

        var original = tagResult.Original;
        var chapters = request.ProbeInfo?.Chapters is { Count: > 0 } probeChapters
            ? CloneChapters(probeChapters)
            : CloneChapters(request.Track?.EmbeddedChapters ?? []);

        return new SourceMetadataSnapshot
        {
            SourceId = request.SourceId,
            CanonicalFields = BuildCanonicalFields(original),
            RawTags = BuildRawTags(original),
            OriginalChapters = chapters,
            CoverCandidate = BuildCoverCandidate(tagResult, coverData),
            TechnicalInfo = BuildTechnicalInfo(request, tagResult, probeInfo),
            ReadDiagnostics = diagnostics
        }.RequireComplete();
    }

    private static SourceMetadataCaptureRequest? SelectUnambiguousRequest(
        IReadOnlyList<SourceMetadataCaptureRequest> requests)
    {
        var existingSourceFiles = requests
            .Where(request => File.Exists(request.SourcePath))
            .GroupBy(request => NormalizePathForComparison(request.SourcePath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingSourceFiles.Count == 1)
            return existingSourceFiles[0].First();

        if (existingSourceFiles.Count > 1)
            return null;

        return requests.Count == 1
            ? requests[0]
            : null;
    }

    private static IReadOnlyList<SourceMetadataCaptureRequest> BuildBackfillRequests(string projectFolder)
    {
        var requests = new List<SourceMetadataCaptureRequest>();
        AddSourceCopyIdentityRequests(projectFolder, requests);
        AddMp3DiscRequests(projectFolder, requests);
        AddAudioDiscRequests(projectFolder, requests);
        return requests;
    }

    private static void AddSourceCopyIdentityRequests(string projectFolder, List<SourceMetadataCaptureRequest> requests)
    {
        foreach (var identity in new SourceCopyIdentityStore().Load(projectFolder))
        {
            if (identity.SourceId == Guid.Empty)
                continue;

            requests.Add(new SourceMetadataCaptureRequest(
                identity.SourceId,
                Path.Combine(projectFolder, identity.TargetRelativePath),
                Evidence: "source-copy-identity"));
        }
    }

    private static void AddMp3DiscRequests(string projectFolder, List<SourceMetadataCaptureRequest> requests)
    {
        var manifest = new Mp3DiscProjectService().TryLoad(projectFolder);
        if (manifest is null)
            return;

        foreach (var file in manifest.ImportedDiscs.SelectMany(disc => disc.SourceFiles))
        {
            if (file.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(file.RelativePath))
                continue;

            requests.Add(new SourceMetadataCaptureRequest(
                file.SourceId,
                Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectFolder), file.RelativePath),
                Evidence: "mp3-disc-manifest"));
        }
    }

    private static void AddAudioDiscRequests(string projectFolder, List<SourceMetadataCaptureRequest> requests)
    {
        var manifest = new AudioDiscProjectService().TryLoad(projectFolder);
        if (manifest is null)
            return;

        foreach (var track in manifest.Discs.SelectMany(disc => disc.Tracks))
        {
            if (track.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(track.RelativePath))
                continue;

            var sourcePath = Path.Combine(projectFolder, track.RelativePath);
            requests.Add(new SourceMetadataCaptureRequest(
                track.SourceId,
                sourcePath,
                new TrackInfo
                {
                    SourceId = track.SourceId,
                    Index = track.GlobalIndex,
                    DiscNumber = track.DiscNumber,
                    TrackNumber = track.TrackNumber,
                    FilePath = sourcePath,
                    FileName = track.FileName,
                    Duration = FormatDuration(track.Duration),
                    DurationTicks = track.Duration.Ticks,
                    BitrateKbps = 1411,
                    Channels = 2,
                    Codec = "PCM"
                },
                Evidence: "audio-disc-manifest"));
        }
    }

    public static SourceMetadataCanonicalFields BuildCanonicalFields(OriginalTagMetadata original)
    {
        return new SourceMetadataCanonicalFields
        {
            SourceTrackNumber = ToPositiveInt(original.TrackNumber),
            SourceTrackTotal = ToPositiveInt(original.TrackTotal),
            SourceDiscNumber = ToPositiveInt(original.DiscNumber),
            SourceDiscTotal = ToPositiveInt(original.DiscTotal),
            Title = Clean(original.Title),
            Subtitle = Clean(original.Subtitle),
            OriginalTitle = Clean(original.OriginalTitle),
            Album = Clean(original.Album),
            Description = Clean(original.Description),
            Keywords = CleanList(original.Keywords),
            ContentType = Clean(original.ContentType),
            Comment = Clean(original.Comment),
            Authors = CleanList(original.Authors),
            Narrators = CleanList(original.Narrators),
            Translators = CleanList(original.Translators),
            Composers = CleanList(original.Composers),
            Performers = CleanList(original.Performers),
            Contributors = CleanList(original.Contributors),
            Hosts = CleanList(original.Hosts),
            Guests = CleanList(original.Guests),
            Speakers = CleanList(original.Speakers),
            Genres = CleanList(original.Genres),
            Languages = CleanList(original.Languages),
            OriginalLanguages = CleanList(original.OriginalLanguages),
            TargetLanguages = CleanList(original.TargetLanguages),
            Series = CleanSeries(original.Series),
            Volume = Clean(original.Volume),
            Edition = Clean(original.Edition),
            AbridgedStatus = TryParseAbridgedStatus(original.AbridgedStatus),
            AudioWorkType = TryParseAudioWorkType(original.AudioWorkType),
            Script = Clean(original.Script),
            Publisher = Clean(original.Publisher),
            ReleaseDate = MetadataDateParser.TryParse(original.ReleaseDate, out var releaseDate) ? releaseDate : null,
            Year = ToPositiveInt(original.Year) ?? TryParseYear(original.ReleaseDate),
            Copyright = Clean(original.Copyright),
            PhonogramCopyright = Clean(original.PhonogramCopyright),
            License = Clean(original.License),
            Categories = CleanList(original.Categories),
            ISBN = Clean(original.ISBN),
            Barcode = Clean(original.Barcode),
            ASIN = Clean(original.ASIN),
            CatalogNumbers = CleanList(original.CatalogNumbers),
            MusicBrainzReleaseId = Clean(original.MusicBrainzReleaseId),
            SortTitle = Clean(original.SortTitle),
            SortAuthors = CleanList(original.SortAuthors),
            SortNarrators = CleanList(original.SortNarrators),
            SortAlbum = Clean(original.SortAlbum),
            SortSeries = Clean(original.SortSeries),
            Website = Clean(original.Website),
            Source = Clean(original.Source)
        };
    }

    private static IReadOnlyList<RawTagEntry> BuildRawTags(OriginalTagMetadata original) =>
        original.RawTagEntries.Count > 0
            ? original.RawTagEntries
                .Select(entry => new RawTagEntry(
                    entry.OriginalKey,
                    entry.Values,
                    entry.TagFormat,
                    entry.Namespace))
                .ToArray()
            : original.RawTags
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value.Count > 0)
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new RawTagEntry(
                item.Key,
                item.Value,
                MetadataTagFormat.Unknown,
                string.Empty))
            .ToArray();

    private static IReadOnlyList<TrackEmbeddedChapterInfo> CloneChapters(
        IEnumerable<TrackEmbeddedChapterInfo> chapters) =>
        chapters
            .Select(chapter => new TrackEmbeddedChapterInfo
            {
                Title = chapter.Title ?? string.Empty,
                StartTicks = chapter.StartTicks,
                EndTicks = chapter.EndTicks
            })
            .ToArray();

    private static SourceCoverCandidateInfo BuildCoverCandidate(
        TagMetadataReadResult tagResult,
        EmbeddedCoverData? coverData)
    {
        if (coverData is null)
        {
            return new SourceCoverCandidateInfo
            {
                HasEmbeddedCover = tagResult.HasEmbeddedCover
            };
        }

        return new SourceCoverCandidateInfo
        {
            HasEmbeddedCover = true,
            MimeType = coverData.MimeType?.Trim() ?? string.Empty,
            ByteLength = coverData.Bytes.LongLength,
            Fingerprint = Convert.ToHexString(SHA256.HashData(coverData.Bytes))
        };
    }

    private static SourceTechnicalInfo BuildTechnicalInfo(
        SourceMetadataCaptureRequest request,
        TagMetadataReadResult tagResult,
        AudioProbeInfo? probeInfo)
    {
        return new SourceTechnicalInfo
        {
            SourcePath = request.SourcePath ?? string.Empty,
            Duration = probeInfo?.Duration
                ?? tagResult.TechnicalDurationFallback
                ?? TicksToDuration(request.Track?.DurationTicks),
            BitrateKbps = probeInfo?.BitrateKbps
                ?? tagResult.TechnicalBitrateKbpsFallback
                ?? request.Track?.BitrateKbps,
            Channels = probeInfo?.Channels
                ?? tagResult.TechnicalChannelsFallback
                ?? request.Track?.Channels,
            Codec = Clean(probeInfo?.CodecName) is { Length: > 0 } codec
                ? codec
                : Clean(request.Track?.Codec),
            Container = Clean(probeInfo?.FormatName)
        };
    }

    private static TimeSpan? TicksToDuration(long? ticks) =>
        ticks is > 0 ? TimeSpan.FromTicks(ticks.Value) : null;

    private static int? TryParseYear(string? value) =>
        MetadataDateParser.TryParse(value, out var date)
            ? date.Year
            : null;

    private static AbridgedStatus? TryParseAbridgedStatus(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "1" or "TRUE" or "ABRIDGED" => AbridgedStatus.Abridged,
            "0" or "FALSE" or "UNABRIDGED" => AbridgedStatus.Unabridged,
            _ => null
        };
    }

    private static AudioWorkType? TryParseAudioWorkType(string value)
    {
        var normalized = NormalizeToken(value);
        return normalized switch
        {
            "AUDIOBOOK" => AudioWorkType.Audiobook,
            "AUDIODRAMA" => AudioWorkType.AudioDrama,
            _ => null
        };
    }

    private static string NormalizeToken(string value) =>
        new(Clean(value)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string FormatDuration(TimeSpan duration) =>
        duration.ToString(duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    private static string NormalizePathForComparison(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static int? ToPositiveInt(uint value) =>
        value is > 0 and <= int.MaxValue ? (int)value : null;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static IReadOnlyList<SeriesEntry> CleanSeries(IEnumerable<SeriesEntry> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value.Name))
            .Select(value => new SeriesEntry(value.Name.Trim(), value.Part?.Trim() ?? string.Empty))
            .ToArray();

    private static IReadOnlyList<string> CleanList(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
