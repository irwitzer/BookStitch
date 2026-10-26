using BookStitch.Models;
using BookStitch.Dialog;
using BookStitch.Services;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Threading;

namespace BookStitch.Controls;

public enum ConceptMetadataFieldPresentationMode
{
    Standard,
    Minimal
}

internal sealed record ConceptMetadataEditorDraftState(
    string SemanticDraft,
    string Text,
    int SelectionStart,
    int SelectionLength,
    int CaretIndex,
    bool HadKeyboardFocus,
    object? SelectedValueToCommit,
    bool WasAutomaticSelection);

public partial class ConceptMetadataFieldControl : UserControl, IConceptMinimalPresentationParticipant
{
    private Point _dragStartPoint;
    private ConceptSingleValue? _dragCandidate;
    private bool _suppressSelectionPreview;
    private ConceptMetadataFieldViewModel? _observedField;
    private ConceptMetadataFieldBindingState? _observedBinding;
    private bool _observersAttached;

    private static readonly DependencyPropertyKey IsMinimalEditorOpenPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsMinimalEditorOpen),
            typeof(bool),
            typeof(ConceptMetadataFieldControl),
            new PropertyMetadata(false, OnIsMinimalEditorOpenChanged));

    public static readonly DependencyProperty IsMinimalEditorOpenProperty =
        IsMinimalEditorOpenPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HeaderAccessoryProperty = DependencyProperty.Register(
        nameof(HeaderAccessory),
        typeof(object),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderLabelAccessoryProperty = DependencyProperty.Register(
        nameof(HeaderLabelAccessory),
        typeof(object),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FieldBindingProperty = DependencyProperty.Register(
        nameof(FieldBinding),
        typeof(ConceptMetadataFieldBindingState),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(null, OnFieldBindingChanged));

    public static readonly DependencyProperty PresentationModeProperty = DependencyProperty.Register(
        nameof(PresentationMode),
        typeof(ConceptMetadataFieldPresentationMode),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(
            ConceptMetadataFieldPresentationMode.Standard,
            OnPresentationModeChanged));

    public static readonly DependencyProperty ShowOriginTextProperty = DependencyProperty.Register(
        nameof(ShowOriginText),
        typeof(bool),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(true));

    public static readonly DependencyProperty UseFullWidthProperty = DependencyProperty.Register(
        nameof(UseFullWidth),
        typeof(bool),
        typeof(ConceptMetadataFieldControl),
        new PropertyMetadata(false, OnUseFullWidthChanged));

    public object? HeaderAccessory
    {
        get => GetValue(HeaderAccessoryProperty);
        set => SetValue(HeaderAccessoryProperty, value);
    }

    public object? HeaderLabelAccessory
    {
        get => GetValue(HeaderLabelAccessoryProperty);
        set => SetValue(HeaderLabelAccessoryProperty, value);
    }

    public ConceptMetadataFieldBindingState? FieldBinding
    {
        get => (ConceptMetadataFieldBindingState?)GetValue(FieldBindingProperty);
        set => SetValue(FieldBindingProperty, value);
    }

    public ConceptMetadataFieldPresentationMode PresentationMode
    {
        get => (ConceptMetadataFieldPresentationMode)GetValue(PresentationModeProperty);
        set => SetValue(PresentationModeProperty, value);
    }

    public bool ShowOriginText
    {
        get => (bool)GetValue(ShowOriginTextProperty);
        set => SetValue(ShowOriginTextProperty, value);
    }

    public bool UseFullWidth
    {
        get => (bool)GetValue(UseFullWidthProperty);
        set => SetValue(UseFullWidthProperty, value);
    }

    public bool IsMinimalEditorOpen =>
        (bool)GetValue(IsMinimalEditorOpenProperty);

    public ConceptMinimalPresentationScope? MinimalPresentationScope { get; set; }

    public bool HasDraft =>
        DataContext is ConceptMetadataFieldViewModel { HasDraft: true };

    public ConceptMetadataFieldControl()
    {
        InitializeComponent();
        UpdateWidthMode();
        DataContextChanged += ConceptMetadataFieldControl_DataContextChanged;
        Loaded += ConceptMetadataFieldControl_Loaded;
        Unloaded += ConceptMetadataFieldControl_Unloaded;
    }

    private static void OnUseFullWidthChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ConceptMetadataFieldControl control)
            control.UpdateWidthMode();
    }

    private void UpdateWidthMode()
    {
        var contentWidth = UseFullWidth ? new GridLength(1, GridUnitType.Star) : new GridLength(29, GridUnitType.Star);
        var contextWidth = UseFullWidth ? new GridLength(0) : new GridLength(11, GridUnitType.Star);

        HeaderContentColumn.Width = contentWidth;
        HeaderContextColumn.Width = contextWidth;
        EditorContentColumn.Width = contentWidth;
        EditorContextColumn.Width = contextWidth;
        ValueContentColumn.Width = contentWidth;
        ValueContextColumn.Width = contextWidth;
    }

    private static void OnPresentationModeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ConceptMetadataFieldControl control)
            return;

        if ((ConceptMetadataFieldPresentationMode)e.NewValue != ConceptMetadataFieldPresentationMode.Minimal)
            control.SetMinimalEditorOpen(false);

        control.UpdateMinimalEditorAutomationName();
        control.EnsureMinimalEditorAvailability();
    }

    private static void OnIsMinimalEditorOpenChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ConceptMetadataFieldControl control)
            control.UpdateMinimalEditorAutomationName();
    }

    private static void OnFieldBindingChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ConceptMetadataFieldControl control)
            return;

        if (control._observersAttached)
        {
            control.ObserveBinding(e.OldValue as ConceptMetadataFieldBindingState, attach: false);
            control.ObserveBinding(e.NewValue as ConceptMetadataFieldBindingState, attach: true);
        }

        control.EnsureMinimalEditorAvailability();
    }

    private void ConceptMetadataFieldControl_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_observersAttached)
        {
            ObserveField(e.OldValue as ConceptMetadataFieldViewModel, attach: false);
            ObserveField(e.NewValue as ConceptMetadataFieldViewModel, attach: true);
        }

        ApplyFieldInfo();
        UpdateMinimalEditorAutomationName();
        EnsureMinimalEditorAvailability();
    }

    private void ConceptMetadataFieldControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_observersAttached)
        {
            _observersAttached = true;
            ObserveField(DataContext as ConceptMetadataFieldViewModel, attach: true);
            ObserveBinding(FieldBinding, attach: true);
        }

        ApplyFieldInfo();
        UpdateMinimalEditorAutomationName();
        EnsureMinimalEditorAvailability();
    }

    private void ConceptMetadataFieldControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_observersAttached)
            return;

        ObserveField(_observedField, attach: false);
        ObserveBinding(_observedBinding, attach: false);
        _observersAttached = false;
    }

    private void ObserveField(ConceptMetadataFieldViewModel? field, bool attach)
    {
        if (field is null)
            return;

        if (attach)
        {
            _observedField = field;
            field.PropertyChanged += ObservedField_PropertyChanged;
        }
        else
        {
            field.PropertyChanged -= ObservedField_PropertyChanged;
            if (ReferenceEquals(_observedField, field))
                _observedField = null;
        }
    }

    private void ObserveBinding(ConceptMetadataFieldBindingState? binding, bool attach)
    {
        if (binding is null)
            return;

        if (attach)
        {
            _observedBinding = binding;
            binding.PropertyChanged += ObservedBinding_PropertyChanged;
        }
        else
        {
            binding.PropertyChanged -= ObservedBinding_PropertyChanged;
            if (ReferenceEquals(_observedBinding, binding))
                _observedBinding = null;
        }
    }

    private void ObservedField_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConceptMetadataFieldViewModel.CanEditValues)
            or nameof(ConceptMetadataFieldViewModel.IsEditorVisible))
        {
            EnsureMinimalEditorAvailability();
        }

        if (e.PropertyName == nameof(ConceptMetadataFieldViewModel.Label))
            UpdateMinimalEditorAutomationName();
    }

    private void ObservedBinding_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConceptMetadataFieldBindingState.IsActive))
            EnsureMinimalEditorAvailability();
    }

    private void EnsureMinimalEditorAvailability()
    {
        if (PresentationMode != ConceptMetadataFieldPresentationMode.Minimal)
            return;

        if (FieldBinding?.IsActive == true ||
            DataContext is not ConceptMetadataFieldViewModel { CanEditValues: true, IsEditorVisible: true })
        {
            SetMinimalEditorOpen(false);
        }
    }

    private void UpdateMinimalEditorAutomationName()
    {
        if (MinimalEditorToggleButton is null)
            return;

        var label = (DataContext as ConceptMetadataFieldViewModel)?.Label ?? LocalizationText.Get("Metadata.Automation.GenericField");
        AutomationProperties.SetName(MinimalEditorToggleButton,
            LocalizationText.Format(
                IsMinimalEditorOpen ? "Metadata.Automation.CloseFieldEditor" : "Metadata.Automation.OpenFieldEditor",
                label));
    }

    private void SetMinimalEditorOpen(bool value)
    {
        if (PresentationMode != ConceptMetadataFieldPresentationMode.Minimal)
            value = false;

        SetValue(IsMinimalEditorOpenPropertyKey, value);
        if (!value)
            MinimalPresentationScope?.Release(this);
    }

    public void DiscardDraftAndClose()
    {
        if (DataContext is ConceptMetadataFieldViewModel field)
            field.DiscardDraft();
        CloseMinimalEditor();
    }

    private void CloseMinimalEditor()
    {
        EditorComboBox.IsDropDownOpen = false;
        _suppressSelectionPreview = true;
        try
        {
            using (CandidateComboBoxInteraction.SuppressSelectionFinalization(EditorComboBox))
                EditorComboBox.SelectedItem = null;
        }
        finally
        {
            _suppressSelectionPreview = false;
        }
        SetMinimalEditorOpen(false);
    }

    private void OpenMinimalEditor()
    {
        MinimalPresentationScope?.Activate(this);
        SetMinimalEditorOpen(true);
    }

    private void ApplyFieldInfo()
    {
        if (DataContext is not ConceptMetadataFieldViewModel { Info: { } info })
        {
            FieldInfoPoint.Title = string.Empty;
            FieldInfoPoint.Body = string.Empty;
            return;
        }

        FieldInfoPoint.Title = ResolveInfoText(info.TitleResourceKey);
        FieldInfoPoint.Body = ResolveInfoText(info.BodyResourceKey);
    }

    private string ResolveInfoText(string resourceKey)
    {
        var resource = TryFindResource(resourceKey) ?? Application.Current?.TryFindResource(resourceKey);
        return resource as string
            ?? throw new InvalidOperationException($"Info resource '{resourceKey}' was not found or is not text.");
    }

    internal void PrepareForTransientRemoval()
    {
        if (DataContext is not ConceptMetadataFieldViewModel field)
            return;

        // The shared Field-State is authoritative. Detach the transient Text binding before
        // WPF tears the editor down so an empty/stale target value cannot flow back to Draft.
        BindingOperations.ClearBinding(EditorComboBox, ComboBox.TextProperty);
        EditorComboBox.Text = field.Draft;
    }

    public void FocusEditor()
    {
        if (FieldBinding?.IsActive == true ||
            DataContext is not ConceptMetadataFieldViewModel { IsEditorVisible: true })
        {
            return;
        }

        EditorComboBox.Focus();
        Keyboard.Focus(EditorComboBox);
        EditorComboBox.ApplyTemplate();
        if (GetEditableTextBox() is { } textBox)
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
        }
    }

    public void FocusEditorAndSelectAll()
    {
        FocusEditor();
        EditorComboBox.ApplyTemplate();
        GetEditableTextBox()?.SelectAll();
    }

    internal ConceptMetadataEditorDraftState CaptureCandidateDraftState()
    {
        EditorComboBox.ApplyTemplate();
        var textBox = GetEditableTextBox();
        var text = textBox?.Text ?? EditorComboBox.Text ?? string.Empty;
        var field = DataContext as ConceptMetadataFieldViewModel;
        return new ConceptMetadataEditorDraftState(
            field?.Draft ?? text,
            text,
            textBox?.SelectionStart ?? text.Length,
            textBox?.SelectionLength ?? 0,
            textBox?.CaretIndex ?? text.Length,
            EditorComboBox.IsKeyboardFocusWithin,
            (EditorComboBox.SelectedItem as ConceptMetadataCandidate)?.ValueToCommit,
            CandidateComboBoxInteraction.IsAutomaticSelection(EditorComboBox));
    }

    internal void RestoreCandidateDraftState(ConceptMetadataEditorDraftState state)
    {
        if (DataContext is not ConceptMetadataFieldViewModel field)
            return;

        EditorComboBox.ApplyTemplate();
        var matchingCandidate = state.SelectedValueToCommit is null
            ? null
            : EditorComboBox.Items
                .OfType<ConceptMetadataCandidate>()
                .FirstOrDefault(candidate => CandidateValueEquals(
                    candidate.ValueToCommit,
                    state.SelectedValueToCommit));

        _suppressSelectionPreview = true;
        try
        {
            using (CandidateComboBoxInteraction.SuppressSelectionFinalization(EditorComboBox))
            {
                EditorComboBox.SelectedItem = matchingCandidate;
            }
        }
        finally
        {
            _suppressSelectionPreview = false;
        }

        field.Draft = state.SemanticDraft;
        CandidateComboBoxInteraction.RestoreEditorState(
            EditorComboBox,
            state.Text,
            state.SelectionStart,
            state.SelectionLength,
            state.CaretIndex,
            state.WasAutomaticSelection);

        if (state.HadKeyboardFocus && GetEditableTextBox() is { } textBox)
            textBox.Focus();
    }

    private static bool CandidateValueEquals(object left, object right)
    {
        if (left is string leftText && right is string rightText)
            return string.Equals(leftText, rightText, StringComparison.OrdinalIgnoreCase);

        return Equals(left, right);
    }

    private void EditorComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ConceptMetadataFieldViewModel field)
            return;

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        var selectedCandidate = EditorComboBox.SelectedItem as ConceptMetadataCandidate;
        var wasAutomaticSelection = CandidateComboBoxInteraction.IsAutomaticSelection(EditorComboBox);

        EditorComboBox.IsDropDownOpen = false;
        if (selectedCandidate is not null && !wasAutomaticSelection)
            field.ApplyCandidate(selectedCandidate);
        else
            field.CommitDraft();
        EditorComboBox.SelectedItem = null;

        if (PresentationMode == ConceptMetadataFieldPresentationMode.Minimal &&
            field.IsSingle &&
            field.Values.Count == 1 &&
            string.IsNullOrWhiteSpace(field.Draft))
        {
            SetMinimalEditorOpen(false);
        }
    }

    private void Candidate_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem { DataContext: ConceptMetadataCandidate candidate } ||
            DataContext is not ConceptMetadataFieldViewModel field ||
            field.Field is not (ProjectMetadataField.Genres or ProjectMetadataField.Languages or ProjectMetadataField.OriginalLanguages))
            return;

        // Let the ComboBox finish selecting before applying the value and clearing its draft.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(DataContext, field) || !field.CanEditValues)
                return;
            field.ApplyCandidate(candidate);
            EditorComboBox.IsDropDownOpen = false;
            EditorComboBox.SelectedItem = null;
        }));
    }

    private void EditorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionPreview || CandidateComboBoxInteraction.IsAutomaticSelection(EditorComboBox))
            return;

        if (DataContext is ConceptMetadataFieldViewModel field &&
            EditorComboBox.SelectedItem is ConceptMetadataCandidate candidate)
        {
            field.Draft = candidate.DisplayValue;
        }
    }

    private TextBox? GetEditableTextBox() =>
        EditorComboBox.Template.FindName("PART_EditableTextBox", EditorComboBox) as TextBox;

    private void RemoveValue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConceptMetadataFieldViewModel field &&
            (sender as FrameworkElement)?.DataContext is ConceptSingleValue value)
        {
            field.RemoveValue(value);
            e.Handled = true;
        }
    }

    private void EditValue_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConceptSingleValue value &&
            BeginEditValue(value))
        {
            e.Handled = true;
        }
    }

    private void Badge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 ||
            (sender as FrameworkElement)?.DataContext is not ConceptSingleValue value)
        {
            return;
        }

        if (BeginEditValue(value))
            e.Handled = true;
    }

    internal bool BeginEditValue(ConceptSingleValue value)
    {
        if (FieldBinding?.IsActive == true ||
            DataContext is not ConceptMetadataFieldViewModel { CanEditValues: true } field ||
            !field.Values.Contains(value))
        {
            return false;
        }

        if (PresentationMode == ConceptMetadataFieldPresentationMode.Minimal)
            OpenMinimalEditor();

        field.EditValue(value);
        FocusEditor();
        RestoreExplicitEditDraftAtEnd(field);

        _ = EditorComboBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (PresentationMode == ConceptMetadataFieldPresentationMode.Minimal)
                    FocusEditor();
                RestoreExplicitEditDraftAtEnd(field);
            });
        return true;
    }


    public bool TryOpenMinimalEditor()
    {
        if (PresentationMode != ConceptMetadataFieldPresentationMode.Minimal ||
            IsMinimalEditorOpen ||
            FieldBinding?.IsActive == true ||
            DataContext is not ConceptMetadataFieldViewModel { CanEditValues: true, IsEditorVisible: true } field)
        {
            return false;
        }

        OpenMinimalEditor();
        FocusEditor();
        _ = EditorComboBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () => FocusEditor());
        return true;
    }

    private void MinimalEditorToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresentationMode != ConceptMetadataFieldPresentationMode.Minimal ||
            FieldBinding?.IsActive == true ||
            DataContext is not ConceptMetadataFieldViewModel { CanEditValues: true, IsEditorVisible: true } field)
        {
            return;
        }

        if (IsMinimalEditorOpen)
        {
            field.DiscardDraft();
            CloseMinimalEditor();
            e.Handled = true;
            return;
        }

        OpenMinimalEditor();

        if (field.IsSingle && field.Values.Count == 1)
        {
            if (!BeginEditValue(field.Values[0]))
                SetMinimalEditorOpen(false);
        }
        else
        {
            FocusEditor();
            _ = EditorComboBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                () => FocusEditor());
        }

        e.Handled = true;
    }

    private void RestoreExplicitEditDraftAtEnd(ConceptMetadataFieldViewModel field)
    {
        EditorComboBox.ApplyTemplate();
        var text = field.Draft ?? string.Empty;
        EditorComboBox.Text = text;

        if (GetEditableTextBox() is not { } textBox)
            return;

        textBox.Text = text;
        textBox.SelectionStart = text.Length;
        textBox.SelectionLength = 0;
        textBox.CaretIndex = text.Length;
    }

    private void ValueBadges_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = DataContext is ConceptMetadataFieldViewModel { CanReorderValues: true } &&
                         FindValueFromVisualSource(e.OriginalSource as DependencyObject) is { } value
            ? value
            : null;
        _dragStartPoint = e.GetPosition(ValueBadges);
    }

    private void ValueBadges_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCandidate is { IsDragging: true } dragged)
            dragged.IsDragging = false;
        _dragCandidate = null;
    }

    private void ValueBadges_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(ValueBadges);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _dragCandidate;
        dragged.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop(
                ValueBadges,
                new DataObject(typeof(ConceptSingleValue), dragged),
                DragDropEffects.Move);
        }
        finally
        {
            dragged.IsDragging = false;
            _dragCandidate = null;
        }
    }

    private void ValueBadges_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is ConceptMetadataFieldViewModel { CanReorderValues: true } &&
                    e.Data.GetDataPresent(typeof(ConceptSingleValue))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ValueBadges_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not ConceptMetadataFieldViewModel { CanReorderValues: true } field ||
            e.Data.GetData(typeof(ConceptSingleValue)) is not ConceptSingleValue dragged)
        {
            return;
        }

        var target = FindValueFromVisualSource(e.OriginalSource as DependencyObject);
        field.MoveValue(dragged, target is null ? field.Values.Count - 1 : field.Values.IndexOf(target));
        e.Handled = true;
    }

    private static ConceptSingleValue? FindValueFromVisualSource(DependencyObject? source)
    {
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { DataContext: ConceptSingleValue value })
                return value;
        }

        return null;
    }
}
