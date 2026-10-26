using BookStitch.Models;
using BookStitch.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Dialog;

public sealed class ConceptMetadataFieldViewModel : INotifyPropertyChanged
{
    private readonly Func<ProjectMetadata> _currentMetadata;
    private readonly MetadataPreferences _preferences;
    private readonly MetadataNormalizationService _normalization;
    private readonly Action<ProjectMetadata> _saveMetadata;
    private readonly Action<MetadataPreferences> _savePreferences;
    private readonly Action<ProjectMetadataField>? _candidateRefreshRequested;
    private readonly Action<ProjectMetadataField, string>? _candidateValueCommitted;
    private readonly ConceptMetadataFieldDescriptor _descriptor;
    private string _draft = string.Empty;
    private string _committedDraft = string.Empty;
    private ConceptSingleValue? _editedValue;
    private int _editedValueIndex = -1;
    private bool _showTechnicalFieldNames;

    public ConceptMetadataFieldViewModel(
        Func<ProjectMetadata> currentMetadata,
        MetadataPreferences preferences,
        MetadataNormalizationService normalization,
        ProjectMetadataField field,
        ConceptMetadataFieldDescriptor descriptor,
        Action<ProjectMetadata> saveMetadata,
        Action<MetadataPreferences> savePreferences,
        bool isMultiline = false,
        bool isReadOnly = false,
        Action<ProjectMetadataField>? candidateRefreshRequested = null,
        Action<ProjectMetadataField, string>? candidateValueCommitted = null)
    {
        _currentMetadata = currentMetadata ?? throw new ArgumentNullException(nameof(currentMetadata));
        _preferences = preferences;
        _normalization = normalization;
        Field = field;
        _descriptor = descriptor;
        _saveMetadata = saveMetadata;
        _savePreferences = savePreferences;
        _candidateRefreshRequested = candidateRefreshRequested;
        _candidateValueCommitted = candidateValueCommitted;
        IsMultiline = isMultiline;
        IsReadOnly = isReadOnly;
        ValueKind = ProjectMetadataFields.GetValueKind(field);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectMetadataField Field { get; }
    public string Label => _descriptor.GetDisplayName(_showTechnicalFieldNames);
    public ConceptMetadataFieldDescriptor Descriptor => _descriptor;
    public ConceptFieldInfoDescriptor? Info => _descriptor.Info;
    public bool HasInfo => Info is not null;
    public ProjectMetadataFieldValueKind ValueKind { get; }
    public bool IsList => ValueKind == ProjectMetadataFieldValueKind.StringList;
    public bool IsMultiline { get; }
    public bool IsReadOnly { get; }
    public bool IsSingle => !IsList;
    public ConceptFieldInteractionMode InteractionMode => _descriptor.InteractionMode;
    public bool IsPassThrough => InteractionMode == ConceptFieldInteractionMode.PassThrough;
    public bool CanEnterDraft => !IsReadOnly && !IsPassThrough;
    public bool IsEditControlVisible => IsSingle && CanEnterDraft;
    public bool IsRemoveControlVisible => CanEnterDraft;
    public int MaxInputLength => _descriptor.MaxInputLength ?? 0;
    public bool CanEditValues => CanEnterDraft && IsOutputEnabled && !IsLocked;
    public bool CanRemoveValues => CanEnterDraft && !IsLocked;
    public bool CanReorderValues => IsList && CanEditValues && Values.Count > 1;
    public ConceptFieldLockKind LockKind => _descriptor.LockKind;
    public bool IsLockable => LockKind != ConceptFieldLockKind.None;
    public bool IsLocked
    {
        get => LockKind switch
        {
            ConceptFieldLockKind.LanguagePreference => _preferences.LanguagePreferenceLocked,
            ConceptFieldLockKind.GenrePreference => _preferences.GenrePreferenceLocked,
            _ => false
        };
        set
        {
            if (!IsLockable || IsLocked == value)
                return;

            var metadata = CurrentMetadata;
            if (value)
            {
                var values = Values
                    .Select(item => item.Value)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (values.Length == 0)
                    return;

                SetPreferredValues(values);
                SetPreferenceLock(true);
                if (_descriptor.LockControlsOutputPolicy)
                    MetadataPreferencesService.SetPolicy(_preferences, Field, OutputFieldPolicy.Enabled);
                metadata.SetValue<IReadOnlyList<string>>(Field, values, MetadataOrigin.GlobalPreference);
                _saveMetadata(metadata);
            }
            else
            {
                SetPreferenceLock(false);
            }

            _savePreferences(_preferences);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanToggleOutput));
            OnPropertyChanged(nameof(IsOutputEnabled));
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(CanEditValues));
            OnPropertyChanged(nameof(CanRemoveValues));
            OnPropertyChanged(nameof(CanReorderValues));
            OnPropertyChanged(nameof(OriginLabel));
            _candidateRefreshRequested?.Invoke(Field);
        }
    }
    public bool CanToggleOutput => !IsLocked || !_descriptor.LockControlsOutputPolicy;
    public string EditorAutomationName => LocalizationText.Format("Metadata.Automation.Editor", Label);
    public string OutputToggleAutomationName => LocalizationText.Format("Metadata.Automation.Output", Label);
    public string LockToggleAutomationName => LocalizationText.Format("Metadata.Automation.Lock", Label);
    public string ValuesAutomationName => LocalizationText.Format("Metadata.Automation.Values", Label);
    public string EditAutomationName => LocalizationText.Format("Metadata.Automation.EditValue", Label);
    public string RemoveAutomationName => LocalizationText.Format("Metadata.Automation.RemoveValue", Label);
    public bool HasDraft => !string.Equals(Draft, _committedDraft, StringComparison.Ordinal);
    public ObservableCollection<ConceptSingleValue> Values { get; } = [];
    public ObservableCollection<ConceptMetadataCandidate> Candidates { get; } = [];

    public string Draft
    {
        get => _draft;
        set
        {
            var next = IsPassThrough ? string.Empty : value ?? string.Empty;
            if (string.Equals(_draft, next, StringComparison.Ordinal))
                return;
            _draft = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDraft));
        }
    }

    public bool IsOutputEnabled
    {
        get => MetadataPreferencesService.GetPolicy(_preferences, Field) == OutputFieldPolicy.Enabled;
        set
        {
            if (IsLocked && !value && _descriptor.LockControlsOutputPolicy)
                return;

            if (!MetadataPreferencesService.SetPolicy(
                    _preferences,
                    Field,
                    value ? OutputFieldPolicy.Enabled : OutputFieldPolicy.Disabled))
            {
                return;
            }

            _savePreferences(_preferences);
            if (!value)
                DiscardDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(CanEditValues));
            OnPropertyChanged(nameof(CanReorderValues));
        }
    }

    public bool IsEditorVisible =>
        IsOutputEnabled &&
        CanEnterDraft &&
        (!IsLocked || !_descriptor.HideEditorWhenLocked);

    public string OriginLabel => ValueKind switch
    {
        ProjectMetadataFieldValueKind.StringList =>
            MetadataOriginEvidenceLabels.FormatOptional(CurrentMetadata.GetValue<IReadOnlyList<string>>(Field).Origin),
        _ => MetadataOriginEvidenceLabels.FormatOptional(CurrentMetadata.GetValue<string>(Field).Origin)
    };

    public void Refresh()
    {
        var editedValueIndex = _editedValue is null ? -1 : _editedValueIndex;
        Values.Clear();
        if (IsList)
        {
            foreach (var value in CurrentMetadata.GetValue<IReadOnlyList<string>>(Field).Value ?? [])
            {
                var normalized = _normalization.NormalizeText(value);
                if (normalized.Length > 0)
                    Values.Add(new ConceptSingleValue(normalized));
            }
        }
        else
        {
            var value = _normalization.NormalizeText(CurrentMetadata.GetValue<string>(Field).Value);
            if (value.Length > 0)
                Values.Add(new ConceptSingleValue(value));
        }

        if (editedValueIndex >= 0 && editedValueIndex < Values.Count)
        {
            _editedValueIndex = editedValueIndex;
            _editedValue = Values[editedValueIndex];
        }
        else
        {
            _editedValueIndex = -1;
            _editedValue = null;
        }

        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(IsOutputEnabled));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(CanEditValues));
        OnPropertyChanged(nameof(CanRemoveValues));
        OnPropertyChanged(nameof(CanReorderValues));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(CanToggleOutput));
    }

    public void SetTechnicalFieldNameMode(bool showTechnicalFieldNames)
    {
        if (_showTechnicalFieldNames == showTechnicalFieldNames)
            return;

        _showTechnicalFieldNames = showTechnicalFieldNames;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(EditorAutomationName));
        OnPropertyChanged(nameof(OutputToggleAutomationName));
        OnPropertyChanged(nameof(LockToggleAutomationName));
        OnPropertyChanged(nameof(ValuesAutomationName));
        OnPropertyChanged(nameof(EditAutomationName));
        OnPropertyChanged(nameof(RemoveAutomationName));
    }

    public void SetCandidates(IEnumerable<ConceptMetadataCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates.Clear();
        foreach (var candidate in candidates)
        {
            if (candidate.Field != Field)
                throw new ArgumentException($"Candidate field '{candidate.Field}' does not match '{Field}'.", nameof(candidates));
            Candidates.Add(candidate);
        }
    }

    public void CommitDraft()
    {
        if (!CanEditValues)
            return;

        if (IsList && Draft.Contains(';'))
        {
            var parts = Draft.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                Draft = part;
                CommitSingleDraft();
            }
            Draft = string.Empty;
            return;
        }

        CommitSingleDraft();
    }

    private void CommitSingleDraft()
    {
        if (!CanEditValues)
            return;

        var value = _normalization.NormalizeText(Draft);
        if (value.Length == 0)
            return;

        if (Field is ProjectMetadataField.Languages or ProjectMetadataField.OriginalLanguages or ProjectMetadataField.TargetLanguages)
        {
            if (!MetadataPreferencesService.TryCanonicalizeLanguage(value, out value))
                return;
        }

        var metadata = CurrentMetadata;

        if (IsList)
        {
            var key = _normalization.NormalizeForComparison(Field, value);
            var duplicateIndex = Values
                .Select((item, index) => new { item, index })
                .FirstOrDefault(entry => string.Equals(
                    _normalization.NormalizeForComparison(Field, entry.item.Value),
                    key,
                    StringComparison.Ordinal))
                ?.index ?? -1;

            if (_editedValue is not null && _editedValueIndex >= 0)
            {
                var targetIndex = Math.Min(_editedValueIndex, Values.Count - 1);
                var duplicateOutsideEditedValue = duplicateIndex >= 0 && duplicateIndex != targetIndex;
                if (duplicateOutsideEditedValue)
                {
                    FlashDuplicate(Values[duplicateIndex]);
                }
                else
                {
                    Values[targetIndex] = new ConceptSingleValue(value);
                    metadata.UnsuppressValue(Field, value);
                    CommitList(MetadataOrigin.User);
                    _candidateValueCommitted?.Invoke(Field, value);
                }
            }
            else if (duplicateIndex < 0)
            {
                Values.Add(new ConceptSingleValue(value));
                metadata.UnsuppressValue(Field, value);
                CommitList(MetadataOrigin.User);
                _candidateValueCommitted?.Invoke(Field, value);
            }
            else
            {
                var duplicate = Values[duplicateIndex];
                if (!string.Equals(duplicate.Value, value, StringComparison.Ordinal))
                {
                    duplicate = new ConceptSingleValue(value);
                    Values[duplicateIndex] = duplicate;
                    metadata.UnsuppressValue(Field, value);
                    CommitList(MetadataOrigin.User);
                    _candidateValueCommitted?.Invoke(Field, value);
                }

                FlashDuplicate(duplicate);
            }
        }
        else
        {
            var existing = Values.FirstOrDefault();
            if (existing is not null && string.Equals(
                    _normalization.NormalizeForComparison(Field, existing.Value),
                    _normalization.NormalizeForComparison(Field, value),
                    StringComparison.Ordinal))
            {
                if (!string.Equals(existing.Value, value, StringComparison.Ordinal))
                {
                    existing = new ConceptSingleValue(value);
                    Values[0] = existing;
                    metadata.UnsuppressValue(Field, value);
                    metadata.SetValue(Field, value, MetadataOrigin.User);
                    _saveMetadata(metadata);
                    _candidateValueCommitted?.Invoke(Field, value);
                }

                FlashDuplicate(existing);
            }
            else
            {
                Values.Clear();
                Values.Add(new ConceptSingleValue(value));
                metadata.UnsuppressValue(Field, value);
                metadata.SetValue(Field, value, MetadataOrigin.User);
                _saveMetadata(metadata);
                _candidateValueCommitted?.Invoke(Field, value);
            }
            OnPropertyChanged(nameof(OriginLabel));
        }

        Draft = string.Empty;
        _editedValue = null;
        _editedValueIndex = -1;
        _candidateRefreshRequested?.Invoke(Field);
    }

    public void ApplyCandidate(ConceptMetadataCandidate candidate)
    {
        Draft = candidate.ValueToCommit as string ?? candidate.ValueToCommit.ToString() ?? candidate.DisplayValue;
        CommitDraft();
    }

    public void RemoveValue(ConceptSingleValue value)
    {
        if (!CanRemoveValues || !Values.Contains(value))
            return;

        var matching = Candidates.FirstOrDefault(candidate =>
            string.Equals(
                _normalization.NormalizeForComparison(Field, CandidateText(candidate.ValueToCommit)),
                _normalization.NormalizeForComparison(Field, value.Value),
                StringComparison.Ordinal));
        if (matching?.HasSourceEvidence == true)
            CurrentMetadata.SuppressValue(Field, value.Value);

        Values.Remove(value);
        if (IsList)
            CommitList(Values.Count == 0 ? MetadataOrigin.None : MetadataOrigin.User);
        else
        {
            CurrentMetadata.SetValue<string>(Field, null, MetadataOrigin.None);
            _saveMetadata(CurrentMetadata);
            OnPropertyChanged(nameof(OriginLabel));
        }
        _candidateRefreshRequested?.Invoke(Field);
    }

    public void EditValue(ConceptSingleValue value)
    {
        if (!CanEditValues)
            return;
        var index = Values.IndexOf(value);
        if (index < 0)
            return;
        _editedValue = value;
        _editedValueIndex = index;
        Draft = value.Value;
    }

    public void MoveValue(ConceptSingleValue value, int targetIndex)
    {
        if (!CanReorderValues)
            return;

        var sourceIndex = Values.IndexOf(value);
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= Values.Count || sourceIndex == targetIndex)
            return;

        Values.Move(sourceIndex, targetIndex);
        CommitList(MetadataOrigin.User);
    }

    private void CommitList(MetadataOrigin origin)
    {
        var metadata = CurrentMetadata;
        var values = Values.Select(item => item.Value).ToArray();
        var actualOrigin = IsLocked ? MetadataOrigin.GlobalPreference : origin;
        metadata.SetValue<IReadOnlyList<string>>(Field, values, actualOrigin);
        if (IsLocked)
        {
            SetPreferredValues(values);
            _savePreferences(_preferences);
        }
        _saveMetadata(metadata);
        OnPropertyChanged(nameof(OriginLabel));
    }

    public void SetDraftFromCanonical(string? value)
    {
        _committedDraft = value ?? string.Empty;
        Draft = _committedDraft;
    }

    public void DiscardDraft()
    {
        _editedValue = null;
        _editedValueIndex = -1;
        Draft = _committedDraft;
    }

    public void DetachActiveValuesForBinding()
    {
        if (ValueKind is not (ProjectMetadataFieldValueKind.String or ProjectMetadataFieldValueKind.StringList))
            throw new InvalidOperationException($"Field binding activation does not support '{ValueKind}' for '{Field}'.");

        var metadata = CurrentMetadata;
        foreach (var value in Values.Select(item => item.Value).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray())
        {
            _candidateValueCommitted?.Invoke(Field, value);
            metadata.SuppressValue(Field, value);
        }

        Values.Clear();
        if (IsList)
            metadata.SetValue<IReadOnlyList<string>>(Field, [], MetadataOrigin.None);
        else
            metadata.SetValue<string>(Field, null, MetadataOrigin.None);

        _committedDraft = string.Empty;
        Draft = string.Empty;
        _saveMetadata(metadata);
        OnPropertyChanged(nameof(OriginLabel));
        OnPropertyChanged(nameof(CanReorderValues));
        _candidateRefreshRequested?.Invoke(Field);
    }

    private void SetPreferenceLock(bool value)
    {
        switch (LockKind)
        {
            case ConceptFieldLockKind.LanguagePreference:
                _preferences.LanguagePreferenceLocked = value;
                break;
            case ConceptFieldLockKind.GenrePreference:
                _preferences.GenrePreferenceLocked = value;
                break;
        }
    }

    private void SetPreferredValues(IReadOnlyList<string> values)
    {
        switch (LockKind)
        {
            case ConceptFieldLockKind.LanguagePreference:
                _preferences.PreferredLanguages = values.ToList();
                break;
            case ConceptFieldLockKind.GenrePreference:
                _preferences.PreferredGenres = values.ToList();
                break;
        }
    }

    private static string CandidateText(object value) =>
        value as string ?? value.ToString() ?? string.Empty;

    private ProjectMetadata CurrentMetadata => _currentMetadata();

    private async void FlashDuplicate(ConceptSingleValue value)
    {
        var version = ++value.DuplicateHighlightVersion;
        value.IsDuplicateHighlighted = true;
        await Task.Delay(450);
        if (value.DuplicateHighlightVersion == version)
            value.IsDuplicateHighlighted = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
