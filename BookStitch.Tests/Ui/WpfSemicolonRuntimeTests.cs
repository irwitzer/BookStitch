using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Setup_cover_removal_records_suppression_before_clearing_cover()
    {
        RunOnStaThread(() =>
        {
            using var folder = new BookStitch.Tests.TestHelpers.TemporaryFolder();
            var path = folder.CreateFile("cover.jpg");
            System.IO.File.WriteAllBytes(path, [1, 2, 3]);
            var metadata = new ProjectMetadataState();
            metadata.ApplyCoverMetadata(path, path, MetadataOrigin.User);
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.Mp3Disc, metadataState: metadata);
            try
            {
                typeof(DiscProjectSetupDialog).GetMethod("RemoveCover_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(setup, [setup, new RoutedEventArgs()]);
                Assert.Empty(metadata.CoverSourcePath);
                Assert.Empty(metadata.ProcessedCoverPath);
                Assert.Single(metadata.CanonicalMetadata.GetSuppressedValues(ProjectMetadataField.Cover));
            }
            finally { setup.Close(); }
        });
    }

    [Fact]
    public void Metadata_dialog_all_editable_list_states_commit_through_control_with_one_enter()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            using var source = CreateKeyboardInputSource("Semicolon editors");
            try
            {
                var fields = typeof(MetadataDialog).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.GetValue(dialog))
                    .SelectMany(value => value is ConceptMetadataFieldViewModel field ? new[] { field } :
                        value is IEnumerable<ConceptMetadataFieldViewModel> collection ? collection : [])
                    .Distinct().Where(field => field.IsList && field.InteractionMode == ConceptFieldInteractionMode.Standard).ToList();
                Assert.NotEmpty(fields);
                var expectedFields = ProjectMetadataFields.OutputFields.Where(field =>
                    ProjectMetadataFields.GetValueKind(field) == ProjectMetadataFieldValueKind.StringList &&
                    ConceptMetadataFieldCatalog.Get(field).InteractionMode == ConceptFieldInteractionMode.Standard);
                Assert.Equal(expectedFields.OrderBy(field => field), fields.Select(field => field.Field).Distinct().OrderBy(field => field));
                foreach (var field in fields)
                {
                    field.IsOutputEnabled = true;
                    var language = field.Field is ProjectMetadataField.Languages or ProjectMetadataField.OriginalLanguages;
                    var first = language ? "de" : "Alpha";
                    var second = language ? "en" : "Beta";
                    var control = new ConceptMetadataFieldControl { DataContext = field };
                    var host = new Window { Content = control, Width = 500, Height = 200 };
                    try
                    {
                        host.Show();
                        DrainDispatcher();
                        var previous = field.Values.Select(value => value.Value).ToArray();
                        control.EditorComboBox.Text = first;
                        RaiseKey(control.EditorComboBox, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                        control.EditorComboBox.Text = $"  {first} ;; {second};{second}; ";
                        RaiseKey(control.EditorComboBox, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                        Assert.Equal(previous.Concat(new[] { first, second }), field.Values.Select(value => value.Value));
                        Assert.True(field.Values.Single(value => value.Value == second).IsDuplicateHighlighted);
                    }
                    finally { host.Close(); }
                }
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Setup_lists_commit_semicolons_but_series_preserves_name_and_number_flow(bool useSetup)
    {
        RunOnStaThread(() =>
        {
            using var source = CreateKeyboardInputSource("Series semicolon editor");
            Window dialog = useSetup ? CreateProjectSetupDialog(ProjectSetupSourceKind.Folder,
                metadataState: new ProjectMetadataState()) : CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            try
            {
                dialog.Show();
                DrainDispatcher();
                if (dialog is DiscProjectSetupDialog setup)
                {
                    foreach (var control in new[] { setup.SetupAuthorField, setup.SetupNarratorField, setup.SetupGenreField })
                    {
                        var field = Assert.IsType<ConceptMetadataFieldViewModel>(control.DataContext);
                        var previous = field.Values.Select(value => value.Value).ToArray();
                        control.EditorComboBox.Text = " Alpha ;; Beta; Beta ; ";
                        RaiseKey(control.EditorComboBox, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                        Assert.Equal(previous.Concat(new[] { "Alpha", "Beta" }), field.Values.Select(value => value.Value));
                    }
                }
                var seriesControl = Assert.IsType<ConceptSeriesFieldControl>(dialog.FindName(useSetup ? "SetupSeriesField" : "ConceptWorkSeriesField"));
                seriesControl.NameEditor.Text = "Alpha;Beta";
                RaiseKey(seriesControl.NameEditor, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                Assert.Empty(seriesControl.State!.SeriesValues);
                seriesControl.PartEditor.Text = "2";
                RaiseKey(seriesControl.PartEditor, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                var entry = Assert.Single(seriesControl.State.SeriesValues);
                Assert.Equal("Alpha;Beta", entry.Name);
                Assert.Equal("2", entry.Part);
            }
            finally { dialog.Close(); }
        });
    }
}
