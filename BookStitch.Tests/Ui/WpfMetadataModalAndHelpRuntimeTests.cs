using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_modal_layer_disables_owners_until_each_layer_closes(bool withPreparationLayer)
    {
        RunOnStaThread(() =>
        {
            var main = new Window();
            var preparation = withPreparationLayer ? new Window() : null;
            var metadata = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            var settings = new Window();
            Exception? failure = null;
            bool Enabled(Window window) => IsWindowEnabled(new WindowInteropHelper(window).Handle);

            void CheckMetadataLayer()
            {
                try
                {
                    Assert.False(Enabled(main));
                    if (preparation is not null)
                        Assert.False(Enabled(preparation));
                    Assert.True(Enabled(metadata));
                    settings.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        try
                        {
                            Assert.Same(metadata, settings.Owner);
                            Assert.False(Enabled(main));
                            Assert.False(Enabled(metadata));
                            Assert.True(Enabled(settings));
                        }
                        catch (Exception exception) { failure = exception; }
                        finally { settings.Close(); }
                    }));
                    ModalDialogService.ShowDialog(settings, metadata);
                    Assert.True(Enabled(metadata));
                    Assert.False(Enabled(main));
                    if (preparation is not null)
                        Assert.False(Enabled(preparation));
                }
                catch (Exception exception) { failure ??= exception; }
                finally { CloseWithoutDraftWarning(metadata); }
            }

            void OpenMetadata(Window owner)
            {
                metadata.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(CheckMetadataLayer));
                ModalDialogService.ShowDialog(metadata, owner);
                Assert.Same(owner, metadata.Owner);
                Assert.True(Enabled(owner));
            }

            try
            {
                main.Show();
                if (preparation is null)
                    OpenMetadata(main);
                else
                {
                    preparation.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        try
                        {
                            OpenMetadata(preparation);
                            Assert.False(Enabled(main));
                        }
                        catch (Exception exception) { failure ??= exception; }
                        finally { preparation.Close(); }
                    }));
                    ModalDialogService.ShowDialog(preparation, main);
                }
                Assert.True(Enabled(main));
                Assert.Null(failure);
            }
            finally
            {
                settings.Close();
                CloseWithoutDraftWarning(metadata);
                preparation?.Close();
                main.Close();
            }
        });
    }

    [Fact]
    public void Help_dropdown_shows_evidence_without_adopting_losing_candidate_and_retains_user_values()
    {
        RunOnStaThread(() =>
        {
            var state = new ProjectMetadataState();
            var initialGenres = state.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value?.ToArray() ?? [];
            var dialog = CreateMetadataDialog(state, new MetadataPreferences());
            try
            {
                dialog.Show();
                DrainDispatcher();
                var field = dialog.HelpDemoField;
                var sourceCandidates = field.Candidates.Where(candidate => candidate.HasSourceEvidence).ToArray();
                Assert.Equal([10, 4], sourceCandidates.Select(candidate => candidate.SourceCount));
                Assert.True(sourceCandidates[0].IsAutomaticWinner);
                Assert.False(sourceCandidates[1].IsAutomaticWinner);
                Assert.Contains("★", sourceCandidates[0].EvidenceLabel);
                Assert.DoesNotContain("★", sourceCandidates[1].EvidenceLabel);
                Assert.DoesNotContain(field.Values, value => value.Value == sourceCandidates[1].DisplayValue);
                Assert.Equal(2, field.Candidates.Count(candidate => candidate.EvidenceOrigin == MetadataOrigin.User));

                var example = Assert.IsType<ComboBox>(dialog.FindName("ConceptHelpDropdownExample"));
                Assert.Same(field.Candidates, example.ItemsSource);
                Assert.Equal(180, example.Parent is FrameworkElement border ? border.Width : double.NaN);
                example.SelectedItem = sourceCandidates[1];
                Assert.DoesNotContain(field.Values, value => value.Value == sourceCandidates[1].DisplayValue);

                field.Draft = "My own topic";
                field.CommitDraft();
                var ownValue = Assert.Single(field.Values, value => value.Value == "My own topic");
                field.RemoveValue(ownValue);
                var ownCandidate = Assert.Single(field.Candidates, candidate => candidate.DisplayValue == "My own topic");
                Assert.Equal(MetadataOrigin.User, ownCandidate.EvidenceOrigin);
                Assert.Equal(initialGenres, state.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value ?? []);
            }
            finally { CloseWithoutDraftWarning(dialog); }
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Help_demo_and_dropdown_are_localized_without_changing_candidate_semantics(string language)
    {
        RunOnStaThread(() =>
        {
            var previous = System.Globalization.CultureInfo.CurrentUICulture;
            MetadataDialog? dialog = null;
            try
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
                EnsureApplicationResources();
                dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
                dialog.Show();
                DrainDispatcher();
                var candidates = dialog.HelpDemoField.Candidates;
                Assert.Equal(LocalizationText.Get("MetadataHelpDemoStartAdventure"), candidates[0].DisplayValue);
                Assert.Equal(LocalizationText.Get("MetadataHelpDemoCandidateContinents"), candidates[1].DisplayValue);
                Assert.Equal(LocalizationText.Get("Metadata.Origin.User"), candidates[2].EvidenceLabel);
                Assert.Equal([10, 4, 0, 0], candidates.Select(candidate => candidate.SourceCount));
                var dropdown = Assert.IsType<ComboBox>(dialog.FindName("ConceptHelpDropdownExample"));
                Assert.Equal(LocalizationText.Get("MetadataHelpDropdownTitle"), System.Windows.Automation.AutomationProperties.GetName(dropdown));
                var body = Assert.IsType<string>(dialog.FindResource("MetadataHelpDropdownText"));
                Assert.Contains("★", body);
                Assert.Contains("10×", body);
                Assert.Contains("\n\n", body.Replace("\r\n", "\n"));
            }
            finally
            {
                if (dialog is not null)
                    CloseWithoutDraftWarning(dialog);
                System.Globalization.CultureInfo.CurrentUICulture = previous;
                EnsureApplicationResources();
            }
        });
    }
}
