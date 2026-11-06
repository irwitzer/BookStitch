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
    private string _trackNumber = string.Empty;
    private string _trackCount = string.Empty;
    private AudioWorkType? _workType;
    private AbridgedStatus? _abridgedStatus;
    private IConceptSpecialMetadataDraft? _workTypeState;
    private IConceptSpecialMetadataDraft? _abridgedStatusState;

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
        IConceptSpecialMetadataDraft workTypeState,
        IConceptSpecialMetadataDraft abridgedStatusState)
    {
        _workTypeState = workTypeState;
        _abridgedStatusState = abridgedStatusState;
        _workTypeState.PropertyChanged += SpecialState_PropertyChanged;
        _abridgedStatusState.PropertyChanged += SpecialState_PropertyChanged;
        WorkType = _workType;
        AbridgedStatus = _abridgedStatus;
        _workType = null;
        _abridgedStatus = null;
    }

    private void SpecialState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(IConceptSpecialMetadataDraft.DraftText) or nameof(IConceptSpecialMetadataDraft.DraftValue)))
            return;

        if (ReferenceEquals(sender, _workTypeState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkType)));
        else if (ReferenceEquals(sender, _abridgedStatusState))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AbridgedStatus)));
    }

    public void Clear()
    {
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
