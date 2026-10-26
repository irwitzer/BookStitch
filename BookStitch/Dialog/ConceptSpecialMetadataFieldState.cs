using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BookStitch.Dialog;

public sealed class ConceptSpecialMetadataFieldState : INotifyPropertyChanged, IConceptSpecialMetadataDraft
{
    private readonly ProjectMetadataState _metadataState;
    private readonly MetadataPreferences _preferences;
    private readonly Action<ProjectMetadata> _saveMetadata;
    private readonly Action<MetadataPreferences> _savePreferences;
    private readonly ConceptCandidateSessionService? _candidateSession;
    private object? _draftValue;
    private string _draftText = string.Empty;
    private bool _showTechnicalFieldNames;

    public ConceptSpecialMetadataFieldState(
        ProjectMetadataState metadataState,
        MetadataPreferences preferences,
        ProjectMetadataField field,
        Action<ProjectMetadata> saveMetadata,
        Action<MetadataPreferences> savePreferences,
        ConceptCandidateSessionService? candidateSession = null)
    {
        if (field is not (ProjectMetadataField.AudioWorkType or ProjectMetadataField.AbridgedStatus or ProjectMetadataField.Year))
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unsupported special metadata field.");

        _metadataState = metadataState ?? throw new ArgumentNullException(nameof(metadataState));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _saveMetadata = saveMetadata ?? throw new ArgumentNullException(nameof(saveMetadata));
        _savePreferences = savePreferences ?? throw new ArgumentNullException(nameof(savePreferences));
        _candidateSession = candidateSession;
        Field = field;
        RefreshCanonical();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectMetadataField Field { get; }
    public string Label => ConceptMetadataFieldCatalog.Get(Field).GetDisplayName(_showTechnicalFieldNames);
    public string OriginLabel => MetadataOriginEvidenceLabels.FormatOptional(GetOrigin());
    public bool IsEnum => Field is ProjectMetadataField.AudioWorkType or ProjectMetadataField.AbridgedStatus;
    public bool IsYear => Field == ProjectMetadataField.Year;
    public bool IsLockable => Field == ProjectMetadataField.AudioWorkType;
    public string LockToggleAutomationName => LocalizationText.Format("Metadata.Automation.Lock", Label);
    public string OutputToggleAutomationName => LocalizationText.Format("Metadata.Automation.Output", Label);
    public bool IsLocked
    {
        get => IsLockable && _preferences.AudioWorkTypePreferenceLocked;
        set
        {
            if (!IsLockable || value == IsLocked)
                return;
            var current = _metadataState.CanonicalMetadata.GetValue<AudioWorkType?>(Field).Value;
            if (value && current is not (AudioWorkType.Audiobook or AudioWorkType.AudioDrama))
                return;
            _preferences.AudioWorkTypePreferenceLocked = value;
            if (value)
            {
                _preferences.PreferredAudioWorkType = current;
                MetadataPreferencesService.SetPolicy(_preferences, Field, OutputFieldPolicy.Enabled);
                CommitValue(current, MetadataOrigin.GlobalPreference);
            }
            DiscardDraft();
            _savePreferences(_preferences);
            RefreshCanonical();
        }
    }
    public bool CanToggleOutput => !IsLocked;
    public bool CanEditValues => IsOutputEnabled && !IsLocked;
    public bool CanRemoveValues => !IsLocked;
    public bool HasDraft => IsEnum ? DraftValue is not null : !string.IsNullOrWhiteSpace(DraftText);
    public ObservableCollection<ConceptSingleValue> Values { get; } = [];
    public ObservableCollection<ConceptMetadataCandidate> Candidates { get; } = [];

    public object? DraftValue
    {
        get => _draftValue;
        set
        {
            if (Equals(_draftValue, value))
                return;
            _draftValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDraft));
        }
    }

    public string DraftText
    {
        get => _draftText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_draftText, next, StringComparison.Ordinal))
                return;
            _draftText = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDraft));
        }
    }

    public bool IsOutputEnabled
    {
        get => MetadataPreferencesService.GetPolicy(_preferences, Field) == OutputFieldPolicy.Enabled;
        set
        {
            if (IsLocked && !value)
                return;
            if (!MetadataPreferencesService.SetPolicy(
                    _preferences,
                    Field,
                    value ? OutputFieldPolicy.Enabled : OutputFieldPolicy.Disabled))
                return;

            if (!value)
                DiscardDraft();
            _savePreferences(_preferences);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(CanEditValues));
        }
    }

    public bool IsEditorVisible => CanEditValues;

    public void SetCandidates(IEnumerable<ConceptMetadataCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            if (candidate.Field != Field)
                throw new ArgumentException("Candidate field does not match the special field.", nameof(candidates));
            Candidates.Add(candidate);
        }
    }

    public IReadOnlyList<ConceptMetadataCandidate> BuildCandidates(
        IReadOnlyDictionary<ProjectMetadataField, IReadOnlyList<SourceMetadataAlternative>> sourceCandidates)
    {
        if (!IsEnum || _candidateSession is null)
            return [];

        var source = sourceCandidates.TryGetValue(Field, out var candidates)
            ? candidates.OfType<ScalarSourceMetadataAlternative>().ToArray()
            : [];
        var values = Field == ProjectMetadataField.AudioWorkType
            ? ConceptEnumCandidateOrder.AudioWorkTypes.Cast<object>()
            : ConceptEnumCandidateOrder.AbridgedStatuses.Cast<object>();
        var staticCandidates = values.Select(value => new ConceptMetadataCandidate(
            Field,
            value switch
            {
                AudioWorkType workType => MetadataDisplayLabels.AudioWorkType(workType),
                AbridgedStatus status => MetadataDisplayLabels.AbridgedStatus(status),
                _ => value.ToString() ?? string.Empty
            },
            value,
            0,
            MetadataOrigin.StaticList)).ToArray();

        return Field switch
        {
            ProjectMetadataField.AudioWorkType => _candidateSession.PresentValueScalar(
                Field,
                source,
                _metadataState.CanonicalMetadata.GetValue<AudioWorkType?>(Field),
                staticCandidates),
            ProjectMetadataField.AbridgedStatus => _candidateSession.PresentValueScalar(
                Field,
                source,
                _metadataState.CanonicalMetadata.GetValue<AbridgedStatus?>(Field),
                staticCandidates),
            _ => []
        };
    }

    public void RefreshCanonical(bool preserveDraft = true)
    {
        var draftValue = DraftValue;
        var draftText = DraftText;
        Values.Clear();
        var display = GetCanonicalDisplayValue();
        if (!string.IsNullOrWhiteSpace(display))
            Values.Add(new ConceptSingleValue(display));

        if (!preserveDraft)
            DiscardDraft();
        else
        {
            DraftValue = draftValue;
            DraftText = draftText;
        }

        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(IsOutputEnabled));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(CanToggleOutput));
        OnPropertyChanged(nameof(CanEditValues));
        OnPropertyChanged(nameof(CanRemoveValues));
    }

    public bool CommitDraft()
    {
        if (!CanEditValues)
            return false;

        if (IsYear)
        {
            if (DraftText.Length != 4 ||
                !int.TryParse(DraftText, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                return false;
            CommitValue(year, MetadataOrigin.User);
            return true;
        }

        if (DraftValue is null)
            return false;
        CommitValue(DraftValue, MetadataOrigin.User);
        return true;
    }

    public void CommitValue(object? value, MetadataOrigin origin)
    {
        if (IsLocked && origin != MetadataOrigin.GlobalPreference)
            return;
        switch (Field)
        {
            case ProjectMetadataField.AudioWorkType:
                var workType = value is AudioWorkType actualWorkType && actualWorkType != AudioWorkType.Unknown
                    ? actualWorkType
                    : (AudioWorkType?)null;
                _metadataState.CanonicalMetadata.SetValue<AudioWorkType?>(Field, workType, workType.HasValue ? origin : MetadataOrigin.None);
                if (origin == MetadataOrigin.User && workType.HasValue)
                    _candidateSession?.RetainUserValue(Field, workType.Value, MetadataDisplayLabels.AudioWorkType(workType.Value));
                break;
            case ProjectMetadataField.AbridgedStatus:
                var status = value is AbridgedStatus actualStatus && actualStatus != AbridgedStatus.Unknown
                    ? actualStatus
                    : (AbridgedStatus?)null;
                _metadataState.CanonicalMetadata.SetValue<AbridgedStatus?>(Field, status, status.HasValue ? origin : MetadataOrigin.None);
                if (origin == MetadataOrigin.User && status.HasValue)
                    _candidateSession?.RetainUserValue(Field, status.Value, MetadataDisplayLabels.AbridgedStatus(status.Value));
                break;
            case ProjectMetadataField.Year:
                var year = value as int?;
                if (!year.HasValue && origin == MetadataOrigin.None &&
                    _metadataState.CanonicalMetadata.GetValue<int?>(Field).Value is { } removedYear)
                {
                    _metadataState.CanonicalMetadata.SuppressValue(
                        Field, removedYear.ToString(CultureInfo.InvariantCulture));
                }
                else if (year.HasValue && origin == MetadataOrigin.User)
                {
                    _metadataState.CanonicalMetadata.UnsuppressValue(
                        Field, year.Value.ToString(CultureInfo.InvariantCulture));
                }
                _metadataState.CanonicalMetadata.SetValue<int?>(Field, year, year.HasValue ? origin : MetadataOrigin.None);
                break;
        }

        _saveMetadata(_metadataState.CanonicalMetadata);
        DiscardDraft();
        RefreshCanonical();
    }

    public void BeginEdit()
    {
        if (!CanEditValues)
            return;
        switch (Field)
        {
            case ProjectMetadataField.AudioWorkType:
                DraftValue = _metadataState.CanonicalMetadata.GetValue<AudioWorkType?>(Field).Value;
                break;
            case ProjectMetadataField.AbridgedStatus:
                DraftValue = _metadataState.CanonicalMetadata.GetValue<AbridgedStatus?>(Field).Value;
                break;
            case ProjectMetadataField.Year:
                DraftText = _metadataState.CanonicalMetadata.GetValue<int?>(Field).Value?.ToString("0000", CultureInfo.InvariantCulture) ?? string.Empty;
                break;
        }
    }

    public void RemoveValue()
    {
        if (CanRemoveValues)
            CommitValue(null, MetadataOrigin.None);
    }

    public void DiscardDraft()
    {
        DraftValue = null;
        DraftText = string.Empty;
    }

    public void SetTechnicalFieldNameMode(bool showTechnicalFieldNames)
    {
        if (_showTechnicalFieldNames == showTechnicalFieldNames)
            return;
        _showTechnicalFieldNames = showTechnicalFieldNames;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(LockToggleAutomationName));
        OnPropertyChanged(nameof(OutputToggleAutomationName));
    }

    private MetadataOrigin GetOrigin() => Field switch
    {
        ProjectMetadataField.AudioWorkType => _metadataState.CanonicalMetadata.GetValue<AudioWorkType?>(Field).Origin,
        ProjectMetadataField.AbridgedStatus => _metadataState.CanonicalMetadata.GetValue<AbridgedStatus?>(Field).Origin,
        ProjectMetadataField.Year => _metadataState.CanonicalMetadata.GetValue<int?>(Field).Origin,
        _ => MetadataOrigin.None
    };

    private string GetCanonicalDisplayValue() => Field switch
    {
        ProjectMetadataField.AudioWorkType => _metadataState.CanonicalMetadata.GetValue<AudioWorkType?>(Field).Value is { } workType && workType != AudioWorkType.Unknown
            ? MetadataDisplayLabels.AudioWorkType(workType)
            : string.Empty,
        ProjectMetadataField.AbridgedStatus => _metadataState.CanonicalMetadata.GetValue<AbridgedStatus?>(Field).Value is { } status && status != AbridgedStatus.Unknown
            ? MetadataDisplayLabels.AbridgedStatus(status)
            : string.Empty,
        ProjectMetadataField.Year => _metadataState.CanonicalMetadata.GetValue<int?>(Field).Value?.ToString("0000", CultureInfo.InvariantCulture) ?? string.Empty,
        _ => string.Empty
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
