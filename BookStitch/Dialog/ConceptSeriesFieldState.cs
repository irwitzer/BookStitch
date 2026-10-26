using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Dialog;

public sealed class ConceptSeriesFieldState : INotifyPropertyChanged
{
    private readonly ProjectMetadataState _metadataState;
    private readonly MetadataPreferences _preferences;
    private readonly MetadataNormalizationService _normalization;
    private readonly Action<ProjectMetadata> _saveMetadata;
    private readonly Action<MetadataPreferences> _savePreferences;
    private readonly Action? _candidateRefreshRequested;
    private readonly SeriesEntryDraftResolver _draftResolver = new();
    private SeriesAutoSplitState? _autoSplitState;
    private bool _draftAllowsAutoSplit = true;
    private bool _showTechnicalFieldNames;
    private string _seriesNameDraft = string.Empty;
    private string _seriesPartDraft = string.Empty;
    private bool _isSyncingCanonical;

    public ConceptSeriesFieldState(
        ProjectMetadataState metadataState,
        MetadataPreferences preferences,
        MetadataNormalizationService normalization,
        Action<ProjectMetadata> saveMetadata,
        Action<MetadataPreferences> savePreferences,
        Action? candidateRefreshRequested = null)
    {
        _metadataState = metadataState ?? throw new ArgumentNullException(nameof(metadataState));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _normalization = normalization ?? throw new ArgumentNullException(nameof(normalization));
        _saveMetadata = saveMetadata ?? throw new ArgumentNullException(nameof(saveMetadata));
        _savePreferences = savePreferences ?? throw new ArgumentNullException(nameof(savePreferences));
        _candidateRefreshRequested = candidateRefreshRequested;
        RefreshCanonical();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConceptSeriesEntryValue> SeriesValues { get; } = [];
    public ObservableCollection<SeriesSourceMetadataAlternative> SeriesCandidates { get; } = [];
    public ObservableCollection<ConceptSeriesEntryCandidate> SeriesEntryCandidates { get; } = [];

    public string SeriesLabel => ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series).GetDisplayName(_showTechnicalFieldNames);
    public string SeriesPartLabel => ConceptMetadataFieldCatalog.Get(ConceptMetadataFieldCatalog.SeriesPartId).GetDisplayName(_showTechnicalFieldNames);
    public string SeriesOriginLabel => MetadataOriginEvidenceLabels.FormatOptional(
        _metadataState.CanonicalMetadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Origin);

    public bool IsSeriesIncluded
    {
        get => MetadataPreferencesService.GetPolicy(_preferences, ProjectMetadataField.Series) == OutputFieldPolicy.Enabled;
        set
        {
            if (IsSeriesIncluded == value)
                return;

            if (!MetadataPreferencesService.SetPolicy(
                    _preferences,
                    ProjectMetadataField.Series,
                    value ? OutputFieldPolicy.Enabled : OutputFieldPolicy.Disabled))
            {
                return;
            }

            _savePreferences(_preferences);
            if (!value)
                DiscardDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSeriesEditorVisible));
        }
    }

    public bool IsSeriesEditorVisible => IsSeriesIncluded;

    public string SeriesNameDraft
    {
        get => _seriesNameDraft;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_seriesNameDraft, next, StringComparison.Ordinal))
                return;
            _seriesNameDraft = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSeriesNameDraft));
            OnPropertyChanged(nameof(HasDraft));
        }
    }

    public string SeriesPartDraft
    {
        get => _seriesPartDraft;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_seriesPartDraft, next, StringComparison.Ordinal))
                return;
            _seriesPartDraft = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSeriesPartDraft));
            OnPropertyChanged(nameof(HasDraft));
        }
    }

    public bool HasSeriesNameDraft => !string.IsNullOrWhiteSpace(SeriesNameDraft);
    public bool HasSeriesPartDraft => !string.IsNullOrWhiteSpace(SeriesPartDraft);
    public bool HasDraft => HasSeriesNameDraft || HasSeriesPartDraft;

    public void SetTechnicalFieldNameMode(bool showTechnicalFieldNames)
    {
        if (_showTechnicalFieldNames == showTechnicalFieldNames)
            return;
        _showTechnicalFieldNames = showTechnicalFieldNames;
        OnPropertyChanged(nameof(SeriesLabel));
        OnPropertyChanged(nameof(SeriesPartLabel));
    }

    public void BeginManualNameInput()
    {
        _autoSplitState = null;
        _draftAllowsAutoSplit = true;
    }

    public void ConfirmNameDraft(string? editorText)
    {
        ApplyDraftResolution(_draftResolver.ResolveUserNameConfirmation(editorText ?? string.Empty, SeriesPartDraft));
        _draftAllowsAutoSplit = true;
    }

    public void ApplyCandidate(ConceptSeriesEntryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ApplyDraftResolution(_draftResolver.LoadStructured(candidate.Name, candidate.Part));
        _draftAllowsAutoSplit = false;
    }

    public bool CommitDraft()
    {
        if (!IsSeriesIncluded)
            return false;

        var resolved = _draftResolver.ResolveForCommit(
            SeriesNameDraft,
            SeriesPartDraft,
            _autoSplitState,
            _draftAllowsAutoSplit);
        var name = _normalization.NormalizeText(resolved.Name);
        if (name.Length == 0)
            return false;

        var part = SeriesPartInputService.NormalizePartText(resolved.Part);
        var comparisonName = _normalization.NormalizeForComparison(ProjectMetadataField.Series, name);
        var duplicate = SeriesValues.FirstOrDefault(item =>
            string.Equals(
                _normalization.NormalizeForComparison(ProjectMetadataField.Series, item.Name),
                comparisonName,
                StringComparison.Ordinal) &&
            string.Equals(
                SeriesPartInputService.NormalizePartText(item.Part),
                part,
                StringComparison.Ordinal));

        if (duplicate is not null)
        {
            FlashDuplicateSeriesValue(duplicate);
            DiscardDraft();
            return false;
        }

        SeriesValues.Add(new ConceptSeriesEntryValue(name, part));
        DiscardDraft();
        CommitCanonicalSeries();
        return true;
    }

    public void BeginEdit(ConceptSeriesEntryValue item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsSeriesIncluded || !SeriesValues.Remove(item))
            return;

        CommitCanonicalSeries();
        ApplyDraftResolution(_draftResolver.LoadStructuredForEdit(item.Name, item.Part));
        _draftAllowsAutoSplit = false;
    }

    public void RemoveValue(ConceptSeriesEntryValue item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (SeriesValues.Remove(item))
            CommitCanonicalSeries();
    }

    public void MoveValue(ConceptSeriesEntryValue item, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (SeriesValues.Count < 2)
            return;
        var sourceIndex = SeriesValues.IndexOf(item);
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= SeriesValues.Count || sourceIndex == targetIndex)
            return;
        SeriesValues.Move(sourceIndex, targetIndex);
        CommitCanonicalSeries();
    }

    public void DiscardDraft()
    {
        _autoSplitState = null;
        _draftAllowsAutoSplit = true;
        SeriesNameDraft = string.Empty;
        SeriesPartDraft = string.Empty;
    }

    public void RefreshCanonical()
    {
        _isSyncingCanonical = true;
        try
        {
            SeriesValues.Clear();
            foreach (var entry in NormalizeEntries(
                         _metadataState.CanonicalMetadata
                             .GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series)
                             .Value ?? []))
            {
                SeriesValues.Add(new ConceptSeriesEntryValue(entry.Name, entry.Part));
            }
        }
        finally
        {
            _isSyncingCanonical = false;
        }

        OnPropertyChanged(nameof(SeriesOriginLabel));
        OnPropertyChanged(nameof(IsSeriesIncluded));
        OnPropertyChanged(nameof(IsSeriesEditorVisible));
    }

    public void SetCandidates(IEnumerable<SeriesSourceMetadataAlternative> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        SeriesCandidates.Clear();
        foreach (var candidate in candidates)
            SeriesCandidates.Add(candidate);

        SeriesEntryCandidates.Clear();
        foreach (var candidate in BuildEntryCandidates(SeriesCandidates))
            SeriesEntryCandidates.Add(candidate);
    }

    public void ClearCandidates()
    {
        SeriesCandidates.Clear();
        SeriesEntryCandidates.Clear();
    }

    public IReadOnlyList<SeriesSourceMetadataAlternative> BuildCandidates(
        IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>> sourceCandidates)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidates);
        var candidates = sourceCandidates.TryGetValue(ProjectMetadataField.Series, out var values)
            ? values.OfType<SeriesSourceMetadataAlternative>().ToArray()
            : [];

        var canonical = NormalizeEntries(
            _metadataState.CanonicalMetadata
                .GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series)
                .Value ?? []);

        var result = candidates
            .Select((candidate, index) => new { candidate, index })
            .Select(item => new
            {
                item.index,
                Candidate = item.candidate with
                {
                    IsCanonicalWinner = SeriesSequencesEqual(item.candidate.Entries, canonical),
                    IsAutomaticWinner = item.candidate.SourceCount > 0 &&
                                        SeriesSequencesEqual(item.candidate.Entries, canonical)
                }
            })
            .ToList();

        var canonicalState = _metadataState.CanonicalMetadata
            .GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series);
        if (canonical.Count > 0 && !result.Any(item => item.Candidate.IsCanonicalWinner) &&
            canonicalState.Origin != MetadataOrigin.None)
        {
            result.Add(new
            {
                index = int.MaxValue,
                Candidate = new SeriesSourceMetadataAlternative(
                    ProjectMetadataField.Series,
                    canonical,
                    1,
                    FormatSeriesDisplay(canonical))
                {
                    EvidenceOrigin = canonicalState.Origin,
                    IsCanonicalWinner = true
                }
            });
        }

        return result
            .OrderByDescending(item => item.Candidate.IsCanonicalWinner)
            .ThenByDescending(item => item.Candidate.SourceCount)
            .ThenBy(item => item.index)
            .Select(item => item.Candidate)
            .ToArray();
    }

    private IReadOnlyList<ConceptSeriesEntryCandidate> BuildEntryCandidates(IEnumerable<SeriesSourceMetadataAlternative> candidates)
    {
        var result = new List<ConceptSeriesEntryCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            foreach (var entry in NormalizeEntries(candidate.Entries))
            {
                var part = _normalization.NormalizeText(entry.Part);
                var key = _normalization.NormalizeForComparison(ProjectMetadataField.Series, entry.Name) + "\u001E" + part;
                if (!seen.Add(key))
                    continue;

                result.Add(new ConceptSeriesEntryCandidate(
                    new SeriesEntry(entry.Name, part),
                    candidate.SourceCount,
                    candidate.EvidenceOrigin,
                    candidate.IsAutomaticWinner));
            }
        }

        return result;
    }

    private IReadOnlyList<SeriesEntry> NormalizeEntries(IEnumerable<SeriesEntry> entries)
    {
        var result = new List<SeriesEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var name = _normalization.NormalizeText(entry.Name);
            var part = _normalization.NormalizeText(entry.Part);
            if (name.Length == 0)
                continue;

            var key = _normalization.NormalizeForComparison(ProjectMetadataField.Series, name) + "\u001E" + part;
            if (!seen.Add(key))
                continue;
            result.Add(new SeriesEntry(name, part));
        }

        return result;
    }

    private bool SeriesSequencesEqual(IReadOnlyList<SeriesEntry> left, IReadOnlyList<SeriesEntry> right) =>
        NormalizeEntries(left).SequenceEqual(NormalizeEntries(right));

    private static string FormatSeriesDisplay(IReadOnlyList<SeriesEntry> entries) =>
        string.Join("; ", entries.Select(entry =>
            string.IsNullOrWhiteSpace(entry.Part) ? entry.Name : $"{entry.Name} #{entry.Part}"));

    private void ApplyDraftResolution(SeriesEntryDraftResolution resolved)
    {
        _autoSplitState = resolved.AutoSplitState;
        SeriesNameDraft = resolved.Name;
        SeriesPartDraft = resolved.Part;
    }

    private void CommitCanonicalSeries(MetadataOrigin origin = MetadataOrigin.User)
    {
        if (_isSyncingCanonical)
            return;

        var entries = NormalizeEntries(SeriesValues.Select(item => new SeriesEntry(item.Name, item.Part)));
        _metadataState.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            entries,
            origin);
        _saveMetadata(_metadataState.CanonicalMetadata);
        OnPropertyChanged(nameof(SeriesOriginLabel));
        _candidateRefreshRequested?.Invoke();
    }

    private async void FlashDuplicateSeriesValue(ConceptSeriesEntryValue seriesValue)
    {
        var version = ++seriesValue.DuplicateHighlightVersion;
        seriesValue.IsDuplicateHighlighted = true;
        await Task.Delay(350);
        if (seriesValue.DuplicateHighlightVersion == version)
            seriesValue.IsDuplicateHighlighted = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ConceptSeriesEntryValue : INotifyPropertyChanged
{
    private bool _isBeingDragged;
    private bool _isDuplicateHighlighted;

    public ConceptSeriesEntryValue(string name, string part)
    {
        Name = name ?? string.Empty;
        Part = part ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; }
    public string Part { get; }
    public bool HasPart => !string.IsNullOrWhiteSpace(Part);
    internal int DuplicateHighlightVersion { get; set; }

    public bool IsDuplicateHighlighted
    {
        get => _isDuplicateHighlighted;
        internal set
        {
            if (_isDuplicateHighlighted == value)
                return;
            _isDuplicateHighlighted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDuplicateHighlighted)));
        }
    }

    public bool IsBeingDragged
    {
        get => _isBeingDragged;
        internal set
        {
            if (_isBeingDragged == value)
                return;
            _isBeingDragged = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBeingDragged)));
        }
    }
}

public sealed class ConceptSeriesEntryCandidate
{
    public ConceptSeriesEntryCandidate(
        SeriesEntry entry,
        int sourceCount,
        MetadataOrigin evidenceOrigin,
        bool isAutomaticWinner)
    {
        Entry = new SeriesEntry(entry.Name ?? string.Empty, entry.Part ?? string.Empty);
        SourceCount = sourceCount;
        EvidenceOrigin = evidenceOrigin;
        IsAutomaticWinner = isAutomaticWinner;
    }

    public SeriesEntry Entry { get; }
    public string Name => Entry.Name;
    public string Part => Entry.Part;
    public int SourceCount { get; }
    public MetadataOrigin EvidenceOrigin { get; }
    public bool IsAutomaticWinner { get; }
    public bool ShowSourceCount => EvidenceOrigin != MetadataOrigin.StaticList;
    public string EvidenceLabel =>
        MetadataOriginEvidenceLabels.Format(EvidenceOrigin) + (IsAutomaticWinner ? " ★" : string.Empty);
    public string DisplayValue => string.IsNullOrWhiteSpace(Part) ? Name : $"{Name} {Part}";
}
