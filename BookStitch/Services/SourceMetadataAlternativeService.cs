using BookStitch.Models;
using System.Collections;
using System.Globalization;

namespace BookStitch.Services;

public abstract record SourceMetadataAlternative(
    ProjectMetadataField Field,
    int SourceCount,
    string DisplayValue)
{
    public MetadataOrigin EvidenceOrigin { get; init; } = MetadataOrigin.SourceAggregation;
    public bool IsCanonicalWinner { get; init; }
    public bool IsAutomaticWinner { get; init; }
    public bool ShowSourceCount => EvidenceOrigin != MetadataOrigin.StaticList;
    public string EvidenceLabel => MetadataOriginEvidenceLabels.Format(EvidenceOrigin) + (IsAutomaticWinner ? " ★" : string.Empty);
}

public static class MetadataOriginEvidenceLabels
{
    public static string FormatOptional(MetadataOrigin origin) =>
        origin == MetadataOrigin.None ? string.Empty : Format(origin);

    public static string Format(MetadataOrigin origin) => origin switch
    {
        MetadataOrigin.SourceAggregation => LocalizationText.Get("Metadata.Origin.SourceAggregation"),
        MetadataOrigin.FolderStructure => LocalizationText.Get("Metadata.Origin.FolderStructure"),
        MetadataOrigin.SingleFileName => LocalizationText.Get("Metadata.Origin.SingleFileName"),
        MetadataOrigin.GlobalPreference => LocalizationText.Get("Metadata.Origin.GlobalPreference"),
        MetadataOrigin.Derived => LocalizationText.Get("Metadata.Origin.Derived"),
        MetadataOrigin.User => LocalizationText.Get("Metadata.Origin.User"),
        MetadataOrigin.LegacyMigrated => LocalizationText.Get("Metadata.Origin.LegacyMigrated"),
        MetadataOrigin.StaticList => LocalizationText.Get("Metadata.Origin.StaticList"),
        _ => "–"
    };
}

public sealed record ScalarSourceMetadataAlternative(
    ProjectMetadataField Field,
    object Value,
    int SourceCount,
    string DisplayValue)
    : SourceMetadataAlternative(Field, SourceCount, DisplayValue);

public sealed record ListSourceMetadataAlternative(
    ProjectMetadataField Field,
    IReadOnlyList<string> Values,
    int SourceCount,
    string DisplayValue)
    : SourceMetadataAlternative(Field, SourceCount, DisplayValue);

public sealed record SeriesSourceMetadataAlternative(
    ProjectMetadataField Field,
    IReadOnlyList<SeriesEntry> Entries,
    int SourceCount,
    string DisplayValue)
    : SourceMetadataAlternative(Field, SourceCount, DisplayValue);

public sealed class SourceMetadataAlternativeService
{
    private readonly SourceMetadataSnapshotStore _snapshotStore;
    private readonly ProjectTitleSourceEvidenceService _projectTitleEvidence;
    private readonly MetadataNormalizationService _normalization;

    public SourceMetadataAlternativeService(
        SourceMetadataSnapshotStore? snapshotStore = null,
        ProjectTitleSourceEvidenceService? projectTitleEvidence = null,
        MetadataNormalizationService? normalization = null)
    {
        _snapshotStore = snapshotStore ?? new SourceMetadataSnapshotStore();
        _projectTitleEvidence = projectTitleEvidence ?? new ProjectTitleSourceEvidenceService();
        _normalization = normalization ?? new MetadataNormalizationService();
    }

    public Task<IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>>> LoadAsync(
        string projectFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        return Task.Run(() => Build(_snapshotStore.LoadAll(projectFolder), cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<ScalarSourceMetadataAlternative>>> LoadScalarTextCandidatesAsync(
        string projectFolder,
        IReadOnlyList<ProjectMetadataField> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(fields);

        var requestedFields = fields.Distinct().ToArray();
        return Task.Run<IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<ScalarSourceMetadataAlternative>>>(() =>
        {
            var snapshots = _snapshotStore.LoadAll(projectFolder);
            return requestedFields.ToDictionary(
                field => field,
                field => BuildScalarTextCandidates(snapshots, field, cancellationToken));
        }, cancellationToken);
    }

    public IReadOnlyList<ScalarSourceMetadataAlternative> BuildScalarTextCandidates(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataField field,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (ProjectMetadataFields.GetValueKind(field) != ProjectMetadataFieldValueKind.String)
            throw new ArgumentException($"Field '{field}' is not a scalar text field.", nameof(field));

        cancellationToken.ThrowIfCancellationRequested();

        if (field == ProjectMetadataField.Title)
        {
            return _projectTitleEvidence
                .BuildTitleCandidateGroups(snapshots)
                .Select(group => new ScalarSourceMetadataAlternative(
                    field,
                    group.Value,
                    group.SourceCount,
                    group.Value))
                .Select((candidate, index) => candidate with { IsAutomaticWinner = index == 0 })
                .ToArray();
        }

        return snapshots
            .Select((snapshot, index) => new
            {
                Value = Read(field, snapshot.CanonicalFields) as string,
                Index = index
            })
            .Select(item => new
            {
                DisplayValue = _normalization.NormalizeText(item.Value),
                ComparisonKey = _normalization.NormalizeForComparison(field, item.Value),
                item.Index
            })
            .Where(item => item.DisplayValue.Length > 0 && item.ComparisonKey.Length > 0)
            .GroupBy(item => item.ComparisonKey, StringComparer.Ordinal)
            .Select(group => new
            {
                DisplayValue = group.OrderBy(item => item.Index).First().DisplayValue,
                SourceCount = group.Count(),
                FirstIndex = group.Min(item => item.Index)
            })
            .OrderByDescending(item => item.SourceCount)
            .ThenBy(item => item.FirstIndex)
            .ThenBy(item => item.DisplayValue, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ScalarSourceMetadataAlternative(
                field,
                item.DisplayValue,
                item.SourceCount,
                item.DisplayValue))
            .Select((candidate, index) => candidate with { IsAutomaticWinner = index == 0 })
            .ToArray();
    }

    public IReadOnlyList<ListSourceMetadataAlternative> BuildStringListItemCandidates(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataField field,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (ProjectMetadataFields.GetValueKind(field) != ProjectMetadataFieldValueKind.StringList)
            throw new ArgumentException($"Field '{field}' is not a string-list field.", nameof(field));

        cancellationToken.ThrowIfCancellationRequested();

        return snapshots
            .SelectMany((snapshot, sourceIndex) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = Read(field, snapshot.CanonicalFields) as IReadOnlyList<string> ?? [];
                var seenInSource = new HashSet<string>(StringComparer.Ordinal);
                return values
                    .Select((value, valueIndex) => new
                    {
                        DisplayValue = _normalization.NormalizeText(value),
                        ComparisonKey = _normalization.NormalizeForComparison(field, value),
                        SourceIndex = sourceIndex,
                        ValueIndex = valueIndex
                    })
                    .Where(item => item.DisplayValue.Length > 0 &&
                                   item.ComparisonKey.Length > 0 &&
                                   seenInSource.Add(item.ComparisonKey));
            })
            .GroupBy(item => item.ComparisonKey, StringComparer.Ordinal)
            .Select(group => new
            {
                DisplayValue = group
                    .OrderBy(item => item.SourceIndex)
                    .ThenBy(item => item.ValueIndex)
                    .First()
                    .DisplayValue,
                SourceCount = group.Count(),
                FirstSourceIndex = group.Min(item => item.SourceIndex),
                FirstValueIndex = group
                    .Where(item => item.SourceIndex == group.Min(candidate => candidate.SourceIndex))
                    .Min(item => item.ValueIndex)
            })
            .OrderByDescending(item => item.SourceCount)
            .ThenBy(item => item.FirstSourceIndex)
            .ThenBy(item => item.FirstValueIndex)
            .ThenBy(item => item.DisplayValue, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ListSourceMetadataAlternative(
                field,
                [item.DisplayValue],
                item.SourceCount,
                item.DisplayValue)
            {
                IsAutomaticWinner = true
            })
            .ToArray();
    }

    public IReadOnlyList<ListSourceMetadataAlternative> BuildGenreCandidates(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        IReadOnlyList<string> standardGenres,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(standardGenres);

        var sourceCandidates = BuildStringListItemCandidates(
            snapshots,
            ProjectMetadataField.Genres,
            cancellationToken);

        var result = new List<ListSourceMetadataAlternative>(sourceCandidates);
        var seen = new HashSet<string>(
            sourceCandidates
                .Select(candidate => _normalization.NormalizeForComparison(
                    ProjectMetadataField.Genres,
                    candidate.DisplayValue))
                .Where(key => key.Length > 0),
            StringComparer.Ordinal);

        foreach (var rawValue in standardGenres)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GenreListService.IsSelectableGenre(rawValue))
                continue;

            var displayValue = _normalization.NormalizeText(rawValue);
            var key = _normalization.NormalizeForComparison(ProjectMetadataField.Genres, displayValue);
            if (displayValue.Length == 0 || key.Length == 0 || !seen.Add(key))
                continue;

            result.Add(new ListSourceMetadataAlternative(
                ProjectMetadataField.Genres,
                [displayValue],
                1,
                displayValue)
            {
                EvidenceOrigin = MetadataOrigin.StaticList
            });
        }

        return result;
    }

    public IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>> Build(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var result = new Dictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>>();

        foreach (var field in ProjectMetadataFields.OutputFields)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (field == ProjectMetadataField.Title)
            {
                var titleEvidence = _projectTitleEvidence.SelectTitle(snapshots);
                if (titleEvidence is not null)
                {
                    result[field] =
                    [
                        new ScalarSourceMetadataAlternative(
                            field,
                            titleEvidence.Value,
                            titleEvidence.SourceCount,
                            titleEvidence.Value)
                    ];
                }

                continue;
            }

            var candidates = snapshots
                .Select(snapshot => Read(field, snapshot.CanonicalFields))
                .Where(value => value is not null)
                .Cast<object>()
                .ToArray();
            if (candidates.Length == 0)
                continue;

            result[field] = candidates
                .GroupBy(value => value, MetadataAlternativeValueComparer.Instance)
                .Select(group => Create(field, group.First(), group.Count()))
                .OrderByDescending(item => item.SourceCount)
                .ThenBy(item => item.DisplayValue, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        return result;
    }

    private static SourceMetadataAlternative Create(ProjectMetadataField field, object value, int count) => value switch
    {
        IReadOnlyList<string> values => new ListSourceMetadataAlternative(field, values.ToArray(), count, string.Join("; ", values)),
        IReadOnlyList<SeriesEntry> entries => new SeriesSourceMetadataAlternative(
            field,
            entries.Select(entry => new SeriesEntry(entry.Name, entry.Part)).ToArray(),
            count,
            string.Join("; ", entries.Select(entry => string.IsNullOrWhiteSpace(entry.Part) ? entry.Name : $"{entry.Name} #{entry.Part}"))),
        _ => new ScalarSourceMetadataAlternative(field, value, count, FormatScalar(value))
    };

    private static object? Read(ProjectMetadataField field, SourceMetadataCanonicalFields value) => field switch
    {
        ProjectMetadataField.Title => NonEmpty(value.Title),
        ProjectMetadataField.Subtitle => NonEmpty(value.Subtitle),
        ProjectMetadataField.OriginalTitle => NonEmpty(value.OriginalTitle),
        ProjectMetadataField.Album => NonEmpty(value.Album),
        ProjectMetadataField.Description => NonEmpty(value.Description),
        ProjectMetadataField.Keywords => NonEmpty(value.Keywords),
        ProjectMetadataField.ContentType => NonEmpty(value.ContentType),
        ProjectMetadataField.Comment => NonEmpty(value.Comment),
        ProjectMetadataField.Authors => NonEmpty(value.Authors),
        ProjectMetadataField.Narrators => NonEmpty(value.Narrators),
        ProjectMetadataField.Translators => NonEmpty(value.Translators),
        ProjectMetadataField.Composers => NonEmpty(value.Composers),
        ProjectMetadataField.Performers => NonEmpty(value.Performers),
        ProjectMetadataField.Contributors => NonEmpty(value.Contributors),
        ProjectMetadataField.Hosts => NonEmpty(value.Hosts),
        ProjectMetadataField.Guests => NonEmpty(value.Guests),
        ProjectMetadataField.Speakers => NonEmpty(value.Speakers),
        ProjectMetadataField.Series => NonEmpty(value.Series),
        ProjectMetadataField.Volume => NonEmpty(value.Volume),
        ProjectMetadataField.Edition => NonEmpty(value.Edition),
        ProjectMetadataField.AbridgedStatus => value.AbridgedStatus,
        ProjectMetadataField.AudioWorkType => value.AudioWorkType,
        ProjectMetadataField.Languages => NonEmpty(value.Languages),
        ProjectMetadataField.OriginalLanguages => NonEmpty(value.OriginalLanguages),
        ProjectMetadataField.TargetLanguages => NonEmpty(value.TargetLanguages),
        ProjectMetadataField.Script => NonEmpty(value.Script),
        ProjectMetadataField.Publisher => NonEmpty(value.Publisher),
        ProjectMetadataField.ReleaseDate => value.ReleaseDate,
        ProjectMetadataField.Year => value.Year,
        ProjectMetadataField.Copyright => NonEmpty(value.Copyright),
        ProjectMetadataField.PhonogramCopyright => NonEmpty(value.PhonogramCopyright),
        ProjectMetadataField.License => NonEmpty(value.License),
        ProjectMetadataField.Genres => NonEmpty(value.Genres),
        ProjectMetadataField.Categories => NonEmpty(value.Categories),
        ProjectMetadataField.ISBN => NonEmpty(value.ISBN),
        ProjectMetadataField.Barcode => NonEmpty(value.Barcode),
        ProjectMetadataField.ASIN => NonEmpty(value.ASIN),
        ProjectMetadataField.CatalogNumbers => NonEmpty(value.CatalogNumbers),
        ProjectMetadataField.MusicBrainzReleaseId => NonEmpty(value.MusicBrainzReleaseId),
        ProjectMetadataField.SortTitle => NonEmpty(value.SortTitle),
        ProjectMetadataField.SortAuthors => NonEmpty(value.SortAuthors),
        ProjectMetadataField.SortNarrators => NonEmpty(value.SortNarrators),
        ProjectMetadataField.SortAlbum => NonEmpty(value.SortAlbum),
        ProjectMetadataField.SortSeries => NonEmpty(value.SortSeries),
        ProjectMetadataField.Website => NonEmpty(value.Website),
        ProjectMetadataField.Source => NonEmpty(value.Source),
        _ => null
    };

    private static object? NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static object? NonEmpty(IReadOnlyList<string> value) => value.Count == 0 ? null : value;
    private static object? NonEmpty(IReadOnlyList<SeriesEntry> value) => value.Count == 0 ? null : value;

    private static string FormatScalar(object value) => value switch
    {
        MetadataDate date => MetadataDateParser.FormatDisplay(date),
        AudioWorkType workType => MetadataDisplayLabels.AudioWorkType(workType),
        AbridgedStatus status => MetadataDisplayLabels.AbridgedStatus(status),
        IFormattable formattable => formattable.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty
    };

    private sealed class MetadataAlternativeValueComparer : IEqualityComparer<object>
    {
        public static MetadataAlternativeValueComparer Instance { get; } = new();

        public new bool Equals(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is IReadOnlyList<string> leftStrings && right is IReadOnlyList<string> rightStrings)
                return leftStrings.SequenceEqual(rightStrings, StringComparer.OrdinalIgnoreCase);
            if (left is IReadOnlyList<SeriesEntry> leftSeries && right is IReadOnlyList<SeriesEntry> rightSeries)
                return leftSeries.SequenceEqual(rightSeries);
            return left?.Equals(right) == true;
        }

        public int GetHashCode(object value)
        {
            var hash = new HashCode();
            if (value is IReadOnlyList<string> strings)
            {
                foreach (var item in strings)
                    hash.Add(item, StringComparer.OrdinalIgnoreCase);
            }
            else if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                    hash.Add(item);
            }
            else
            {
                hash.Add(value);
            }
            return hash.ToHashCode();
        }
    }
}

internal static class MetadataDisplayLabels
{
    public static string AudioWorkType(AudioWorkType value) => value switch
    {
        Models.AudioWorkType.Audiobook => LocalizationText.Get("Metadata.WorkType.Audiobook"),
        Models.AudioWorkType.AudioDrama => LocalizationText.Get("Metadata.WorkType.AudioDrama"),
        _ => LocalizationText.Get("Metadata.Common.Unknown")
    };

    public static string AbridgedStatus(AbridgedStatus value) => value switch
    {
        Models.AbridgedStatus.Unabridged => LocalizationText.Get("Metadata.AbridgedStatus.Unabridged"),
        Models.AbridgedStatus.Abridged => LocalizationText.Get("Metadata.AbridgedStatus.Abridged"),
        _ => LocalizationText.Get("Metadata.Common.Unknown")
    };
}
