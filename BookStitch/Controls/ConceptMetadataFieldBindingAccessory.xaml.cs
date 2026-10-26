using BookStitch.Dialog;
using BookStitch.Presentation;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

public partial class ConceptMetadataFieldBindingAccessory : UserControl
{
    public static readonly DependencyProperty BindingStateProperty = DependencyProperty.Register(
        nameof(BindingState),
        typeof(ConceptMetadataFieldBindingState),
        typeof(ConceptMetadataFieldBindingAccessory),
        new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(ConceptMetadataFieldBindingAccessory),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InfoTitleProperty = DependencyProperty.Register(
        nameof(InfoTitle),
        typeof(string),
        typeof(ConceptMetadataFieldBindingAccessory),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InfoBodyProperty = DependencyProperty.Register(
        nameof(InfoBody),
        typeof(string),
        typeof(ConceptMetadataFieldBindingAccessory),
        new PropertyMetadata(string.Empty));

    public ConceptMetadataFieldBindingAccessory()
    {
        InitializeComponent();
    }

    public ConceptMetadataFieldBindingState? BindingState
    {
        get => (ConceptMetadataFieldBindingState?)GetValue(BindingStateProperty);
        set => SetValue(BindingStateProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string InfoTitle
    {
        get => (string)GetValue(InfoTitleProperty);
        set => SetValue(InfoTitleProperty, value);
    }

    public string InfoBody
    {
        get => (string)GetValue(InfoBodyProperty);
        set => SetValue(InfoBodyProperty, value);
    }

    public static void Configure(
        ConceptMetadataFieldControl control,
        ConceptMetadataFieldViewModel targetField,
        ConceptMetadataFieldBindingState bindingState)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(targetField);
        ArgumentNullException.ThrowIfNull(bindingState);

        var presentation = ConceptMetadataFieldBindingPresentationCatalog.Get(bindingState.BindingId);
        control.DataContext = targetField;
        control.FieldBinding = bindingState;
        var accessory = new ConceptMetadataFieldBindingAccessory
        {
            BindingState = bindingState
        };
        accessory.SetResourceReference(LabelProperty, presentation.LabelResourceKey);
        accessory.SetResourceReference(InfoTitleProperty, presentation.InfoTitleResourceKey);
        accessory.SetResourceReference(InfoBodyProperty, presentation.InfoBodyResourceKey);
        control.HeaderAccessory = accessory;
    }
}
