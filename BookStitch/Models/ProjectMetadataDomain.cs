using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Models;

public enum MetadataOrigin
{
    None,
    SourceAggregation,
    FolderStructure,
    SingleFileName,
    GlobalPreference,
    Derived,
    User,
    LegacyMigrated,
    StaticList
}

public enum OutputFieldPolicy
{
    Enabled,
    Disabled
}

public enum AudioWorkType
{
    Unknown,
    Audiobook,
    AudioDrama
}

public enum AbridgedStatus
{
    Unknown,
    Unabridged,
    Abridged
}

public enum ProjectMetadataField
{
    Title,
    Subtitle,
    Album,
    Description,
    Keywords,
    ContentType,
    Comment,
    Authors,
    Narrators,
    Translators,
    Performers,
    Contributors,
    Hosts,
    Guests,
    Speakers,
    Series,
    Volume,
    Edition,
    AbridgedStatus,
    AudioWorkType,
    Languages,
    OriginalLanguages,
    TargetLanguages,
    Script,
    Publisher,
    ReleaseDate,
    Year = 28,
    Copyright = 29,
    PhonogramCopyright = 30,
    License = 31,
    Genres = 32,
    Categories = 33,
    ISBN = 34,
    Barcode = 35,
    ASIN = 36,
    CatalogNumbers = 37,
    MusicBrainzReleaseId = 38,
    SortTitle = 39,
    SortAuthors = 40,
    SortNarrators = 41,
    SortAlbum = 42,
    SortSeries = 43,
    Website = 44,
    Source = 45,
    OutputTrackNumber = 46,
    OutputTrackTotal = 47,
    Composers = 48,
    Cover = 49,
    OriginalTitle = 50
}

public static class ProjectMetadataFields
{
    private static readonly HashSet<ProjectMetadataField> RetiredCanonicalFields =
    [
        ProjectMetadataField.Performers,
        ProjectMetadataField.Contributors,
        ProjectMetadataField.Hosts,
        ProjectMetadataField.Guests,
        ProjectMetadataField.Speakers
    ];

    public static IReadOnlyList<ProjectMetadataField> OutputFields { get; } =
        Enum.GetValues<ProjectMetadataField>()
            .Where(IsCanonicalField)
            .ToArray();

    public static bool IsCanonicalField(ProjectMetadataField field) =>
        field != ProjectMetadataField.Cover && !RetiredCanonicalFields.Contains(field);

    public static ProjectMetadataFieldValueKind GetValueKind(ProjectMetadataField field) => field switch
    {
        ProjectMetadataField.Keywords or
        ProjectMetadataField.Authors or
        ProjectMetadataField.Narrators or
        ProjectMetadataField.Translators or
        ProjectMetadataField.Composers or
        ProjectMetadataField.Performers or
        ProjectMetadataField.Contributors or
        ProjectMetadataField.Hosts or
        ProjectMetadataField.Guests or
        ProjectMetadataField.Speakers or
        ProjectMetadataField.Languages or
        ProjectMetadataField.OriginalLanguages or
        ProjectMetadataField.TargetLanguages or
        ProjectMetadataField.Genres or
        ProjectMetadataField.Categories or
        ProjectMetadataField.CatalogNumbers or
        ProjectMetadataField.SortAuthors or
        ProjectMetadataField.SortNarrators => ProjectMetadataFieldValueKind.StringList,
        ProjectMetadataField.Series => ProjectMetadataFieldValueKind.SeriesList,
        ProjectMetadataField.ReleaseDate => ProjectMetadataFieldValueKind.MetadataDate,
        ProjectMetadataField.Year or
        ProjectMetadataField.OutputTrackNumber or
        ProjectMetadataField.OutputTrackTotal => ProjectMetadataFieldValueKind.Integer,
        ProjectMetadataField.AudioWorkType => ProjectMetadataFieldValueKind.AudioWorkType,
        ProjectMetadataField.AbridgedStatus => ProjectMetadataFieldValueKind.AbridgedStatus,
        _ => ProjectMetadataFieldValueKind.String
    };
}

public enum ProjectMetadataFieldValueKind
{
    String,
    StringList,
    SeriesList,
    MetadataDate,
    Integer,
    AudioWorkType,
    AbridgedStatus
}

public readonly record struct MetadataDate
{
    public int Year { get; }

    public int? Month { get; }

    public int? Day { get; }

    [System.Text.Json.Serialization.JsonConstructor]
    public MetadataDate(int year, int? month = null, int? day = null)
    {
        if (year is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year));

        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        if (day.HasValue && !month.HasValue)
            throw new ArgumentException("A day requires a month.", nameof(day));

        if (day.HasValue && (day.Value < 1 || day.Value > DateTime.DaysInMonth(year, month!.Value)))
            throw new ArgumentOutOfRangeException(nameof(day));

        Year = year;
        Month = month;
        Day = day;
    }

    public override string ToString()
    {
        if (Month is null)
            return Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);

        if (Day is null)
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Year:0000}-{Month.Value:00}");

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Year:0000}-{Month.Value:00}-{Day.Value:00}");
    }
}

public sealed record SeriesEntry(string Name, string Part)
{
    public string Name { get; init; } = Name ?? string.Empty;

    public string Part { get; init; } = Part ?? string.Empty;
}

public sealed record MetadataValue<T>(T? Value, MetadataOrigin Origin)
{
    public bool IsUserDefined => Origin == MetadataOrigin.User;
}

public sealed record ProjectCoverMetadata(
    string SourcePath = "",
    string ProcessedPath = "",
    MetadataOrigin Origin = MetadataOrigin.None)
{
    public string SourcePath { get; init; } = SourcePath ?? string.Empty;

    public string ProcessedPath { get; init; } = ProcessedPath ?? string.Empty;
}

public sealed class ProjectMetadata : INotifyPropertyChanged
{
    private readonly Dictionary<ProjectMetadataField, object?> _values = [];
    private readonly Dictionary<ProjectMetadataField, List<string>> _suppressedValues = [];
    private ProjectCoverMetadata _cover = new();
    private bool _useNarratorsAsComposers;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Revision { get; private set; }

    public ProjectCoverMetadata Cover => _cover;

    public bool UseNarratorsAsComposers => _useNarratorsAsComposers;

    public bool SetUseNarratorsAsComposers(bool value)
    {
        if (_useNarratorsAsComposers == value)
            return false;

        _useNarratorsAsComposers = value;
        IncrementRevision(nameof(UseNarratorsAsComposers));
        return true;
    }

    public MetadataValue<T> GetValue<T>(ProjectMetadataField field)
    {
        EnsureValueType<T>(field);
        return _values.TryGetValue(field, out var value) && value is MetadataValue<T> typed
            ? typed
            : new MetadataValue<T>(default, MetadataOrigin.None);
    }

    public bool SetValue<T>(ProjectMetadataField field, T? value, MetadataOrigin origin)
    {
        EnsureCanonicalField(field);
        EnsureValueType<T>(field);
        var normalized = NormalizeValue(value);
        var next = new MetadataValue<T>(normalized, origin);
        var current = GetValue<T>(field);

        if (AreMetadataValuesEqual(current, next))
            return false;

        _values[field] = next;
        IncrementRevision(field);
        return true;
    }

    private static void EnsureCanonicalField(ProjectMetadataField field)
    {
        if (!ProjectMetadataFields.IsCanonicalField(field))
        {
            throw new ArgumentOutOfRangeException(
                nameof(field),
                field,
                "The field is retained only for source/tag compatibility and is not part of canonical ProjectMetadata.");
        }
    }

    private static void EnsureValueType<T>(ProjectMetadataField field)
    {
        var expectedKind = ProjectMetadataFields.GetValueKind(field);
        var actualType = typeof(T);
        var compatible = expectedKind switch
        {
            ProjectMetadataFieldValueKind.String => actualType == typeof(string),
            ProjectMetadataFieldValueKind.StringList => actualType == typeof(IReadOnlyList<string>),
            ProjectMetadataFieldValueKind.SeriesList => actualType == typeof(IReadOnlyList<SeriesEntry>),
            ProjectMetadataFieldValueKind.MetadataDate => actualType == typeof(MetadataDate) || actualType == typeof(MetadataDate?),
            ProjectMetadataFieldValueKind.Integer => actualType == typeof(int) || actualType == typeof(int?),
            ProjectMetadataFieldValueKind.AudioWorkType => actualType == typeof(AudioWorkType) || actualType == typeof(AudioWorkType?),
            ProjectMetadataFieldValueKind.AbridgedStatus => actualType == typeof(AbridgedStatus) || actualType == typeof(AbridgedStatus?),
            _ => false
        };

        if (!compatible)
        {
            throw new ArgumentException(
                $"ProjectMetadata field '{field}' expects {expectedKind}, but was accessed as {actualType.Name}.",
                nameof(field));
        }
    }

    public IReadOnlyDictionary<ProjectMetadataField, object?> GetExplicitValues()
    {
        return new ReadOnlyDictionary<ProjectMetadataField, object?>(
            new Dictionary<ProjectMetadataField, object?>(_values));
    }

    public IReadOnlyList<string> GetSuppressedValues(ProjectMetadataField field) =>
        _suppressedValues.TryGetValue(field, out var values)
            ? values.ToArray()
            : [];

    public bool SuppressValue(ProjectMetadataField field, string? value)
    {
        if (field != ProjectMetadataField.Cover)
            EnsureCanonicalField(field);
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return false;

        if (!_suppressedValues.TryGetValue(field, out var values))
        {
            values = [];
            _suppressedValues[field] = values;
        }

        if (values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return false;

        values.Add(normalized);
        IncrementRevision($"{field}.SuppressedValues");
        return true;
    }

    public bool UnsuppressValue(ProjectMetadataField field, string? value)
    {
        if (field != ProjectMetadataField.Cover)
            EnsureCanonicalField(field);
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || !_suppressedValues.TryGetValue(field, out var values))
            return false;

        var index = values.FindIndex(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        values.RemoveAt(index);
        if (values.Count == 0)
            _suppressedValues.Remove(field);
        IncrementRevision($"{field}.SuppressedValues");
        return true;
    }

    internal IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<string>> GetSuppressedValuesForPersistence() =>
        _suppressedValues.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)item.Value.ToArray());

    internal void SetSuppressedValuesForPersistence(IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<string>> values)
    {
        _suppressedValues.Clear();
        foreach (var item in values)
        {
            var normalized = item.Value
                .Select(value => value?.Trim() ?? string.Empty)
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalized.Count > 0)
                _suppressedValues[item.Key] = normalized;
        }
    }

    public bool SetCover(string? sourcePath, string? processedPath, MetadataOrigin origin)
    {
        var next = new ProjectCoverMetadata(
            NormalizePathValue(sourcePath),
            NormalizePathValue(processedPath),
            origin);

        if (_cover == next)
            return false;

        _cover = next;
        IncrementRevision(nameof(Cover));
        return true;
    }

    internal void SetCoverForPersistence(ProjectCoverMetadata? cover)
    {
        _cover = cover ?? new ProjectCoverMetadata();
    }

    internal void SetUseNarratorsAsComposersForPersistence(bool value)
    {
        _useNarratorsAsComposers = value;
    }

    internal void SetRevisionForPersistence(long revision)
    {
        Revision = Math.Max(0, revision);
    }

    private static string NormalizePathValue(string? value) => value?.Trim() ?? string.Empty;

    private static T? NormalizeValue<T>(T? value)
    {
        if (value is null)
            return default;

        if (value is string text)
            return (T)(object)text.Trim();

        if (value is IEnumerable<string> strings)
            return (T)(object)strings
                .Select(item => item ?? string.Empty)
                .ToArray();

        if (value is IEnumerable<SeriesEntry> series)
            return (T)(object)series
                .Select(item => new SeriesEntry(item.Name, item.Part))
                .ToArray();

        return value;
    }

    private static bool AreMetadataValuesEqual<T>(MetadataValue<T> left, MetadataValue<T> right)
    {
        if (left.Origin != right.Origin)
            return false;

        return AreValuesEqual(left.Value, right.Value);
    }

    private static bool AreValuesEqual<T>(T? left, T? right)
    {
        if (left is IEnumerable<string> leftStrings && right is IEnumerable<string> rightStrings)
            return leftStrings.SequenceEqual(rightStrings, StringComparer.Ordinal);

        if (left is IEnumerable<SeriesEntry> leftSeries && right is IEnumerable<SeriesEntry> rightSeries)
            return leftSeries.SequenceEqual(rightSeries);

        return EqualityComparer<T?>.Default.Equals(left, right);
    }

    private void IncrementRevision(ProjectMetadataField field, [CallerMemberName] string? propertyName = null)
    {
        Revision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(field.ToString()));
    }

    private void IncrementRevision(string propertyName)
    {
        Revision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
