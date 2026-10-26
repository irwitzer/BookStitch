using System.Globalization;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Dialog;

public sealed class ConceptMetadataFieldViewModelTests
{
    public static IEnumerable<object[]> EditableListFields => ProjectMetadataFields.OutputFields
        .Where(metadataField => ProjectMetadataFields.GetValueKind(metadataField) == ProjectMetadataFieldValueKind.StringList &&
                        ConceptMetadataFieldCatalog.Get(metadataField).InteractionMode == ConceptFieldInteractionMode.Standard)
        .Select(metadataField => new object[] { metadataField });

    [Theory]
    [MemberData(nameof(EditableListFields))]
    public void SemicolonInput_UsesSingleValueValidationAndDuplicateHandlingForEveryEditableList(ProjectMetadataField fieldId)
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, fieldId, _ => { });
        var language = fieldId is ProjectMetadataField.Languages or ProjectMetadataField.OriginalLanguages;
        var first = language ? "de" : "Anna Example";
        var second = language ? "en" : "Adventure";
        var third = language ? "fr" : "History";
        field.Draft = first;
        field.CommitDraft();

        field.Draft = $"  {first} ; ; {second};{second};;  {third}  ; ";
        field.CommitDraft();

        Assert.Equal(new[] { first, second, third }, field.Values.Select(value => value.Value));
        Assert.Equal(new[] { first, second, third }, metadata.GetValue<IReadOnlyList<string>>(fieldId).Value);
        Assert.Empty(field.Draft);
        Assert.True(field.Values[0].IsDuplicateHighlighted);
        Assert.True(field.Values[1].IsDuplicateHighlighted);
    }

    [Fact]
    public void SemicolonInput_DoesNotSplitScalarFields()
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });
        field.Draft = "Alpha;Beta";
        field.CommitDraft();
        Assert.Equal("Alpha;Beta", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void SemicolonInput_KeepsLanguageValidationAndOutputRestrictions()
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, ProjectMetadataField.Languages, _ => { });
        field.Draft = "de; !!! ; en";
        field.CommitDraft();
        Assert.Equal(new[] { "de", "en" }, field.Values.Select(value => value.Value));
        field.IsOutputEnabled = false;
        field.Draft = "fr;es";
        field.CommitDraft();
        Assert.Equal(new[] { "de", "en" }, field.Values.Select(value => value.Value));
    }

    [Fact]
    public void AccessibilityNamesFollowCurrentUiCultureAndUseTheLocalizedFieldLabel()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var metadata = new ProjectMetadata();
            var field = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });
            var label = field.Label;

            Assert.Equal(LocalizationText.Format("Metadata.Automation.Editor", label), field.EditorAutomationName);
            Assert.Equal(LocalizationText.Format("Metadata.Automation.Output", label), field.OutputToggleAutomationName);
            Assert.Equal(LocalizationText.Format("Metadata.Automation.Lock", label), field.LockToggleAutomationName);
            Assert.Equal(LocalizationText.Format("Metadata.Automation.Values", label), field.ValuesAutomationName);
            Assert.Equal(LocalizationText.Format("Metadata.Automation.EditValue", label), field.EditAutomationName);
            Assert.Equal(LocalizationText.Format("Metadata.Automation.RemoveValue", label), field.RemoveAutomationName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void CommitDraft_UsesTheCurrentCanonicalMetadataInstance()
    {
        var former = new ProjectMetadata();
        var current = former;
        var persisted = new List<ProjectMetadata>();
        var field = CreateField(() => current, ProjectMetadataField.Barcode, persisted.Add);

        current = new ProjectMetadata();
        field.Draft = "978-3-16-148410-0";
        field.CommitDraft();

        Assert.Null(former.GetValue<string>(ProjectMetadataField.Barcode).Value);
        Assert.Equal("978-3-16-148410-0", current.GetValue<string>(ProjectMetadataField.Barcode).Value);
        Assert.Equal([current], persisted);
    }

    [Fact]
    public void Edit_single_value_is_transactional_until_commit()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Alter Titel", MetadataOrigin.User);
        var field = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });

        field.EditValue(Assert.Single(field.Values));

        Assert.Equal("Alter Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Alter Titel", Assert.Single(field.Values).Value);
        field.Draft = "Nicht bestätigt";
        field.DiscardDraft();
        Assert.Equal("Alter Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Alter Titel", Assert.Single(field.Values).Value);
    }

    [Fact]
    public void Edit_list_value_preserves_order_on_discard_and_replaces_in_place_on_commit()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["A", "B", "C"],
            MetadataOrigin.User);
        var field = CreateField(() => metadata, ProjectMetadataField.Authors, _ => { });

        field.EditValue(field.Values[1]);
        field.Draft = "Nicht bestätigt";
        field.DiscardDraft();

        Assert.Equal(["A", "B", "C"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["A", "B", "C"], field.Values.Select(value => value.Value));

        field.EditValue(field.Values[1]);
        field.Draft = "B2";
        field.CommitDraft();

        Assert.Equal(["A", "B2", "C"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["A", "B2", "C"], field.Values.Select(value => value.Value));
    }

    [Fact]
    public void OutputDisabled_DiscardsDraftButKeepsValuesCandidatesAndRemoval()
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, ProjectMetadataField.Keywords, _ => { });
        field.SetCandidates(
        [
            new ConceptMetadataCandidate(
                ProjectMetadataField.Keywords,
                "Quelle",
                "Quelle",
                1,
                MetadataOrigin.SourceAggregation)
        ]);
        field.Draft = "Behalten";
        field.CommitDraft();
        field.Draft = "Verwerfen";

        field.IsOutputEnabled = false;

        Assert.False(field.IsEditorVisible);
        Assert.False(field.HasDraft);
        Assert.Equal("Behalten", Assert.Single(field.Values).Value);
        Assert.NotEmpty(field.Candidates);
        Assert.False(field.CanEditValues);
        Assert.True(field.CanRemoveValues);
        field.EditValue(field.Values[0]);
        Assert.Equal("Behalten", Assert.Single(field.Values).Value);
        field.RemoveValue(field.Values[0]);
        Assert.Empty(field.Values);
    }

    [Theory]
    [InlineData(ProjectMetadataField.Barcode)]
    [InlineData(ProjectMetadataField.Keywords)]
    public void CommitDraft_DuplicateValueDoesNotCreateAnotherBadge(ProjectMetadataField metadataField)
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, metadataField, _ => { });
        field.Draft = "Doppelwert";
        field.CommitDraft();
        field.Draft = "Doppelwert";
        field.CommitDraft();

        var value = Assert.Single(field.Values);
        Assert.True(value.IsDuplicateHighlighted);
    }

    [Fact]
    public void CommitDraft_CaseOnlyListDuplicate_ReplacesInPlaceAndPersistsUserCasing()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Peter", "hallo", "Anna"],
            MetadataOrigin.SourceAggregation);
        var committedCandidates = new List<string>();
        var field = CreateField(
            () => metadata,
            ProjectMetadataField.Authors,
            _ => { },
            (_, value) => committedCandidates.Add(value));

        field.Draft = "Hallo";
        field.CommitDraft();

        Assert.Equal(["Peter", "Hallo", "Anna"], field.Values.Select(item => item.Value));
        Assert.True(field.Values[1].IsDuplicateHighlighted);
        var canonical = metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors);
        Assert.Equal(["Peter", "Hallo", "Anna"], canonical.Value);
        Assert.Equal(MetadataOrigin.User, canonical.Origin);
        Assert.Equal(["Hallo"], committedCandidates);
        Assert.Equal(string.Empty, field.Draft);
    }

    [Fact]
    public void CommitDraft_ExactDuplicate_FlashesExistingValueWithoutReplacingInstance()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Hallo"],
            MetadataOrigin.SourceAggregation);
        var field = CreateField(() => metadata, ProjectMetadataField.Authors, _ => { });
        var existing = Assert.Single(field.Values);

        field.Draft = "Hallo";
        field.CommitDraft();

        Assert.Same(existing, Assert.Single(field.Values));
        Assert.True(existing.IsDuplicateHighlighted);
        Assert.Equal(MetadataOrigin.SourceAggregation,
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Origin);
    }

    [Fact]
    public void CommitDraft_CaseOnlyScalarDuplicate_ReplacesCanonicalValueWithUserCasing()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "hallo", MetadataOrigin.SourceAggregation);
        var field = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });

        field.Draft = "Hallo";
        field.CommitDraft();

        var value = Assert.Single(field.Values);
        Assert.Equal("Hallo", value.Value);
        Assert.True(value.IsDuplicateHighlighted);
        var canonical = metadata.GetValue<string>(ProjectMetadataField.Title);
        Assert.Equal("Hallo", canonical.Value);
        Assert.Equal(MetadataOrigin.User, canonical.Origin);
    }

    [Fact]
    public void CommitDraft_IdentifierComparisonRemainsCaseSensitive()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Barcode, "abc", MetadataOrigin.SourceAggregation);
        var field = CreateField(() => metadata, ProjectMetadataField.Barcode, _ => { });

        field.Draft = "ABC";
        field.CommitDraft();

        var value = Assert.Single(field.Values);
        Assert.Equal("ABC", value.Value);
        Assert.False(value.IsDuplicateHighlighted);
        Assert.Equal("ABC", metadata.GetValue<string>(ProjectMetadataField.Barcode).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Barcode).Origin);
    }

    [Fact]
    public void FieldCatalog_VolumeDeclaresInfoResources_AndTitleHasNoInfo()
    {
        var volume = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Volume);
        var title = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Title);

        var info = Assert.IsType<ConceptFieldInfoDescriptor>(volume.Info);
        Assert.Equal("InfoVolumeTitle", info.TitleResourceKey);
        Assert.Equal("InfoVolumeBody", info.BodyResourceKey);
        Assert.Null(title.Info);
    }

    [Fact]
    public void Source_UsesTheNormalEditableScalarFieldContract()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Source, "Archiv", MetadataOrigin.SourceAggregation);
        var field = CreateField(() => metadata, ProjectMetadataField.Source, _ => { });

        Assert.False(field.IsReadOnly);
        Assert.True(field.IsEditorVisible);
        Assert.True(field.CanEditValues);
        Assert.True(field.CanRemoveValues);
        Assert.Equal(400, field.MaxInputLength);

        field.Draft = "Benutzerquelle";
        field.CommitDraft();

        var canonical = metadata.GetValue<string>(ProjectMetadataField.Source);
        Assert.Equal("Benutzerquelle", canonical.Value);
        Assert.Equal(MetadataOrigin.User, canonical.Origin);

        field.RemoveValue(Assert.Single(field.Values));
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Source).Value);
    }

    [Fact]
    public void LanguageCatalog_ExposesCanonicalChoicesAndKeepsUserInputEditable()
    {
        var expected = new[] { "de", "de-DE", "de-CH", "en", "en-US" };
        Assert.All(expected, value => Assert.Contains(LanguageOptionCatalog.All, option => option.Value == value));

        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, ProjectMetadataField.Languages, _ => { });
        field.SetCandidates(LanguageOptionCatalog.All.Select(option =>
            new ConceptMetadataCandidate(
                ProjectMetadataField.Languages,
                option.DisplayText,
                option.Value,
                0,
                MetadataOrigin.StaticList)));
        field.Draft = "de_CH";
        field.CommitDraft();

        Assert.Equal("de-CH", Assert.Single(field.Values).Value);
        Assert.Contains(
            field.Candidates,
            candidate => candidate.ValueToCommit is string value && value == "de-CH");
    }

    [Theory]
    [InlineData(ProjectMetadataField.Languages)]
    [InlineData(ProjectMetadataField.OriginalLanguages)]
    [InlineData(ProjectMetadataField.TargetLanguages)]
    public void LanguageFields_BuildSharedCandidatesWithoutExceptions(ProjectMetadataField metadataField)
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, metadataField, _ => { });

        var session = new ConceptCandidateSessionService();
        field.SetCandidates(session.PresentList(
            metadataField,
            [
                new ListSourceMetadataAlternative(
                    metadataField,
                    ["de-CH"],
                    1,
                    "de-CH")
            ],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None),
            LanguageOptionCatalog.All.Select(option => new ConceptMetadataCandidate(
                metadataField,
                option.DisplayText,
                option.Value,
                0,
                MetadataOrigin.StaticList))));

        Assert.NotEmpty(field.Candidates);
        Assert.Contains(field.Candidates, candidate =>
            candidate.ValueToCommit is string value &&
            value == "de-CH" &&
            candidate.EvidenceOrigin == MetadataOrigin.SourceAggregation);
        Assert.Equal(metadataField == ProjectMetadataField.Languages, field.IsLockable);
    }

    [Fact]
    public void MultiValue_ReorderUpdatesTheCanonicalOrderAndOutputOffBlocksIt()
    {
        var metadata = new ProjectMetadata();
        var field = CreateField(() => metadata, ProjectMetadataField.Keywords, _ => { });
        field.Draft = "Erste";
        field.CommitDraft();
        field.Draft = "Zweite";
        field.CommitDraft();

        field.MoveValue(field.Values[1], 0);

        Assert.Equal(["Zweite", "Erste"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Keywords).Value);
        field.IsOutputEnabled = false;
        field.MoveValue(field.Values[1], 0);
        Assert.Equal(["Zweite", "Erste"], field.Values.Select(value => value.Value));
    }

    [Fact]
    public void LanguageLock_ReusesPreferredLanguageSettingsAndPreservesCurrentValueOnUnlock()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.Languages,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Languages),
            _ => { },
            _ => { });
        field.Draft = "de-CH";
        field.CommitDraft();

        field.IsLocked = true;

        Assert.True(preferences.LanguagePreferenceLocked);
        Assert.Equal(["de-CH"], preferences.PreferredLanguages);
        Assert.Equal(MetadataOrigin.GlobalPreference,
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Origin);
        Assert.True(field.IsOutputEnabled);

        field.IsLocked = false;

        Assert.False(preferences.LanguagePreferenceLocked);
        Assert.Equal(["de-CH"], field.Values.Select(value => value.Value));
    }

    [Fact]
    public void LongScalarAndMultiValuesRemainUntruncatedInStateAndCanonicalMetadata()
    {
        var metadata = new ProjectMetadata();
        var title = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });
        var authors = CreateField(() => metadata, ProjectMetadataField.Authors, _ => { });
        var longTitle = new string('T', 420);
        var longAuthor = new string('A', 360);

        title.Draft = longTitle;
        title.CommitDraft();
        authors.Draft = longAuthor;
        authors.CommitDraft();
        authors.Draft = "Zweiter Autor";
        authors.CommitDraft();
        authors.MoveValue(authors.Values[1], 0);

        Assert.Equal(longTitle, Assert.Single(title.Values).Value);
        Assert.Equal(longTitle, metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Contains(longAuthor, authors.Values.Select(value => value.Value));
        Assert.Contains(longAuthor, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value!);
        Assert.Equal("Zweiter Autor", authors.Values[0].Value);
    }

    [Fact]
    public void LockableFieldWithoutValues_RejectsLockWithoutPersistingPreferencesOrMetadata()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var metadataSaveCount = 0;
        var preferenceSaveCount = 0;
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.Genres,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Genres),
            _ => metadataSaveCount++,
            _ => preferenceSaveCount++);

        Assert.Empty(field.Values);

        field.IsLocked = true;

        Assert.False(field.IsLocked);
        Assert.False(preferences.GenrePreferenceLocked);
        Assert.Empty(preferences.PreferredGenres);
        Assert.Equal(0, metadataSaveCount);
        Assert.Equal(0, preferenceSaveCount);
        Assert.Equal(MetadataOrigin.None, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);
    }


    [Fact]
    public void GenreLock_UsesSharedLockCapabilityAndBlocksEditingUntilUnlock()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var savedMetadata = new List<ProjectMetadata>();
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.Genres,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Genres),
            savedMetadata.Add,
            _ => { });
        field.Draft = "Fantasy";
        field.CommitDraft();
        field.Draft = "Krimi";
        field.CommitDraft();

        field.IsLocked = true;

        Assert.Equal(ConceptFieldLockKind.GenrePreference, field.LockKind);
        Assert.True(field.IsLocked);
        Assert.True(preferences.GenrePreferenceLocked);
        Assert.Equal(["Fantasy", "Krimi"], preferences.PreferredGenres);
        Assert.Equal(MetadataOrigin.GlobalPreference,
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);
        Assert.True(field.IsOutputEnabled);
        Assert.True(field.CanToggleOutput);
        Assert.False(field.IsEditorVisible);
        Assert.False(field.CanEditValues);
        Assert.False(field.CanRemoveValues);
        Assert.False(field.CanReorderValues);

        var lockedValues = field.Values.Select(value => value.Value).ToArray();
        field.RemoveValue(field.Values[0]);
        Assert.Equal(lockedValues, field.Values.Select(value => value.Value));

        field.IsLocked = false;

        Assert.False(field.IsLocked);
        Assert.False(preferences.GenrePreferenceLocked);
        Assert.True(field.CanEditValues);
        Assert.Equal(["Fantasy", "Krimi"], field.Values.Select(value => value.Value));
        Assert.NotEmpty(savedMetadata);
    }


    [Fact]
    public void GenreLock_AndOutputPolicy_AreIndependentInAllReachableStates()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.Genres,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Genres),
            _ => { },
            _ => { });
        field.Draft = "Fantasy";
        field.CommitDraft();

        Assert.True(field.IsOutputEnabled);
        Assert.True(field.IsEditorVisible);

        field.IsOutputEnabled = false;
        Assert.False(field.IsOutputEnabled);
        field.IsLocked = true;
        Assert.True(field.IsLocked);
        Assert.False(field.IsOutputEnabled);
        Assert.False(field.IsEditorVisible);
        Assert.True(field.CanToggleOutput);

        field.IsOutputEnabled = true;
        Assert.True(field.IsOutputEnabled);
        Assert.True(field.IsLocked);
        Assert.False(field.IsEditorVisible);

        field.IsOutputEnabled = false;
        Assert.False(field.IsOutputEnabled);
        Assert.True(field.IsLocked);

        field.IsLocked = false;
        Assert.False(field.IsLocked);
        Assert.False(field.IsOutputEnabled);
        Assert.False(field.IsEditorVisible);

        field.IsOutputEnabled = true;
        Assert.True(field.IsOutputEnabled);
        Assert.True(field.IsEditorVisible);
    }


    [Fact]
    public void PassThroughScalar_PreservesCanonicalUserValueAndAllowsOnlyOutputPolicy()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.ContentType, "Custom", MetadataOrigin.User);
        var preferences = new MetadataPreferences();
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.ContentType,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ContentType),
            _ => { },
            _ => { });

        Assert.True(field.IsPassThrough);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough, field.InteractionMode);
        Assert.Equal("Custom", Assert.Single(field.Values).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.ContentType).Origin);
        Assert.False(field.IsEditorVisible);
        Assert.False(field.CanEnterDraft);
        Assert.False(field.CanEditValues);
        Assert.False(field.CanRemoveValues);
        Assert.False(field.IsEditControlVisible);
        Assert.False(field.IsRemoveControlVisible);

        field.Draft = "Ignored";
        Assert.False(field.HasDraft);
        Assert.Equal(string.Empty, field.Draft);

        field.IsOutputEnabled = false;
        Assert.False(field.IsOutputEnabled);
        Assert.Equal("Custom", Assert.Single(field.Values).Value);
        Assert.Equal("Custom", metadata.GetValue<string>(ProjectMetadataField.ContentType).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.ContentType).Origin);

        field.IsOutputEnabled = true;
        Assert.True(field.IsOutputEnabled);
        Assert.Equal("Custom", Assert.Single(field.Values).Value);
    }

    [Fact]
    public void PassThroughList_PreservesValuesCandidatesAndDisablesRemovalAndReorder()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.TargetLanguages,
            ["de", "en"],
            MetadataOrigin.SourceAggregation);
        var field = CreateField(() => metadata, ProjectMetadataField.TargetLanguages, _ => { });
        field.SetCandidates(
        [
            new ConceptMetadataCandidate(
                ProjectMetadataField.TargetLanguages,
                "Deutsch",
                "de",
                2,
                MetadataOrigin.SourceAggregation)
        ]);

        Assert.True(field.IsPassThrough);
        Assert.Equal(["de", "en"], field.Values.Select(value => value.Value));
        Assert.NotEmpty(field.Candidates);
        Assert.False(field.IsEditorVisible);
        Assert.False(field.CanRemoveValues);
        Assert.False(field.CanReorderValues);
        Assert.False(field.IsRemoveControlVisible);

        field.RemoveValue(field.Values[0]);
        field.MoveValue(field.Values[1], 0);

        Assert.Equal(["de", "en"], field.Values.Select(value => value.Value));
        Assert.Equal(["de", "en"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.TargetLanguages).Value);
    }

    [Fact]
    public void NormalTextDescriptors_DefaultTo400Characters_WithoutLimitingSpecialFields()
    {
        Assert.Equal(400, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Title).MaxInputLength);
        Assert.Equal(400, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Authors).MaxInputLength);
        Assert.Equal(400, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ISBN).MaxInputLength);
        Assert.Null(ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Description).MaxInputLength);
        Assert.Null(ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ReleaseDate).MaxInputLength);
        Assert.Null(ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackNumber).MaxInputLength);
    }

    [Theory]
    [InlineData(MetadataOrigin.User)]
    [InlineData(MetadataOrigin.StaticList)]
    public void NonSourceCandidate_RemoveDoesNotCreateSourceSuppression(MetadataOrigin evidenceOrigin)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Barcode, "4000000000041", MetadataOrigin.User);
        var field = CreateField(() => metadata, ProjectMetadataField.Barcode, _ => { });
        field.SetCandidates(
        [
            new ConceptMetadataCandidate(
                ProjectMetadataField.Barcode,
                "4000000000041",
                "4000000000041",
                0,
                evidenceOrigin,
                IsCanonicalWinner: true)
        ]);

        field.RemoveValue(Assert.Single(field.Values));

        Assert.Empty(metadata.GetSuppressedValues(ProjectMetadataField.Barcode));
    }

    [Fact]
    public void SourceBackedGenericValue_RemoveKeepsCandidate_AndExplicitCommitUnsuppresses()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Barcode, "4000000000041", MetadataOrigin.SourceAggregation);
        var refreshRequests = 0;
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            new MetadataPreferences(),
            new MetadataNormalizationService(),
            ProjectMetadataField.Barcode,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Barcode),
            _ => { },
            _ => { },
            candidateRefreshRequested: _ => refreshRequests++);

        field.SetCandidates(
        [
            new ConceptMetadataCandidate(
                ProjectMetadataField.Barcode,
                "4000000000041",
                "4000000000041",
                2,
                MetadataOrigin.SourceAggregation,
                IsCanonicalWinner: true)
        ]);

        field.RemoveValue(Assert.Single(field.Values));

        Assert.Contains("4000000000041", metadata.GetSuppressedValues(ProjectMetadataField.Barcode));
        Assert.Single(field.Candidates);
        Assert.True(refreshRequests > 0);

        field.Draft = "4000000000041";
        field.CommitDraft();

        Assert.Empty(metadata.GetSuppressedValues(ProjectMetadataField.Barcode));
        Assert.Equal("4000000000041", metadata.GetValue<string>(ProjectMetadataField.Barcode).Value);
    }


    [Fact]
    public void FieldBinding_ActivationClearsTargetRetainsCandidatesAndProjectsTheAuthoritativeSourceValues()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Sprecher A", "Sprecher B"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, ["Komponist A", "Komponist B"], MetadataOrigin.User);
        var retained = new List<(ProjectMetadataField Field, string Value)>();
        var source = CreateField(() => metadata, ProjectMetadataField.Narrators, _ => { });
        var target = CreateField(
            () => metadata,
            ProjectMetadataField.Composers,
            _ => { },
            (field, value) => retained.Add((field, value)));
        target.Draft = "unbestätigt";
        target.SetCandidates(
        [
            new ConceptMetadataCandidate(ProjectMetadataField.Composers, "Echter Kandidat", "Echter Kandidat", 1, MetadataOrigin.SourceAggregation)
        ]);
        source.SetCandidates(
        [
            new ConceptMetadataCandidate(ProjectMetadataField.Narrators, "Paul Sommer", "Paul Sommer", 1, MetadataOrigin.SourceAggregation)
        ]);
        var isActive = false;
        var binding = new ConceptMetadataFieldBindingState(
            ProjectMetadataFieldBindingCatalog.NarratorsToComposers,
            source,
            target,
            () => isActive,
            value => isActive = value);

        binding.IsActive = true;

        Assert.True(binding.IsActive);
        Assert.Equal(ProjectMetadataField.Narrators, binding.SourceFieldId);
        Assert.Equal(ProjectMetadataField.Composers, binding.TargetFieldId);
        Assert.Same(source.Values, binding.ProjectedValues);
        Assert.Equal(["Sprecher A", "Sprecher B"], binding.ProjectedValues.Select(value => value.Value));
        Assert.Empty(target.Values);
        Assert.Empty(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
        Assert.Equal(string.Empty, target.Draft);
        Assert.Equal(["Komponist A", "Komponist B"], metadata.GetSuppressedValues(ProjectMetadataField.Composers));
        Assert.Equal(
            [(ProjectMetadataField.Composers, "Komponist A"), (ProjectMetadataField.Composers, "Komponist B")],
            retained);
        Assert.Contains(target.Candidates, candidate => candidate.DisplayValue == "Echter Kandidat");
        Assert.DoesNotContain(target.Candidates, candidate => candidate.DisplayValue == "Paul Sommer");

        target.SetCandidates(
        [
            new ConceptMetadataCandidate(ProjectMetadataField.Composers, "Paul Sommer", "Paul Sommer", 1, MetadataOrigin.SourceAggregation),
            new ConceptMetadataCandidate(ProjectMetadataField.Composers, "Komponist A", "Komponist A", 1, MetadataOrigin.SourceAggregation)
        ]);
        Assert.Contains(target.Candidates, candidate => candidate.DisplayValue == "Paul Sommer");
        Assert.Contains(target.Candidates, candidate => candidate.DisplayValue == "Komponist A");

        source.Draft = "Sprecher C";
        source.CommitDraft();
        Assert.Equal(["Sprecher A", "Sprecher B", "Sprecher C"], binding.ProjectedValues.Select(value => value.Value));

        source.MoveValue(source.Values[2], 0);
        Assert.Equal(["Sprecher C", "Sprecher A", "Sprecher B"], binding.ProjectedValues.Select(value => value.Value));

        source.EditValue(source.Values[1]);
        source.Draft = "Sprecher A2";
        source.CommitDraft();
        Assert.Equal(["Sprecher C", "Sprecher A2", "Sprecher B"], binding.ProjectedValues.Select(value => value.Value));

        foreach (var value in source.Values.ToArray())
            source.RemoveValue(value);
        Assert.Empty(binding.ProjectedValues);
        Assert.True(binding.IsActive);

        source.Draft = "Sprecher Neu";
        source.CommitDraft();
        Assert.Equal("Sprecher Neu", Assert.Single(binding.ProjectedValues).Value);

        binding.IsActive = false;
        Assert.False(binding.IsActive);
        Assert.Empty(target.Values);
        Assert.Empty(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
    }

    [Fact]
    public void FieldBindingCatalog_UsesStableGlobalDefinitionsWithExpectedDefaults()
    {
        var narratorComposer = ProjectMetadataFieldBindingCatalog.NarratorsToComposers;
        Assert.Equal("narrators-to-composers", narratorComposer.BindingId);
        Assert.Equal(ProjectMetadataField.Narrators, narratorComposer.SourceField);
        Assert.Equal(ProjectMetadataField.Composers, narratorComposer.TargetField);
        Assert.True(narratorComposer.DefaultEnabled);
        Assert.Contains(narratorComposer, ProjectMetadataFieldBindingCatalog.All);

        var titleAlbum = ProjectMetadataFieldBindingCatalog.TitleToAlbum;
        Assert.Equal("title-to-album", titleAlbum.BindingId);
        Assert.Equal(ProjectMetadataField.Title, titleAlbum.SourceField);
        Assert.Equal(ProjectMetadataField.Album, titleAlbum.TargetField);
        Assert.True(titleAlbum.DefaultEnabled);
        Assert.Contains(titleAlbum, ProjectMetadataFieldBindingCatalog.All);
    }

    [Fact]
    public void FieldBindingProjection_SupportsProductiveScalarTitleToAlbumBindingGenerically()
    {
        var active = true;
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Quelltitel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Eigenes Album", MetadataOrigin.User);
        var binding = ProjectMetadataFieldBindingCatalog.TitleToAlbum;
        var projection = new ProjectMetadataFieldBindingProjectionService();

        Assert.Equal(["Quelltitel"], projection.ResolveTargetTextValues(metadata, binding, isActive: active));
        active = false;
        Assert.Equal(["Eigenes Album"], projection.ResolveTargetTextValues(metadata, binding, isActive: active));
        Assert.Equal("Eigenes Album", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void ScalarFieldBinding_ActivationDiscardsDraftRetainsAlbumAndNeverCopiesTitleCandidates()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel A", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Album A", MetadataOrigin.User);
        var retained = new List<(ProjectMetadataField Field, string Value)>();
        var source = CreateField(() => metadata, ProjectMetadataField.Title, _ => { });
        var target = CreateField(
            () => metadata,
            ProjectMetadataField.Album,
            _ => { },
            (field, value) => retained.Add((field, value)));
        source.SetCandidates([
            new ConceptMetadataCandidate(ProjectMetadataField.Title, "Titel Kandidat", "Titel Kandidat", 1, MetadataOrigin.SourceAggregation)
        ]);
        target.SetCandidates([
            new ConceptMetadataCandidate(ProjectMetadataField.Album, "Album Kandidat", "Album Kandidat", 1, MetadataOrigin.SourceAggregation)
        ]);
        target.Draft = "Offener Album-Draft";
        var active = false;
        var binding = new ConceptMetadataFieldBindingState(
            ProjectMetadataFieldBindingCatalog.TitleToAlbum,
            source,
            target,
            () => active,
            value => active = value);

        binding.IsActive = true;

        Assert.True(binding.IsActive);
        Assert.Equal(string.Empty, target.Draft);
        Assert.Empty(target.Values);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(["Album A"], metadata.GetSuppressedValues(ProjectMetadataField.Album));
        Assert.Equal([(ProjectMetadataField.Album, "Album A")], retained);
        Assert.Contains(target.Candidates, candidate => candidate.DisplayValue == "Album Kandidat");
        Assert.DoesNotContain(target.Candidates, candidate => candidate.DisplayValue == "Titel Kandidat");
        Assert.Same(source.Values, binding.ProjectedValues);
        Assert.Equal("Titel A", Assert.Single(binding.ProjectedValues).Value);

        source.RemoveValue(Assert.Single(source.Values));
        Assert.Empty(binding.ProjectedValues);
        Assert.True(binding.IsActive);
        source.Draft = "Titel B";
        source.CommitDraft();
        Assert.Equal("Titel B", Assert.Single(binding.ProjectedValues).Value);

        binding.IsActive = false;
        Assert.False(binding.IsActive);
        Assert.Empty(target.Values);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void QuickAccessComposition_CommitsFieldChangesToCanonicalState()
    {
        var state = new ProjectMetadataState();
        var savedMetadata = new List<ProjectMetadata>();
        var composition = new ConceptMetadataFieldComposition(
            () => state.CanonicalMetadata,
            new MetadataPreferences(),
            new MetadataNormalizationService(),
            savedMetadata.Add,
            _ => { });

        composition.Title.Draft = "Gemeinsamer Titel";
        composition.Title.CommitDraft();

        Assert.Equal("Gemeinsamer Titel", state.BookTitle);
        Assert.Same(state.CanonicalMetadata, Assert.Single(savedMetadata));
    }

    [Fact]
    public void QuickAccessComposition_UsesSharedOutputAndGenrePreferences()
    {
        var state = new ProjectMetadataState();
        var preferences = new MetadataPreferences();
        var savedPreferences = new List<MetadataPreferences>();
        var composition = new ConceptMetadataFieldComposition(
            () => state.CanonicalMetadata,
            preferences,
            new MetadataNormalizationService(),
            _ => { },
            savedPreferences.Add);

        composition.Title.IsOutputEnabled = false;
        composition.Genres.Draft = "Fantasy";
        composition.Genres.CommitDraft();
        composition.Genres.IsLocked = true;

        Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Title));
        Assert.True(preferences.GenrePreferenceLocked);
        Assert.Contains("Fantasy", preferences.PreferredGenres);
        Assert.Contains(preferences, savedPreferences);
    }

    [Fact]
    public void QuickAccessComposition_PersistsTitleAlbumBindingAndProjectsTitleValues()
    {
        var state = new ProjectMetadataState();
        var preferences = new MetadataPreferences();
        preferences.MetadataFieldBindings[ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = false;
        var savedPreferences = new List<MetadataPreferences>();
        var composition = new ConceptMetadataFieldComposition(
            () => state.CanonicalMetadata,
            preferences,
            new MetadataNormalizationService(),
            _ => { },
            savedPreferences.Add);

        composition.Title.Draft = "Gemeinsamer Titel";
        composition.Title.CommitDraft();
        composition.TitleAlbumBinding.IsActive = true;

        Assert.True(MetadataFieldBindingPreferencesService.GetIsEnabled(
            preferences,
            ProjectMetadataFieldBindingCatalog.TitleToAlbum));
        Assert.Same(composition.Title.Values, composition.TitleAlbumBinding.ProjectedValues);
        Assert.Equal("Gemeinsamer Titel", Assert.Single(composition.TitleAlbumBinding.ProjectedValues).Value);
        Assert.Contains(preferences, savedPreferences);
    }

    private static ConceptMetadataFieldViewModel CreateField(
        Func<ProjectMetadata> currentMetadata,
        ProjectMetadataField field,
        Action<ProjectMetadata> saveMetadata,
        Action<ProjectMetadataField, string>? candidateValueCommitted = null) =>
        new(
            currentMetadata,
            new MetadataPreferences(),
            new MetadataNormalizationService(),
            field,
            ConceptMetadataFieldCatalog.Get(field),
            saveMetadata,
            _ => { },
            candidateValueCommitted: candidateValueCommitted);

    [Fact]
    public void Setup_draft_helpers_detect_and_discard_only_active_field_drafts()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var normalization = new MetadataNormalizationService();
        var composition = new ConceptMetadataFieldComposition(
            () => metadata,
            preferences,
            normalization,
            _ => { },
            _ => { });

        composition.Title.Draft = "Titelentwurf";
        composition.Genres.Draft = "Fantasy";

        var active = DiscProjectSetupDialog.GetActiveSetupDrafts(composition.Fields);
        Assert.Equal([ProjectMetadataField.Title, ProjectMetadataField.Genres], active.Select(field => field.Field));

        DiscProjectSetupDialog.DiscardSetupDrafts(active);
        Assert.DoesNotContain(composition.Fields, field => field.HasDraft);
        Assert.True(string.IsNullOrEmpty(metadata.GetValue<string>(ProjectMetadataField.Title).Value));
    }

    [Fact]
    public void Shared_concept_draft_warning_message_keeps_distinct_nonempty_labels_in_order()
    {
        var message = ConceptDraftWarningPresentation.BuildMessage(["Titel", "", "Genre", "Titel"]);
        Assert.Equal(
            ["• Titel", "• Genre"],
            message.Split(Environment.NewLine).Where(line => line.StartsWith("• ", StringComparison.Ordinal)));
    }

}
