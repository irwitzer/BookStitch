namespace BookStitch.Models;

public enum MetadataTagFormat
{
    Unknown,
    Id3v1,
    Id3v2,
    Ape,
    Mp4,
    Xiph,
    Asf,
    Riff
}

public enum MetadataCandidateQuality
{
    Exact,
    CompatibleAlias,
    LegacyHeuristic
}

public sealed record RawTagEntry(
    string OriginalKey,
    IReadOnlyList<string> Values,
    MetadataTagFormat TagFormat,
    string Namespace)
{
    public string OriginalKey { get; init; } = NormalizeRequired(OriginalKey, nameof(OriginalKey));

    public IReadOnlyList<string> Values { get; init; } =
        (Values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();

    public string Namespace { get; init; } = Namespace?.Trim() ?? string.Empty;

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A raw tag key is required.", parameterName);

        return value.Trim();
    }
}

public sealed record MetadataCandidate(
    ProjectMetadataField Field,
    object? Value,
    MetadataCandidateQuality Quality,
    string SourceKey);

public sealed record SourceMetadataCanonicalFields
{
    public int? SourceTrackNumber { get; init; }
    public int? SourceTrackTotal { get; init; }
    public int? SourceDiscNumber { get; init; }
    public int? SourceDiscTotal { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string OriginalTitle { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string ContentType { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public IReadOnlyList<string> Authors { get; init; } = [];
    public IReadOnlyList<string> Narrators { get; init; } = [];
    public IReadOnlyList<string> Translators { get; init; } = [];
    public IReadOnlyList<string> Composers { get; init; } = [];
    public IReadOnlyList<string> Performers { get; init; } = [];
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public IReadOnlyList<string> Guests { get; init; } = [];
    public IReadOnlyList<string> Speakers { get; init; } = [];
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<string> OriginalLanguages { get; init; } = [];
    public IReadOnlyList<string> TargetLanguages { get; init; } = [];
    public IReadOnlyList<SeriesEntry> Series { get; init; } = [];
    public string Volume { get; init; } = string.Empty;
    public string Edition { get; init; } = string.Empty;
    public AbridgedStatus? AbridgedStatus { get; init; }
    public AudioWorkType? AudioWorkType { get; init; }
    public string Script { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public MetadataDate? ReleaseDate { get; init; }
    public int? Year { get; init; }
    public string Copyright { get; init; } = string.Empty;
    public string PhonogramCopyright { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string ISBN { get; init; } = string.Empty;
    public string Barcode { get; init; } = string.Empty;
    public string ASIN { get; init; } = string.Empty;
    public IReadOnlyList<string> CatalogNumbers { get; init; } = [];
    public string MusicBrainzReleaseId { get; init; } = string.Empty;
    public string SortTitle { get; init; } = string.Empty;
    public IReadOnlyList<string> SortAuthors { get; init; } = [];
    public IReadOnlyList<string> SortNarrators { get; init; } = [];
    public string SortAlbum { get; init; } = string.Empty;
    public string SortSeries { get; init; } = string.Empty;
    public string Website { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed record SourceTechnicalInfo
{
    public string SourcePath { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
    public int? BitrateKbps { get; init; }
    public int? Channels { get; init; }
    public string Codec { get; init; } = string.Empty;
    public string Container { get; init; } = string.Empty;
}

public sealed record SourceCoverCandidateInfo
{
    public bool HasEmbeddedCover { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public long? ByteLength { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
}

public sealed record SourceMetadataSnapshot
{
    public Guid SourceId { get; init; }
    public SourceMetadataCanonicalFields CanonicalFields { get; init; } = new();
    public IReadOnlyList<RawTagEntry> RawTags { get; init; } = [];
    public IReadOnlyList<TrackEmbeddedChapterInfo> OriginalChapters { get; init; } = [];
    public SourceCoverCandidateInfo CoverCandidate { get; init; } = new();
    public SourceTechnicalInfo TechnicalInfo { get; init; } = new();
    public IReadOnlyList<string> ReadDiagnostics { get; init; } = [];

    public SourceMetadataSnapshot RequireComplete()
    {
        if (SourceId == Guid.Empty)
            throw new InvalidOperationException("A source snapshot requires a stable SourceId.");

        return this with
        {
            RawTags = RawTags.ToArray(),
            OriginalChapters = OriginalChapters
                .Select(chapter => new TrackEmbeddedChapterInfo
                {
                    Title = chapter.Title,
                    StartTicks = chapter.StartTicks,
                    EndTicks = chapter.EndTicks
                })
                .ToArray(),
            ReadDiagnostics = ReadDiagnostics
                .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
                .Select(diagnostic => diagnostic.Trim())
                .ToArray()
        };
    }
}
