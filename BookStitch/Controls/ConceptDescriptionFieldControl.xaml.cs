using BookStitch.Dialog;
using BookStitch.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BookStitch.Controls;

public enum ConceptDescriptionPresentationMode
{
    Compact,
    Stretch,
    Minimal
}

public partial class ConceptDescriptionFieldControl : UserControl, IConceptMinimalPresentationParticipant
{
    private bool _suppressNextLostFocusCommit;

    private static readonly DependencyPropertyKey IsMinimalEditorOpenPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsMinimalEditorOpen),
            typeof(bool),
            typeof(ConceptDescriptionFieldControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsMinimalEditorOpenProperty =
        IsMinimalEditorOpenPropertyKey.DependencyProperty;

    public ConceptDescriptionFieldControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PresentationModeProperty = DependencyProperty.Register(
        nameof(PresentationMode),
        typeof(ConceptDescriptionPresentationMode),
        typeof(ConceptDescriptionFieldControl),
        new PropertyMetadata(
            ConceptDescriptionPresentationMode.Compact,
            static (dependencyObject, _) =>
                ((ConceptDescriptionFieldControl)dependencyObject).UpdateMinimalPresentation()));

    public ConceptDescriptionFieldState? State => DataContext as ConceptDescriptionFieldState;
    public TextBox Editor => DescriptionTextBox;
    public ConceptMinimalPresentationScope? MinimalPresentationScope { get; set; }
    public bool HasDraft => State?.HasDraft == true;
    public bool IsMinimalEditorOpen
    {
        get => (bool)GetValue(IsMinimalEditorOpenProperty);
        private set
        {
            if (IsMinimalEditorOpen == value)
                return;
            SetValue(IsMinimalEditorOpenPropertyKey, value);
            MinimalEditorToggleButton.Content = value ? "−" : "+";
            AutomationProperties.SetName(
                MinimalEditorToggleButton,
                LocalizationText.Format(
                    value ? "Metadata.Automation.CloseFieldEditor" : "Metadata.Automation.OpenFieldEditor",
                    State?.DescriptionLabel ?? LocalizationText.Get("Metadata.Field.Description")));
        }
    }

    public ConceptDescriptionPresentationMode PresentationMode
    {
        get => (ConceptDescriptionPresentationMode)GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    public void FocusEditorAndSelectAll()
    {
        DescriptionTextBox.Focus();
        DescriptionTextBox.SelectAll();
    }

    public bool TryOpenMinimalEditor()
    {
        if (PresentationMode != ConceptDescriptionPresentationMode.Minimal ||
            IsMinimalEditorOpen ||
            State?.IsDescriptionIncluded != true)
            return false;

        MinimalPresentationScope?.Activate(this);
        IsMinimalEditorOpen = true;
        DescriptionTextBox.Focus();
        return true;
    }

    public void DiscardDraftAndClose()
    {
        _suppressNextLostFocusCommit = DescriptionTextBox.IsKeyboardFocusWithin;
        State?.DiscardDraft();
        IsMinimalEditorOpen = false;
        MinimalPresentationScope?.Release(this);
    }

    private void MinimalEditorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsMinimalEditorOpen)
            DiscardDraftAndClose();
        else
            TryOpenMinimalEditor();
        e.Handled = true;
    }

    private void MinimalEditorToggleButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsMinimalEditorOpen)
            _suppressNextLostFocusCommit = true;
    }

    private void DescriptionTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (PresentationMode != ConceptDescriptionPresentationMode.Minimal)
        {
            State?.CommitDraft();
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (_suppressNextLostFocusCommit)
                {
                    _suppressNextLostFocusCommit = false;
                    return;
                }

                if (ReferenceEquals(MinimalPresentationScope?.ActiveParticipant, this) && State?.HasDraft == true)
                    State.CommitDraft();
            }));
    }

    private void UpdateMinimalPresentation()
    {
        if (PresentationMode != ConceptDescriptionPresentationMode.Minimal && IsMinimalEditorOpen)
            IsMinimalEditorOpen = false;
    }
}
