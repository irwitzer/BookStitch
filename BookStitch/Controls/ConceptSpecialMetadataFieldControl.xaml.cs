using BookStitch.Dialog;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Controls;

public partial class ConceptSpecialMetadataFieldControl : UserControl
{
    public ConceptSpecialMetadataFieldControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateEditorKind();
        Loaded += (_, _) => UpdateEditorKind();
    }

    public ConceptSpecialMetadataFieldState? State => DataContext as ConceptSpecialMetadataFieldState;

    private void Badge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !BeginEdit())
            return;

        e.Handled = true;
    }

    private void EditValue_Click(object sender, RoutedEventArgs e)
    {
        if (BeginEdit())
            e.Handled = true;
    }

    private void RemoveValue_Click(object sender, RoutedEventArgs e)
    {
        State?.RemoveValue();
        e.Handled = true;
    }

    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || State?.CommitDraft() != true)
            return;

        e.Handled = true;
    }

    private void EnumCandidate_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem { DataContext: BookStitch.Services.ConceptMetadataCandidate candidate } ||
            State is not { IsEnum: true } state)
            return;

        // Wait for ComboBox selection to finish before committing and clearing the draft.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(State, state) || !state.CanEditValues)
                return;
            state.DraftValue = candidate.ValueToCommit;
            if (state.CommitDraft())
                EnumEditor.IsDropDownOpen = false;
        }));
    }

    private void YearEditor_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private bool BeginEdit()
    {
        if (State is not { IsEditorVisible: true } state)
            return false;

        state.BeginEdit();
        FocusEditor();
        return true;
    }

    private void FocusEditor()
    {
        if (State?.IsYear == true)
        {
            YearEditor.Focus();
            Keyboard.Focus(YearEditor);
            YearEditor.CaretIndex = YearEditor.Text.Length;
        }
        else
        {
            EnumEditor.Focus();
            Keyboard.Focus(EnumEditor);
        }
    }

    private void UpdateEditorKind()
    {
        EnumEditor.Visibility = State?.IsEnum == true ? Visibility.Visible : Visibility.Collapsed;
        YearEditor.Visibility = State?.IsYear == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
