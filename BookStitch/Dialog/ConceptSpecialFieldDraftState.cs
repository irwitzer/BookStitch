using BookStitch.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Dialog;

public interface IConceptSpecialMetadataDraft : INotifyPropertyChanged
{
    object? DraftValue { get; set; }
    string DraftText { get; set; }
}

public sealed class ConceptSpecialFieldDraftState : INotifyPropertyChanged
{
    private string _year = string.Empty;
    private string _trackNumber = string.Empty;
    private string _trackCount = string.Empty;
    private AudioWorkType? _workType;
    private AbridgedStatus? _abridgedStatus;
    private IConceptSpecialMetadataDraft? _yearState;
    private IConceptSpecialMetadataDraft? _workTypeState;
    private IConceptSpecialMetadataDraft? _abridgedStatusState;

    public string Year
    {
        get => _yearState?.DraftText ?? _year;
        set
        {
            if (_yearState is not null)
                _yearState.DraftText = value ?? string.Empty;
            else
                SetField(ref _year, value ?? string.Empty);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Year)));
        }
    }
    public string TrackNumber { get => _trackNumber; set => SetField(ref _trackNumber, value ?? string.Empty); }
    public string TrackCount { get => _trackCount; set => SetField(ref _trackCount, value ?? string.Empty); }
    public AudioWorkType? WorkType
    {
        get => _workTypeState is null
            ? _workType
            : _workTypeState.DraftValue is AudioWorkType value ? value : null;
        set
        {
            if (_workTypeState is not null)
                _workTypeState.DraftValue = value;
            else
                SetField(ref _workType, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkType)));
        }
    }
    public AbridgedStatus? AbridgedStatus
    {
        get => _abridgedStatusState is null
            ? _abridgedStatus
            : _abridgedStatusState.DraftValue is AbridgedStatus value ? value : null;
        set
        {
            if (_abridgedStatusState is not null)
                _abridgedStatusState.DraftValue = value;
            else
                SetField(ref _abridgedStatus, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AbridgedStatus)));
        }
    }

    public void Attach(
        IConceptSpecialMetadataDraft yearState,
        IConceptSpecialMetadataDraft workTypeState,
        IConceptSpecialMetadataDraft abridgedStatusState)
    {
        _yearState = yearState;
        _workTypeState = workTypeState;
        _abridgedStatusState = abridgedStatusState;
        _yearState.PropertyChanged += SpecialState_PropertyChanged;
        _workTypeState.PropertyChanged += SpecialState_PropertyChanged;
        _abridgedStatusState.PropertyChanged += SpecialState_PropertyChanged;
        Year = _year;
        WorkType = _workType;
        AbridgedStatus = _abridgedStatus;
        _year = string.Empty;
        _workType = null;
        _abridgedStatus = null;
    }

    private void SpecialState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IConceptSpecialMetadataDraft.DraftText) or nameof(IConceptSpecialMetadataDraft.DraftValue)))
            return;

        if (ReferenceEquals(sender, _yearState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Year)));
        else if (ReferenceEquals(sender, _workTypeState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkType)));
        else if (ReferenceEquals(sender, _abridgedStatusState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AbridgedStatus)));
    }

    public void Clear()
    {
        Year = string.Empty;
        TrackNumber = string.Empty;
        TrackCount = string.Empty;
        WorkType = null;
        AbridgedStatus = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
