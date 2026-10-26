using BookStitch.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BookStitch.Dialog;

public sealed class ConceptMetadataFieldBindingState : INotifyPropertyChanged
{
    private readonly ProjectMetadataFieldBindingDefinition _definition;
    private readonly Func<bool> _getIsActive;
    private readonly Action<bool> _setIsActive;
    private readonly Action? _bindingChanged;

    public ConceptMetadataFieldBindingState(
        ProjectMetadataFieldBindingDefinition definition,
        ConceptMetadataFieldViewModel sourceField,
        ConceptMetadataFieldViewModel targetField,
        Func<bool> getIsActive,
        Action<bool> setIsActive,
        Action? bindingChanged = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        SourceField = sourceField ?? throw new ArgumentNullException(nameof(sourceField));
        TargetField = targetField ?? throw new ArgumentNullException(nameof(targetField));
        _getIsActive = getIsActive ?? throw new ArgumentNullException(nameof(getIsActive));
        _setIsActive = setIsActive ?? throw new ArgumentNullException(nameof(setIsActive));
        _bindingChanged = bindingChanged;

        if (SourceField.Field != _definition.SourceField)
            throw new ArgumentException("Source field does not match the binding definition.", nameof(sourceField));
        if (TargetField.Field != _definition.TargetField)
            throw new ArgumentException("Target field does not match the binding definition.", nameof(targetField));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BindingId => _definition.BindingId;
    public ProjectMetadataField SourceFieldId => _definition.SourceField;
    public ProjectMetadataField TargetFieldId => _definition.TargetField;
    public ConceptMetadataFieldViewModel SourceField { get; }
    public ConceptMetadataFieldViewModel TargetField { get; }

    // This is deliberately the source field's authoritative collection, not a synchronized copy.
    public ObservableCollection<ConceptSingleValue> ProjectedValues => SourceField.Values;

    public bool IsActive
    {
        get => _getIsActive();
        set
        {
            if (_getIsActive() == value)
                return;

            if (value)
                DetachTargetForActiveBinding();

            _setIsActive(value);
            OnPropertyChanged();
            _bindingChanged?.Invoke();
        }
    }

    public void SynchronizeActiveBinding()
    {
        if (IsActive)
            DetachTargetForActiveBinding();

        RefreshActiveState();
    }

    public void RefreshActiveState() => OnPropertyChanged(nameof(IsActive));

    private void DetachTargetForActiveBinding()
    {
        if (TargetField.Values.Count == 0 && string.IsNullOrEmpty(TargetField.Draft))
            return;

        TargetField.DetachActiveValuesForBinding();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
