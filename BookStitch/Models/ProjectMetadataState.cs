using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Models;

public sealed class ProjectMetadataState : INotifyPropertyChanged
{
    private string _coverSourcePath = "";
    private string _processedCoverPath = "";
    private string _coverPreviewSource = "";
    private ProjectMetadata _canonicalMetadata;

    public ProjectMetadataState()
    {
        _canonicalMetadata = CreateEmptyCanonicalMetadata("Audiobook", MetadataOrigin.LegacyMigrated);
        _canonicalMetadata.PropertyChanged += CanonicalMetadata_PropertyChanged;
    }

    public ProjectMetadata CanonicalMetadata => _canonicalMetadata;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BookTitle
    {
        get => ReadScalar(ProjectMetadataField.Title);
        set => SetScalar(ProjectMetadataField.Title, value, MetadataOrigin.User);
    }

    public string Album
    {
        get => ReadScalar(ProjectMetadataField.Album);
        set => SetScalar(ProjectMetadataField.Album, value, MetadataOrigin.User);
    }

    public string Subtitle
    {
        get => ReadScalar(ProjectMetadataField.Subtitle);
        set => SetScalar(ProjectMetadataField.Subtitle, value, MetadataOrigin.User);
    }

    public string OriginalTitle
    {
        get => ReadScalar(ProjectMetadataField.OriginalTitle);
        set => SetScalar(ProjectMetadataField.OriginalTitle, value, MetadataOrigin.User);
    }

    public AudioWorkType? WorkType
    {
        get => _canonicalMetadata.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value;
        set => _canonicalMetadata.SetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType, value, MetadataOrigin.User);
    }

    public AbridgedStatus? AbridgedStatus
    {
        get => _canonicalMetadata.GetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus).Value;
        set => _canonicalMetadata.SetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus, value, MetadataOrigin.User);
    }

    public IReadOnlyList<SeriesEntry> Series
    {
        get => _canonicalMetadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value ?? [];
        set
        {
            var next = value is null
                ? Array.Empty<SeriesEntry>()
                : value.Select(item => new SeriesEntry(item.Name, item.Part)).ToArray();
            _canonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
                ProjectMetadataField.Series,
                next,
                MetadataOrigin.User);
        }
    }

    public string Volume
    {
        get => ReadScalar(ProjectMetadataField.Volume);
        set => SetScalar(ProjectMetadataField.Volume, value, MetadataOrigin.User);
    }

    public string Edition
    {
        get => ReadScalar(ProjectMetadataField.Edition);
        set => SetScalar(ProjectMetadataField.Edition, value, MetadataOrigin.User);
    }

    public string Author
    {
        get => ReadPrimaryListValue(ProjectMetadataField.Authors);
        set => SetPrimaryListValue(ProjectMetadataField.Authors, value, MetadataOrigin.User);
    }

    public string Narrator
    {
        get => ReadPrimaryListValue(ProjectMetadataField.Narrators);
        set => SetPrimaryListValue(ProjectMetadataField.Narrators, value, MetadataOrigin.User);
    }

    public bool UseNarratorsAsComposers
    {
        get => _canonicalMetadata.UseNarratorsAsComposers;
        set => _canonicalMetadata.SetUseNarratorsAsComposers(value);
    }

    public IReadOnlyList<string> Translators
    {
        get => _canonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Translators).Value ?? [];
        set => _canonicalMetadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Translators,
            value?.ToArray() ?? [],
            MetadataOrigin.User);
    }

    public string Publisher
    {
        get => ReadScalar(ProjectMetadataField.Publisher);
        set => SetScalar(ProjectMetadataField.Publisher, value, MetadataOrigin.User);
    }

    public MetadataDate? ReleaseDate
    {
        get => _canonicalMetadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Value;
        set => _canonicalMetadata.SetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate, value, MetadataOrigin.User);
    }

    public int? Year
    {
        get => _canonicalMetadata.GetValue<int?>(ProjectMetadataField.Year).Value;
        set => _canonicalMetadata.SetValue<int?>(ProjectMetadataField.Year, value, MetadataOrigin.User);
    }

    public string License
    {
        get => ReadScalar(ProjectMetadataField.License);
        set => SetScalar(ProjectMetadataField.License, value, MetadataOrigin.User);
    }

    public int? OutputTrackNumber
    {
        get => _canonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value;
        set => _canonicalMetadata.SetValue<int?>(ProjectMetadataField.OutputTrackNumber, value, MetadataOrigin.User);
    }

    public int? OutputTrackTotal
    {
        get => _canonicalMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value;
        set => _canonicalMetadata.SetValue<int?>(ProjectMetadataField.OutputTrackTotal, value, MetadataOrigin.User);
    }

    public string Genre
    {
        get => ReadPrimaryListValue(ProjectMetadataField.Genres);
        set => SetPrimaryListValue(ProjectMetadataField.Genres, value, MetadataOrigin.User);
    }

    public string CoverSourcePath
    {
        get => _coverSourcePath;
        set
        {
            if (!SetPresentationField(ref _coverSourcePath, value))
                return;

            SyncCoverPreviewSource();
            SyncCoverToCanonical(MetadataOrigin.User);
        }
    }

    public string ProcessedCoverPath
    {
        get => _processedCoverPath;
        set
        {
            if (!SetPresentationField(ref _processedCoverPath, value))
                return;

            SyncCoverPreviewSource();
            SyncCoverToCanonical(MetadataOrigin.User);
        }
    }

    public string CoverPreviewSource
    {
        get => _coverPreviewSource;
        set => SetPresentationField(ref _coverPreviewSource, value);
    }

    public void Clear(string defaultGenre = "Audiobook")
    {
        ReplaceCanonicalMetadata(CreateEmptyCanonicalMetadata(defaultGenre, MetadataOrigin.LegacyMigrated));
        CoverSourcePath = "";
        ProcessedCoverPath = "";
        CoverPreviewSource = "";
    }

    public ProjectMetadata EnsureCanonicalMetadata(MetadataOrigin bootstrapOrigin = MetadataOrigin.LegacyMigrated)
    {
        // ProjectMetadataState always owns one canonical instance. The parameter remains for
        // source compatibility with older callers; bootstrap happens when the state is created
        // or explicitly replaced, never by copying compact UI shadow fields.
        _ = bootstrapOrigin;
        return _canonicalMetadata;
    }

    public void ApplyCanonicalMetadata(ProjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (ReferenceEquals(_canonicalMetadata, metadata))
            return;

        ReplaceCanonicalMetadata(metadata);
    }

    public void ApplyCoverMetadata(string? sourcePath, string? processedPath, MetadataOrigin origin)
    {
        SetPresentationField(ref _coverSourcePath, sourcePath, nameof(CoverSourcePath));
        SetPresentationField(ref _processedCoverPath, processedPath, nameof(ProcessedCoverPath));
        SyncCoverPreviewSource();
        _canonicalMetadata.SetCover(_coverSourcePath, _processedCoverPath, origin);
    }

    private void ReplaceCanonicalMetadata(ProjectMetadata metadata)
    {
        _canonicalMetadata.PropertyChanged -= CanonicalMetadata_PropertyChanged;
        _canonicalMetadata = metadata;
        _canonicalMetadata.PropertyChanged += CanonicalMetadata_PropertyChanged;
        RaiseCanonicalProjectionChanged();
    }

    private void CanonicalMetadata_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var propertyName = e.PropertyName switch
        {
            nameof(ProjectMetadataField.Title) => nameof(BookTitle),
            nameof(ProjectMetadataField.Album) => nameof(Album),
            nameof(ProjectMetadataField.Subtitle) => nameof(Subtitle),
            nameof(ProjectMetadataField.OriginalTitle) => nameof(OriginalTitle),
            nameof(ProjectMetadataField.AudioWorkType) => nameof(WorkType),
            nameof(ProjectMetadataField.AbridgedStatus) => nameof(AbridgedStatus),
            nameof(ProjectMetadataField.Series) => nameof(Series),
            nameof(ProjectMetadataField.Volume) => nameof(Volume),
            nameof(ProjectMetadataField.Edition) => nameof(Edition),
            nameof(ProjectMetadataField.Authors) => nameof(Author),
            nameof(ProjectMetadataField.Narrators) => nameof(Narrator),
            nameof(ProjectMetadata.UseNarratorsAsComposers) => nameof(UseNarratorsAsComposers),
            nameof(ProjectMetadataField.Translators) => nameof(Translators),
            nameof(ProjectMetadataField.Publisher) => nameof(Publisher),
            nameof(ProjectMetadataField.ReleaseDate) => nameof(ReleaseDate),
            nameof(ProjectMetadataField.Year) => nameof(Year),
            nameof(ProjectMetadataField.License) => nameof(License),
            nameof(ProjectMetadataField.OutputTrackNumber) => nameof(OutputTrackNumber),
            nameof(ProjectMetadataField.OutputTrackTotal) => nameof(OutputTrackTotal),
            nameof(ProjectMetadataField.Genres) => nameof(Genre),
            nameof(ProjectMetadata.Cover) => nameof(ProjectMetadata.Cover),
            _ => null
        };

        if (propertyName == nameof(ProjectMetadata.Cover))
        {
            ApplyCanonicalCoverToPresentation();
        }
        else if (propertyName is not null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private void RaiseCanonicalProjectionChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanonicalMetadata)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BookTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Album)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subtitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkType)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AbridgedStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Series)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Edition)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Author)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Narrator)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseNarratorsAsComposers)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Translators)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Publisher)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseDate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Year)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(License)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutputTrackNumber)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OutputTrackTotal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Genre)));
        ApplyCanonicalCoverToPresentation();
    }

    private string ReadScalar(ProjectMetadataField field) =>
        _canonicalMetadata.GetValue<string>(field).Value ?? string.Empty;

    private string ReadPrimaryListValue(ProjectMetadataField field) =>
        (_canonicalMetadata.GetValue<IReadOnlyList<string>>(field).Value ?? [])
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))?
            .Trim() ?? string.Empty;

    private void SetScalar(ProjectMetadataField field, string? value, MetadataOrigin origin)
    {
        _canonicalMetadata.SetValue(field, value ?? string.Empty, origin);
    }

    private void SetPrimaryListValue(ProjectMetadataField field, string? value, MetadataOrigin origin)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var existing = _canonicalMetadata.GetValue<IReadOnlyList<string>>(field).Value ?? [];

        var next = new List<string>();
        if (normalized.Length > 0)
            next.Add(normalized);

        foreach (var tail in existing.Skip(1))
        {
            var item = tail?.Trim() ?? string.Empty;
            if (item.Length == 0)
                continue;

            if (normalized.Length > 0 &&
                string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (next.Any(existingItem => string.Equals(existingItem, item, StringComparison.OrdinalIgnoreCase)))
                continue;

            next.Add(item);
        }

        _canonicalMetadata.SetValue<IReadOnlyList<string>>(field, next, origin);
    }

    private bool SetPresentationField(
        ref string field,
        string? value,
        [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (string.Equals(field, normalized, StringComparison.Ordinal))
            return false;

        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void SyncCoverToCanonical(MetadataOrigin origin)
    {
        _canonicalMetadata.SetCover(_coverSourcePath, _processedCoverPath, origin);
    }

    private void ApplyCanonicalCoverToPresentation()
    {
        var cover = _canonicalMetadata.Cover;
        SetPresentationField(ref _coverSourcePath, cover.SourcePath, nameof(CoverSourcePath));
        SetPresentationField(ref _processedCoverPath, cover.ProcessedPath, nameof(ProcessedCoverPath));
        SyncCoverPreviewSource();
    }

    private void SyncCoverPreviewSource()
    {
        var preview = !string.IsNullOrWhiteSpace(_processedCoverPath)
            ? _processedCoverPath
            : _coverSourcePath;
        SetPresentationField(ref _coverPreviewSource, preview, nameof(CoverPreviewSource));
    }

    private static ProjectMetadata CreateEmptyCanonicalMetadata(string defaultGenre, MetadataOrigin origin)
    {
        var metadata = new ProjectMetadata();
        if (!string.IsNullOrWhiteSpace(defaultGenre))
        {
            metadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Genres,
                [defaultGenre.Trim()],
                origin);
        }

        return metadata;
    }
}
