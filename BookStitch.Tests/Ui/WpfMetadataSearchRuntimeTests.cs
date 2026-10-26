using BookStitch;
using BookStitch.Dialog;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;
using WpfShutdownMode = System.Windows.ShutdownMode;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    public static IEnumerable<object[]> MetadataSearchLocales => UiLanguageService.Choices
        .Where(choice => choice.Id != UiLanguageService.System)
        .Select(choice => new object[] { choice.Id });

    [Theory]
    [MemberData(nameof(MetadataSearchLocales))]
    public void Metadata_search_finds_local_english_and_technical_field_names_in_each_locale(string locale)
    {
        RunOnStaThread(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
            EnsureApplicationResources();
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            try
            {
                var search = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var panel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                var fields = new[] { dialog.TitleField, dialog.AlbumField, dialog.SubtitleField, dialog.VolumeField,
                    dialog.EditionField, dialog.PublisherField, dialog.LicenseField, dialog.AuthorsField,
                    dialog.NarratorsField, dialog.ComposersField, dialog.TranslatorsField, dialog.GenresField }
                    .Concat(dialog.PublicationAdditionalFields).Concat(dialog.LanguageFields)
                    .Concat(dialog.CatalogDataFields).Concat(dialog.SortingFields)
                    .Concat(dialog.DescriptionAdditionalFields).Concat(dialog.PassThroughFields).Distinct().ToArray();
                var specialFields = new[]
                {
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Year), "ConceptQuickAccessYearField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AbridgedStatus), "ConceptQuickAccessAbridgedStatusField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.AudioWorkType), "ConceptQuickAccessWorkTypeField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Description), "ConceptQuickAccessDescriptionField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackNumber), "ConceptWorkTrackNumberField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackTotal), "ConceptWorkTrackCountField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ReleaseDate), "ConceptPublicationReleaseDateField"),
                    (ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series), "ConceptWorkSeriesField"),
                    (ConceptMetadataFieldCatalog.Get(ConceptMetadataFieldCatalog.SeriesPartId), "ConceptWorkSeriesField")
                };

                foreach (var technicalNames in new[] { false, true })
                {
                    dialog.ShowTechnicalFieldNames = technicalNames;
                    foreach (var field in fields)
                    foreach (var query in FieldNameQueries(field.Descriptor))
                    {
                        search.Text = query;
                        DrainDispatcher();
                        Assert.Single(panel.Children.OfType<ConceptMetadataFieldControl>(),
                            control => ReferenceEquals(control.DataContext, field));
                    }
                    foreach (var (descriptor, controlName) in specialFields)
                    foreach (var query in FieldNameQueries(descriptor))
                    {
                        search.Text = query;
                        DrainDispatcher();
                        Assert.Single(panel.Children.Cast<System.Windows.UIElement>(),
                            control => ReferenceEquals(control, dialog.FindName(controlName)));
                    }
                    foreach (var (key, technicalName, controlName) in new[]
                    {
                        ("Metadata.Search.CompatibilityField", "Compatibility Field", "ConceptCompatibilityField"),
                        ("Metadata.Search.DescriptionOutputPreview", "Description Output Preview", "ConceptCompatibilityDescriptionPreview")
                    })
                    foreach (var query in new[] { LocalizationText.Get(key), LocalizationText.Get(key, CultureInfo.GetCultureInfo("en")), technicalName }.Distinct())
                    {
                        search.Text = query;
                        DrainDispatcher();
                        Assert.Single(panel.Children.Cast<System.Windows.UIElement>(),
                            control => ReferenceEquals(control, dialog.FindName(controlName)));
                    }

                    // Exact aliases must keep the intended field first, ahead of partial matches.
                    foreach (var (query, expected) in new (string, object)[]
                    {
                        ("Edition / release", dialog.EditionField),
                        ("Track no.", dialog.FindName("ConceptWorkTrackNumberField")),
                        ("Version", dialog.FindName("ConceptQuickAccessAbridgedStatusField"))
                    })
                    {
                        search.Text = query;
                        var first = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(panel.Children[0]);
                        Assert.True(ReferenceEquals(first, expected) || ReferenceEquals(first.DataContext, expected));
                    }
                }
            }
            finally { CloseWithoutDraftWarning(dialog); }
        }, timeoutSeconds: 90);
    }

    private static IEnumerable<string> FieldNameQueries(ConceptMetadataFieldDescriptor descriptor) =>
        new[] { LocalizationText.Get(descriptor.NameResourceKey),
            LocalizationText.Get(descriptor.NameResourceKey, CultureInfo.GetCultureInfo("en")), descriptor.TechnicalName }.Distinct();

    [Theory]
    [MemberData(nameof(MetadataSearchLocales))]
    public void Metadata_search_preserves_multiscript_values_and_chapter_return_in_each_locale(string locale)
    {
        RunOnStaThread(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
            EnsureApplicationResources();
            var state = new ProjectMetadataState();
            state.CanonicalMetadata.SetValue(ProjectMetadataField.Title, "日本語の物語", MetadataOrigin.User);
            state.CanonicalMetadata.SetValue(ProjectMetadataField.Album, "繁體中文故事", MetadataOrigin.User);
            state.CanonicalMetadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["বাংলা", "𠀀𠀁", "Tiếng Việt"], MetadataOrigin.User);
            var preferences = new MetadataPreferences();
            // Independent title/album fixtures require the existing title-to-album binding to be off.
            MetadataFieldBindingPreferencesService.SetIsEnabled(preferences, ProjectMetadataFieldBindingCatalog.TitleToAlbum, false);
            var dialog = CreateMetadataDialog(state, preferences);
            try
            {
                var search = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var panel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                var workItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptWorkOutputFamilyItem"));
                ((ListBox)dialog.FindName("ConceptFamilyList")).SelectedItem = workItem;
                foreach (var technical in new[] { false, true })
                {
                    dialog.ShowTechnicalFieldNames = technical;
                    foreach (var (query, expected) in new[] { ("物語", dialog.TitleField), ("故事", dialog.AlbumField),
                        ("বাংলা", dialog.AuthorsField), ("𠀀𠀁", dialog.AuthorsField), ("Tieng Viet", dialog.AuthorsField) })
                    {
                        search.Text = query;
                        DrainDispatcher();
                        Assert.Single(panel.Children.OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, expected));
                    }
                    foreach (var query in new[] { "বেলা", "𠀂𠀃" })
                    {
                        search.Text = query;
                        Assert.DoesNotContain(panel.Children.OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.AuthorsField));
                    }
                    foreach (var query in new[] { LocalizationText.Get("UiKapitel"), LocalizationText.Get("UiKapitelvorschlag"),
                        LocalizationText.Get("UiKapitelwarnung"), "chapter" })
                    {
                        search.Text = query;
                        Assert.Equal(System.Windows.Visibility.Visible, ((Grid)dialog.FindName("ConceptChapterPanel")).Visibility);
                    }
                    search.Clear();
                    Assert.Same(workItem, ((ListBox)dialog.FindName("ConceptFamilyList")).SelectedItem);
                }
                Assert.Equal(new[] { "বাংলা", "𠀀𠀁", "Tiếng Việt" }, dialog.AuthorsField.Values.Select(item => item.Value));
            }
            finally { CloseWithoutDraftWarning(dialog); }
        });
    }

    [Fact]
    public void Nonempty_unsearchable_queries_show_no_results_and_clear_restores_previous_group()
    {
        RunOnStaThread(() =>
        {
            var preferences = new MetadataPreferences { RememberLastConceptGroup = true };
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), preferences);
            try
            {
                var familyList = (ListBox)dialog.FindName("ConceptFamilyList");
                var workItem = (ListBoxItem)dialog.FindName("ConceptWorkOutputFamilyItem");
                familyList.SelectedItem = workItem;
                var previousGroup = preferences.LastConceptGroupKey;
                var search = (TextBox)dialog.FindName("ConceptSearchTextBox");
                var panel = (StackPanel)dialog.FindName("ConceptSearchResultsPanel");
                foreach (var query in new[] { "!!!", "😀", "\u0301", new string((char)0xD800, 1) })
                {
                    search.Text = query;
                    DrainDispatcher();
                    Assert.True(dialog.HasConceptSearchText);
                    Assert.Empty(panel.Children.Cast<System.Windows.UIElement>());
                    Assert.Equal(System.Windows.Visibility.Visible, panel.Visibility);
                    Assert.Equal(System.Windows.Visibility.Collapsed, ((Grid)dialog.FindName("ConceptWorkOutputPanel")).Visibility);
                    Assert.Equal(previousGroup, preferences.LastConceptGroupKey);
                    search.Clear();
                    Assert.Same(workItem, familyList.SelectedItem);
                }
            }
            finally { CloseWithoutDraftWarning(dialog); }
        });
    }

    [Fact]
    public void Concept_title_search_view_uses_same_state_without_reparenting_placement_views()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());

            try
            {
                var quickTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));
                var workTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkOutputTitleField"));
                var quickParent = quickTitle.Parent;
                var workParent = workTitle.Parent;
                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));

                searchBox.Text = dialog.TitleField.Label;
                DrainDispatcher();

                var searchTitle = Assert.Single(searchPanel.Children
                    .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.TitleField));
                Assert.Same(dialog.TitleField, searchTitle.DataContext);
                Assert.Same(quickParent, quickTitle.Parent);
                Assert.Same(workParent, workTitle.Parent);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_global_chapter_search_shows_existing_chapter_surface_and_restores_previous_family()
    {
        RunOnStaThread(() =>
        {
            var preferences = new MetadataPreferences { ShowTechnicalConceptFieldNames = false };
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), preferences);

            try
            {
                var familyList = Assert.IsType<ListBox>(dialog.FindName("ConceptFamilyList"));
                var workItem = Assert.IsType<ListBoxItem>(dialog.FindName("ConceptWorkOutputFamilyItem"));
                var workPanel = Assert.IsType<Grid>(dialog.FindName("ConceptWorkOutputPanel"));
                var chapterPanel = Assert.IsType<Grid>(dialog.FindName("ConceptChapterPanel"));
                var chapterGrid = Assert.IsType<DataGrid>(dialog.FindName("ConceptChapterGrid"));
                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                var conceptScrollViewer = Assert.IsType<ScrollViewer>(dialog.FindName("ConceptScrollViewer"));

                familyList.SelectedItem = workItem;
                Assert.Equal(System.Windows.Visibility.Visible, workPanel.Visibility);

                foreach (var technicalNames in new[] { false, true })
                {
                    dialog.ShowTechnicalFieldNames = technicalNames;
                    foreach (var query in new[]
                             {
                                 LocalizationText.Get("UiKapitel"),
                                 LocalizationText.Get("UiKapitelvorschlag"),
                                 LocalizationText.Get("UiKapitelwarnung"),
                                 "chapter"
                             })
                    {
                        searchBox.Text = query;

                        Assert.Equal(System.Windows.Visibility.Visible, chapterPanel.Visibility);
                        Assert.Equal(System.Windows.Visibility.Collapsed, searchPanel.Visibility);
                        Assert.Equal(System.Windows.Visibility.Collapsed, conceptScrollViewer.Visibility);
                        Assert.Same(chapterGrid, dialog.FindName("ConceptChapterGrid"));
                    }
                }

                searchBox.Clear();

                Assert.Equal(System.Windows.Visibility.Collapsed, chapterPanel.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, workPanel.Visibility);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_pass_through_search_and_empty_field_use_shared_output_only_presentation()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue(
                ProjectMetadataField.ContentType,
                "Audiobook",
                MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Composers,
                ["Komponist A"],
                MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Script, "Latn", MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.PhonogramCopyright, "℗ 2026", MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Comment, "Quellkommentar", MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.CatalogNumbers,
                ["KAT-001"],
                MetadataOrigin.SourceAggregation);
            metadataState.CanonicalMetadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Categories,
                ["Fantasy"],
                MetadataOrigin.SourceAggregation);
            var preferences = new MetadataPreferences();
            MetadataFieldBindingPreferencesService.SetIsEnabled(
                preferences,
                ProjectMetadataFieldBindingCatalog.NarratorsToComposers,
                false);
            var dialog = CreateMetadataDialog(metadataState, preferences);

            try
            {
                var contentType = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.ContentType);
                var targetLanguages = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.TargetLanguages);
                var composers = dialog.ComposersField;
                var script = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.Script);
                var phonogram = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.PhonogramCopyright);
                var comment = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.Comment);
                var catalogNumbers = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.CatalogNumbers);
                var categories = Assert.Single(dialog.PassThroughFields, field => field.Field == ProjectMetadataField.Categories);
                Assert.Equal(7, dialog.PassThroughFields.Count);
                Assert.Equal(ProjectMetadataFieldValueKind.StringList, composers.ValueKind);
                Assert.False(composers.IsPassThrough);
                Assert.Equal(["Komponist A"], composers.Values.Select(value => value.Value));
                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));

                foreach (var query in new[] { contentType.Label, "Audiobook" })
                {
                    searchBox.Text = query;
                    DrainDispatcher();

                    var searchContentType = Assert.Single(searchPanel.Children
                        .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, contentType));
                    PrepareControl(searchContentType);

                    Assert.Null(searchContentType.FindName("PassThroughModeBadge"));
                    Assert.True(contentType.HasInfo);
                    var editorHost = Assert.IsType<Border>(searchContentType.FindName("EditorColumnHost"));
                    var valueBadges = Assert.IsType<ItemsControl>(searchContentType.FindName("ValueBadges"));
                    var outputToggle = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(searchContentType.FindName("OutputToggle"));

                    Assert.Same(contentType, searchContentType.DataContext);
                    Assert.Equal(System.Windows.Visibility.Collapsed, editorHost.Visibility);
                    Assert.Single(valueBadges.Items);
                    Assert.True(outputToggle.IsEnabled);
                }

                foreach (var query in new[] { composers.Label, "Composer", "Komponist A" })
                {
                    searchBox.Text = query;
                    DrainDispatcher();
                    var searchComposer = Assert.Single(searchPanel.Children
                        .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, composers));
                    PrepareControl(searchComposer);
                    Assert.False(composers.IsPassThrough);
                    Assert.Equal(
                        System.Windows.Visibility.Visible,
                        Assert.IsType<Border>(searchComposer.FindName("EditorColumnHost")).Visibility);
                    Assert.Single(Assert.IsType<ItemsControl>(searchComposer.FindName("ValueBadges")).Items);
                }


                foreach (var field in new[] { script, phonogram, comment, catalogNumbers, categories })
                {
                    Assert.True(field.IsPassThrough);
                    Assert.False(field.CanEnterDraft);
                    Assert.False(field.CanEditValues);
                    Assert.False(field.CanRemoveValues);
                    Assert.False(field.CanReorderValues);
                    Assert.True(field.IsOutputEnabled);
                    field.IsOutputEnabled = false;
                    Assert.False(field.IsOutputEnabled);
                    field.IsOutputEnabled = true;
                }

                foreach (var (field, query) in new[]
                         {
                             (script, "Latn"),
                             (phonogram, "℗ 2026"),
                             (comment, "Quellkommentar"),
                             (catalogNumbers, "KAT-001"),
                             (categories, "Fantasy")
                         })
                {
                    searchBox.Text = query;
                    DrainDispatcher();
                    var searchField = Assert.Single(searchPanel.Children
                        .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, field));
                    PrepareControl(searchField);
                    Assert.Same(field, searchField.DataContext);
                    Assert.Null(searchField.FindName("PassThroughModeBadge"));
                    Assert.True(field.HasInfo);
                    Assert.Equal(System.Windows.Visibility.Collapsed, Assert.IsType<Border>(searchField.FindName("EditorColumnHost")).Visibility);
                    Assert.Single(Assert.IsType<ItemsControl>(searchField.FindName("ValueBadges")).Items);
                    Assert.True(Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(searchField.FindName("OutputToggle")).IsEnabled);
                }

                Assert.Contains(dialog.CatalogDataFields, field =>
                    field.Field == ProjectMetadataField.Keywords && !field.IsPassThrough);
                foreach (var identifier in new[]
                         {
                             ProjectMetadataField.ISBN,
                             ProjectMetadataField.ASIN,
                             ProjectMetadataField.Barcode,
                             ProjectMetadataField.MusicBrainzReleaseId
                         })
                {
                    Assert.Contains(dialog.CatalogDataFields, field =>
                        field.Field == identifier && !field.IsPassThrough);
                }

                var emptyPassThrough = new ConceptMetadataFieldControl { DataContext = targetLanguages };
                PrepareControl(emptyPassThrough);
                Assert.Null(emptyPassThrough.FindName("PassThroughModeBadge"));
                Assert.True(targetLanguages.HasInfo);
                Assert.Equal(
                    System.Windows.Visibility.Collapsed,
                    Assert.IsType<Border>(emptyPassThrough.FindName("EditorColumnHost")).Visibility);
                Assert.Empty(Assert.IsType<ItemsControl>(emptyPassThrough.FindName("ValueBadges")).Items);
                Assert.True(Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(emptyPassThrough.FindName("OutputToggle")).IsEnabled);

                var quickTitle = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));
                PrepareControl(quickTitle);
                Assert.Null(quickTitle.FindName("PassThroughModeBadge"));
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_source_is_editable_in_publication_and_has_no_visible_technical_group()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue(ProjectMetadataField.Source, "Archiv", MetadataOrigin.SourceAggregation);
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());

            try
            {
                var source = Assert.Single(dialog.PublicationAdditionalFields, field => field.Field == ProjectMetadataField.Source);
                Assert.False(source.IsReadOnly);
                Assert.True(source.IsEditorVisible);
                Assert.Empty(dialog.TechnicalFields);
                Assert.Equal(
                    System.Windows.Visibility.Collapsed,
                    Assert.IsType<ListBoxItem>(dialog.FindName("ConceptTechnicalDataFamilyItem")).Visibility);

                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                searchBox.Text = "Quelle";
                DrainDispatcher();
                var searchSource = Assert.Single(searchPanel.Children
                    .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, source));
                PrepareControl(searchSource);
                Assert.Equal(System.Windows.Visibility.Visible,
                    Assert.IsType<Border>(searchSource.FindName("EditorColumnHost")).Visibility);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Concept_volume_and_search_views_share_descriptor_driven_info_point()
    {
        RunOnStaThread(() =>
        {
            var metadataState = new ProjectMetadataState();
            metadataState.CanonicalMetadata.SetValue(
                ProjectMetadataField.Volume,
                "2",
                MetadataOrigin.SourceAggregation);
            var dialog = CreateMetadataDialog(metadataState, new MetadataPreferences());

            try
            {
                var expectedTitle = Assert.IsType<string>(WpfApplication.Current.FindResource("InfoVolumeTitle"));
                var expectedBody = Assert.IsType<string>(WpfApplication.Current.FindResource("InfoVolumeBody"));
                var volumeControl = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptWorkVolumeField"));
                PrepareControl(volumeControl);

                var volumeInfo = Assert.IsType<InfoPoint>(volumeControl.FindName("FieldInfoPoint"));
                Assert.Equal(System.Windows.Visibility.Visible, volumeInfo.Visibility);
                Assert.Equal(expectedTitle, volumeInfo.Title);
                Assert.Equal(expectedBody, volumeInfo.Body);

                var searchBox = Assert.IsType<TextBox>(dialog.FindName("ConceptSearchTextBox"));
                var searchPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptSearchResultsPanel"));
                searchBox.Text = dialog.VolumeField.Label;
                DrainDispatcher();

                var searchVolume = Assert.Single(searchPanel.Children
                    .OfType<ConceptMetadataFieldControl>(), control => ReferenceEquals(control.DataContext, dialog.VolumeField));
                PrepareControl(searchVolume);
                var searchInfo = Assert.IsType<InfoPoint>(searchVolume.FindName("FieldInfoPoint"));
                Assert.Same(dialog.VolumeField, searchVolume.DataContext);
                Assert.Equal(System.Windows.Visibility.Visible, searchInfo.Visibility);
                Assert.Equal(expectedTitle, searchInfo.Title);
                Assert.Equal(expectedBody, searchInfo.Body);

                var titleControl = Assert.IsType<ConceptMetadataFieldControl>(dialog.FindName("ConceptQuickAccessTitleField"));
                PrepareControl(titleControl);
                Assert.Equal(
                    System.Windows.Visibility.Collapsed,
                    Assert.IsType<InfoPoint>(titleControl.FindName("FieldInfoPoint")).Visibility);
            }
            finally
            {
                dialog.WarnBeforeDiscardingConceptDrafts = false;
                dialog.Close();
            }
        });
    }

}
