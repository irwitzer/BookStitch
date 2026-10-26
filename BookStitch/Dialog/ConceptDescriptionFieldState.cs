using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;

namespace BookStitch.Dialog;

public sealed class ConceptDescriptionFieldState : INotifyPropertyChanged
{
    private readonly ProjectMetadataState _metadataState;
    private readonly Action<ProjectMetadata> _saveMetadata;

    public ConceptDescriptionFieldState(
        ProjectMetadataState metadataState,
        ConceptMetadataFieldViewModel field,
        Action<ProjectMetadata> saveMetadata)
    {
        _metadataState = metadataState ?? throw new ArgumentNullException(nameof(metadataState));
        Field = field ?? throw new ArgumentNullException(nameof(field));
        _saveMetadata = saveMetadata ?? throw new ArgumentNullException(nameof(saveMetadata));
        Field.PropertyChanged += Field_PropertyChanged;
        RefreshCanonical(preserveDraft: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConceptMetadataFieldViewModel Field { get; }
    public string DescriptionLabel => Field.Label;
    public string DescriptionOriginLabel => Field.OriginLabel;
    public bool HasDraft => Field.HasDraft;

    public bool IsDescriptionIncluded
    {
        get => Field.IsOutputEnabled;
        set => Field.IsOutputEnabled = value;
    }

    public string DescriptionText
    {
        get => Field.Draft;
        set => Field.Draft = value ?? string.Empty;
    }

    public void CommitDraft()
    {
        var value = Field.Draft?.Trim() ?? string.Empty;
        _metadataState.CanonicalMetadata.SetValue<string>(
            ProjectMetadataField.Description,
            value.Length == 0 ? null : value,
            value.Length == 0 ? MetadataOrigin.None : MetadataOrigin.User);
        _saveMetadata(_metadataState.CanonicalMetadata);
        Field.Refresh();
        Field.SetDraftFromCanonical(value);
        RaiseAll();
    }

    public void RefreshCanonical(bool preserveDraft = true)
    {
        var hadDraft = Field.HasDraft;
        var draft = Field.Draft;
        Field.Refresh();
        if (!preserveDraft || !hadDraft)
        {
            Field.SetDraftFromCanonical(
                _metadataState.CanonicalMetadata.GetValue<string>(ProjectMetadataField.Description).Value);
        }
        else
        {
            Field.Draft = draft;
        }
        RaiseAll();
    }

    public void DiscardDraft()
    {
        Field.DiscardDraft();
        RaiseAll();
    }

    public void SetTechnicalFieldNameMode(bool showTechnicalFieldNames)
    {
        Field.SetTechnicalFieldNameMode(showTechnicalFieldNames);
        RaiseAll();
    }

    private void Field_PropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionOriginLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDescriptionIncluded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DescriptionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDraft)));
    }
}
