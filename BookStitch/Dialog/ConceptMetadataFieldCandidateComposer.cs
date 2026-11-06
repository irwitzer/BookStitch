using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Dialog;

public static class ConceptMetadataFieldCandidateComposer
{
    public static IReadOnlyList<ConceptMetadataCandidate> Build(
        ConceptMetadataFieldViewModel field,
        ProjectMetadataState metadataState,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        IReadOnlyList<MetadataFieldDecisionDiagnostic> decisions,
        IReadOnlyList<string> genres,
        ConceptCandidateSessionService candidateSession,
        SourceMetadataAlternativeService candidateService,
        IEnumerable<ConceptMetadataFieldBindingState> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(metadataState);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(genres);
        ArgumentNullException.ThrowIfNull(candidateSession);
        ArgumentNullException.ThrowIfNull(candidateService);
        ArgumentNullException.ThrowIfNull(bindings);

        var bindingStates = bindings.ToArray();
        var retainedBindingTargetCandidates = BuildRetainedBindingTargetCandidates(
            field.Field,
            metadataState,
            bindingStates);
        retainedBindingTargetCandidates = (retainedBindingTargetCandidates ?? []).Concat(BuildSortSuggestions(field.Field, metadataState, bindingStates));

        if (field.ValueKind == ProjectMetadataFieldValueKind.StringList)
        {
            IEnumerable<ConceptMetadataCandidate>? staticCandidates = field.Field switch
            {
                ProjectMetadataField.Languages or
                ProjectMetadataField.OriginalLanguages or
                ProjectMetadataField.TargetLanguages => LanguageOptionCatalog.All.Select(option =>
                    new ConceptMetadataCandidate(field.Field, option.DisplayText, option.Value, 0, MetadataOrigin.StaticList)),
                ProjectMetadataField.Genres => genres
                    .Where(GenreListService.IsSelectableGenre)
                    .Select(value => new ConceptMetadataCandidate(field.Field, value, value, 0, MetadataOrigin.StaticList)),
                _ => null
            };

            var presented = candidateSession.PresentList(
                field.Field,
                candidateService.BuildStringListItemCandidates(
                    snapshots,
                    field.Field,
                    cancellationToken),
                metadataState.CanonicalMetadata.GetValue<IReadOnlyList<string>>(field.Field),
                staticCandidates,
                retainedBindingTargetCandidates);
            if (field.Field != ProjectMetadataField.Genres) return presented;

            // The genre catalogue is two locally sorted groups with optional private
            // prefixes. Keep that order even when a catalogue item has source evidence;
            // retain non-catalogue project/import values after the catalogue.
            var order = genres.Where(GenreListService.IsSelectableGenre)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select((value, index) => (value, index))
                .ToDictionary(item => item.value, item => item.index, StringComparer.OrdinalIgnoreCase);
            return presented.OrderBy(candidate =>
                order.GetValueOrDefault(candidate.DisplayValue, int.MaxValue)).ToArray();
        }

        if (field.ValueKind == ProjectMetadataFieldValueKind.String)
        {
            return candidateSession.PresentScalar(
                field.Field,
                candidateService.BuildScalarTextCandidates(
                    snapshots,
                    field.Field,
                    cancellationToken),
                metadataState.CanonicalMetadata.GetValue<string>(field.Field),
                decisions,
                userCandidates: retainedBindingTargetCandidates);
        }

        return [];
    }

    private static IEnumerable<ConceptMetadataCandidate> BuildSortSuggestions(
        ProjectMetadataField field,
        ProjectMetadataState state,
        IReadOnlyList<ConceptMetadataFieldBindingState> bindings)
    {
        var source = field switch
        {
            ProjectMetadataField.SortTitle => ProjectMetadataField.Title,
            ProjectMetadataField.SortAuthors => ProjectMetadataField.Authors,
            ProjectMetadataField.SortNarrators => ProjectMetadataField.Narrators,
            ProjectMetadataField.SortAlbum => ProjectMetadataField.Album,
            ProjectMetadataField.SortSeries => ProjectMetadataField.Series,
            ProjectMetadataField.SortComposers => ProjectMetadataField.Composers,
            _ => (ProjectMetadataField?)null
        };
        if (source is null) return [];
        // An active binding projects its source without storing a second target value.
        var effectiveSource = bindings.SingleOrDefault(binding => binding.TargetFieldId == source && binding.IsActive)
            ?.SourceFieldId ?? source.Value;
        IEnumerable<string> values = effectiveSource == ProjectMetadataField.Series
            ? (state.CanonicalMetadata.GetValue<IReadOnlyList<SeriesEntry>>(effectiveSource).Value ?? []).Select(entry => entry.Name)
            : new ProjectMetadataFieldBindingProjectionService().ReadTextValues(state.CanonicalMetadata, effectiveSource);
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new ConceptMetadataCandidate(field, value, value, 0, MetadataOrigin.Derived));
    }

    private static IEnumerable<ConceptMetadataCandidate>? BuildRetainedBindingTargetCandidates(
        ProjectMetadataField field,
        ProjectMetadataState metadataState,
        IEnumerable<ConceptMetadataFieldBindingState> bindings)
    {
        if (!bindings.Any(binding => binding.TargetFieldId == field))
            return null;

        return metadataState.CanonicalMetadata.GetSuppressedValues(field)
            .Select(value => new ConceptMetadataCandidate(field, value, value, 0, MetadataOrigin.User));
    }
}
