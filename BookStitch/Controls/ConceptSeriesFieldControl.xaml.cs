using BookStitch.Dialog;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BookStitch.Controls;

public partial class ConceptSeriesFieldControl : UserControl
{
    private bool _candidateKeyboardSelectionArmed;
    private Point _dragStartPoint;
    private ConceptSeriesEntryValue? _dragCandidate;

    public ConceptSeriesFieldControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ColumnGapProperty = DependencyProperty.Register(
        nameof(ColumnGap),
        typeof(GridLength),
        typeof(ConceptSeriesFieldControl),
        new PropertyMetadata(new GridLength(18d)));

    public GridLength ColumnGap
    {
        get => (GridLength)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public ConceptSeriesFieldState? State => DataContext as ConceptSeriesFieldState;
    public ComboBox NameEditor => SeriesNameComboBox;
    public TextBox PartEditor => SeriesPartTextBox;

    public void FocusNameEditorAndSelectAll()
    {
        SeriesNameComboBox.Focus();
        Keyboard.Focus(SeriesNameComboBox);
        SeriesNameComboBox.ApplyTemplate();
        if (SeriesNameComboBox.Template.FindName("PART_EditableTextBox", SeriesNameComboBox) is TextBox textBox)
            textBox.SelectAll();
    }

    private void SeriesNameComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        _candidateKeyboardSelectionArmed = false;
        State?.BeginManualNameInput();
    }

    private void SeriesCandidateItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (State is not { IsSeriesIncluded: true } state ||
            sender is not ComboBoxItem { DataContext: ConceptSeriesEntryCandidate candidate })
        {
            return;
        }

        e.Handled = true;
        SeriesNameComboBox.SelectedItem = null;
        state.ApplyCandidate(candidate);
        SeriesNameComboBox.Text = state.SeriesNameDraft;
        SeriesNameComboBox.IsDropDownOpen = false;
        _candidateKeyboardSelectionArmed = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusPartInput));
    }

    private void SeriesNameComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (State is not { IsSeriesIncluded: true } state || sender is not ComboBox comboBox)
            return;

        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            comboBox.IsDropDownOpen = false;
            _candidateKeyboardSelectionArmed = false;
            FocusPartInput();
            return;
        }

        if (comboBox.IsDropDownOpen && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            _candidateKeyboardSelectionArmed = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        if (comboBox.IsDropDownOpen &&
            _candidateKeyboardSelectionArmed &&
            comboBox.SelectedItem is ConceptSeriesEntryCandidate candidate)
        {
            comboBox.SelectedItem = null;
            state.ApplyCandidate(candidate);
            comboBox.Text = state.SeriesNameDraft;
            comboBox.IsDropDownOpen = false;
        }
        else
        {
            state.ConfirmNameDraft(comboBox.Text);
            comboBox.Text = state.SeriesNameDraft;
            comboBox.SelectedItem = null;
        }

        _candidateKeyboardSelectionArmed = false;
        FocusPartInput();
    }

    private void SeriesPartTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (State is not { IsSeriesIncluded: true } state || e.Key != Key.Enter)
            return;

        e.Handled = true;
        state.CommitDraft();
        FocusNameEditorAndSelectAll();
    }

    private void FocusPartInput()
    {
        SeriesPartTextBox.Focus();
        Keyboard.Focus(SeriesPartTextBox);
        SeriesPartTextBox.CaretIndex = SeriesPartTextBox.Text.Length;
    }

    private void SeriesValueBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        if (State is not { } state ||
            sender is not FrameworkElement element ||
            element.DataContext is not ConceptSeriesEntryValue item ||
            e.OriginalSource is not DependencyObject source ||
            FindVisualAncestor<Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (!state.IsSeriesIncluded)
            {
                e.Handled = true;
                return;
            }

            state.BeginEdit(item);
            FocusNameEditorAndSelectAll();
            e.Handled = true;
            return;
        }

        if (state.SeriesValues.Count < 2)
            return;

        _dragCandidate = item;
        _dragStartPoint = e.GetPosition((IInputElement)sender);
    }

    private void SeriesValueBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (State is not { } state)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = null;
            return;
        }
        if (state.SeriesValues.Count < 2 || _dragCandidate is null)
            return;

        var point = e.GetPosition((IInputElement)sender);
        if (Math.Abs(point.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragCandidate;
        _dragCandidate = null;
        dragged.IsBeingDragged = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(ConceptSeriesEntryValue), dragged), DragDropEffects.Move);
        }
        finally
        {
            dragged.IsBeingDragged = false;
        }
    }

    private void SeriesValues_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = State is { SeriesValues.Count: > 1 } && e.Data.GetDataPresent(typeof(ConceptSeriesEntryValue))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void SeriesValues_Drop(object sender, DragEventArgs e)
    {
        if (State is not { } state ||
            state.SeriesValues.Count < 2 ||
            e.Data.GetData(typeof(ConceptSeriesEntryValue)) is not ConceptSeriesEntryValue dragged)
        {
            return;
        }

        var target = e.OriginalSource is DependencyObject source ? FindSeriesValueFromVisualSource(source) : null;
        var targetIndex = target is null ? state.SeriesValues.Count - 1 : state.SeriesValues.IndexOf(target);
        state.MoveValue(dragged, targetIndex);
        e.Handled = true;
    }

    private void RemoveSeriesValue_Click(object sender, RoutedEventArgs e)
    {
        if (State is { } state && sender is Button { DataContext: ConceptSeriesEntryValue item })
            state.RemoveValue(item);
        e.Handled = true;
    }

    private static ConceptSeriesEntryValue? FindSeriesValueFromVisualSource(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ConceptSeriesEntryValue item })
                return item;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
