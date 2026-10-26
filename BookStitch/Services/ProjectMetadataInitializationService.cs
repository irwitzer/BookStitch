using BookStitch.Models;

namespace BookStitch.Services;

public enum ProjectMetadataInitializationState
{
    InitialSetOpen,
    InitialSetComplete,
    ProjectMetadataInitialized,
    SubsequentSource
}

public enum MetadataAggregationDiagnosticCode
{
    AmbiguousMajority,
    StrongIdentityConflict
}

public sealed record MetadataAggregationDiagnostic(
    MetadataAggregationDiagnosticCode Code,
    ProjectMetadataField? Field,
    string Message);

public sealed record ProjectMetadataInitializationOptions(
    string? LastUsedGenre = null,
    string? ReplaceableGenreSeed = null,
    string? PreferredGenre = null,
    bool IsGenrePreferenceFixed = false,
    IReadOnlyList<string>? PreferredGenres = null,
    IReadOnlyList<string>? PreferredLanguages = null,
    bool IsLanguagePreferenceFixed = false,
    int? PreferredOutputTrackNumber = null,
    bool IsOutputTrackNumberPreferenceFixed = false,
    int? PreferredOutputTrackTotal = null,
    bool IsOutputTrackTotalPreferenceFixed = false,
    AudioWorkType? AudioWorkTypeFallback = null,
    string? BookFolderCandidate = null,
    string? ParentFolderCandidate = null,
    string? SingleFileNameCandidate = null,
    bool IsSingleFile = false,
    IReadOnlyList<string>? SourceFileNameCandidates = null,
    bool AllowFilenameTitleFallback = false,
    bool AllowFolderStructureFallbacks = true,
    bool AllowSingleFileNameFallback = true,
    AudioWorkType? PreferredAudioWorkType = null,
    bool IsAudioWorkTypePreferenceFixed = false);

public sealed record ProjectMetadataInitializationResult(
    bool Changed,
    IReadOnlyList<ProjectMetadataField> AppliedFields,
    IReadOnlyList<MetadataAggregationDiagnostic> Diagnostics,
    IReadOnlyList<MetadataFieldDecisionDiagnostic> FieldDecisions);

public enum MetadataDecisionEvidenceType
{
    DirectTitleAggregation,
    DirectAlbumAggregation,
    AlbumFallback,
    OrderedDistinctSourceAggregation,
    FolderStructure,
    SingleFileName,
    FilenameAggregationFallback,
    UserOverride,
    ExistingCanonical
}

public sealed record MetadataDecisionCandidateDiagnostic(
    string Value,
    MetadataDecisionEvidenceType EvidenceType,
    int? SupportCount,
    int? EvidenceCount,
    double? SupportRatio,
    string? RejectedReason = null,
    MetadataDecisionCandidateDiagnostic? RunnerUp = null);

public sealed record MetadataFieldDecisionDiagnostic(
    ProjectMetadataField Field,
    string CanonicalValue,
    MetadataOrigin Origin,
    string DecisionReason,
    MetadataDecisionCandidateDiagnostic? Winner,
    MetadataDecisionCandidateDiagnostic? RunnerUp);

public sealed class ProjectMetadataInitializationService
{
    private readonly MetadataNormalizationService _normalization;
    private readonly ProjectTitleSourceEvidenceService _projectTitleEvidence;

    public ProjectMetadataInitializationService()
        : this(new MetadataNormalizationService())
    {
    }

    public ProjectMetadataInitializationService(MetadataNormalizationService normalization)
    {
        _normalization = normalization ?? throw new ArgumentNullException(nameof(normalization));
        _projectTitleEvidence = new ProjectTitleSourceEvidenceService(_normalization);
    }

    public ProjectMetadataInitializationResult Apply(
        ProjectMetadata metadata,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataInitializationState state,
        ProjectMetadataInitializationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(snapshots);
        options ??= new ProjectMetadataInitializationOptions();

        var diagnostics = DetectStrongIdentityConflicts(snapshots).ToList();
        var candidates = BuildCandidates(snapshots, options, diagnostics);
        var applied = new List<ProjectMetadataField>();

        foreach (var rawCandidate in candidates)
        {
            if (rawCandidate.Origin == MetadataOrigin.GlobalPreference)
            {
                if (SetField(metadata, rawCandidate.Field, rawCandidate.Value, rawCandidate.Origin))
                    applied.Add(rawCandidate.Field);
                continue;
            }
            if (state == ProjectMetadataInitializationState.InitialSetOpen)
                continue;
            var candidate = ApplySuppressedValues(metadata, rawCandidate);
            if (candidate is null || !CanApply(metadata, candidate.Field, state, options))
                continue;

            if (SetField(metadata, candidate.Field, candidate.Value, candidate.Origin))
                applied.Add(candidate.Field);
        }

        return new ProjectMetadataInitializationResult(
            applied.Count > 0,
            applied,
            diagnostics,
            BuildFieldDecisions(metadata, candidates, applied));
    }

    private IReadOnlyList<FieldCandidate> BuildCandidates(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataInitializationOptions options,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        var result = new List<FieldCandidate>();
        var rejectedTitleEvidence = AddProjectTitle(result, snapshots);
        AddString(result, ProjectMetadataField.Subtitle, snapshots.Select(snapshot => snapshot.CanonicalFields.Subtitle), diagnostics);
        AddString(result, ProjectMetadataField.OriginalTitle, snapshots.Select(snapshot => snapshot.CanonicalFields.OriginalTitle), diagnostics);
        AddString(result, ProjectMetadataField.Album, snapshots.Select(snapshot => snapshot.CanonicalFields.Album), diagnostics);
        AddAlbumTitleFallback(result, snapshots, rejectedTitleEvidence);
        AddString(result, ProjectMetadataField.Description, snapshots.Select(snapshot => snapshot.CanonicalFields.Description), diagnostics);
        AddStringList(result, ProjectMetadataField.Keywords, snapshots.Select(snapshot => snapshot.CanonicalFields.Keywords), diagnostics);
        AddString(result, ProjectMetadataField.ContentType, snapshots.Select(snapshot => snapshot.CanonicalFields.ContentType), diagnostics);
        AddString(result, ProjectMetadataField.Comment, snapshots.Select(snapshot => snapshot.CanonicalFields.Comment), diagnostics);
        AddStringList(result, ProjectMetadataField.Authors, snapshots.Select(snapshot => snapshot.CanonicalFields.Authors), diagnostics);
        AddStringList(result, ProjectMetadataField.Narrators, snapshots.Select(snapshot => snapshot.CanonicalFields.Narrators), diagnostics);
        AddStringList(result, ProjectMetadataField.Translators, snapshots.Select(snapshot => snapshot.CanonicalFields.Translators), diagnostics);
        AddStringList(result, ProjectMetadataField.Composers, snapshots.Select(snapshot => snapshot.CanonicalFields.Composers), diagnostics);
        if (options.IsLanguagePreferenceFixed && options.PreferredLanguages is { Count: > 0 })
            result.Add(new FieldCandidate(ProjectMetadataField.Languages, options.PreferredLanguages, MetadataOrigin.GlobalPreference));
        else
            AddStringList(result, ProjectMetadataField.Languages, snapshots.Select(snapshot => snapshot.CanonicalFields.Languages), diagnostics);
        AddStringList(result, ProjectMetadataField.OriginalLanguages, snapshots.Select(snapshot => snapshot.CanonicalFields.OriginalLanguages), diagnostics);
        AddStringList(result, ProjectMetadataField.TargetLanguages, snapshots.Select(snapshot => snapshot.CanonicalFields.TargetLanguages), diagnostics);
        AddSeries(result, snapshots.Select(snapshot => snapshot.CanonicalFields.Series), diagnostics);
        AddString(result, ProjectMetadataField.Volume, snapshots.Select(snapshot => snapshot.CanonicalFields.Volume), diagnostics);
        AddString(result, ProjectMetadataField.Edition, snapshots.Select(snapshot => snapshot.CanonicalFields.Edition), diagnostics);
        AddString(result, ProjectMetadataField.Script, snapshots.Select(snapshot => snapshot.CanonicalFields.Script), diagnostics);
        AddString(result, ProjectMetadataField.Publisher, snapshots.Select(snapshot => snapshot.CanonicalFields.Publisher), diagnostics);
        AddMetadataDate(result, ProjectMetadataField.ReleaseDate, snapshots.Select(snapshot => snapshot.CanonicalFields.ReleaseDate), diagnostics);
        AddInteger(result, ProjectMetadataField.Year, snapshots.Select(snapshot => snapshot.CanonicalFields.Year), diagnostics);
        AddString(result, ProjectMetadataField.Copyright, snapshots.Select(snapshot => snapshot.CanonicalFields.Copyright), diagnostics);
        AddString(result, ProjectMetadataField.PhonogramCopyright, snapshots.Select(snapshot => snapshot.CanonicalFields.PhonogramCopyright), diagnostics);
        AddString(result, ProjectMetadataField.License, snapshots.Select(snapshot => snapshot.CanonicalFields.License), diagnostics);
        AddStringList(result, ProjectMetadataField.Categories, snapshots.Select(snapshot => snapshot.CanonicalFields.Categories), diagnostics);
        AddString(result, ProjectMetadataField.ISBN, snapshots.Select(snapshot => snapshot.CanonicalFields.ISBN), diagnostics);
        AddString(result, ProjectMetadataField.Barcode, snapshots.Select(snapshot => snapshot.CanonicalFields.Barcode), diagnostics);
        AddString(result, ProjectMetadataField.ASIN, snapshots.Select(snapshot => snapshot.CanonicalFields.ASIN), diagnostics);
        AddStringList(result, ProjectMetadataField.CatalogNumbers, snapshots.Select(snapshot => snapshot.CanonicalFields.CatalogNumbers), diagnostics);
        AddString(result, ProjectMetadataField.MusicBrainzReleaseId, snapshots.Select(snapshot => snapshot.CanonicalFields.MusicBrainzReleaseId), diagnostics);
        AddString(result, ProjectMetadataField.SortTitle, snapshots.Select(snapshot => snapshot.CanonicalFields.SortTitle), diagnostics);
        AddStringList(result, ProjectMetadataField.SortAuthors, snapshots.Select(snapshot => snapshot.CanonicalFields.SortAuthors), diagnostics);
        AddStringList(result, ProjectMetadataField.SortNarrators, snapshots.Select(snapshot => snapshot.CanonicalFields.SortNarrators), diagnostics);
        AddString(result, ProjectMetadataField.SortAlbum, snapshots.Select(snapshot => snapshot.CanonicalFields.SortAlbum), diagnostics);
        AddString(result, ProjectMetadataField.SortSeries, snapshots.Select(snapshot => snapshot.CanonicalFields.SortSeries), diagnostics);
        AddString(result, ProjectMetadataField.Website, snapshots.Select(snapshot => snapshot.CanonicalFields.Website), diagnostics);
        AddString(result, ProjectMetadataField.Source, snapshots.Select(snapshot => snapshot.CanonicalFields.Source), diagnostics);
        if (options.IsOutputTrackNumberPreferenceFixed && options.PreferredOutputTrackNumber is >= 0)
            result.Add(new FieldCandidate(ProjectMetadataField.OutputTrackNumber, options.PreferredOutputTrackNumber, MetadataOrigin.GlobalPreference));
        if (options.IsOutputTrackTotalPreferenceFixed && options.PreferredOutputTrackTotal is >= 0)
            result.Add(new FieldCandidate(ProjectMetadataField.OutputTrackTotal, options.PreferredOutputTrackTotal, MetadataOrigin.GlobalPreference));
        AddEnum(result, ProjectMetadataField.AbridgedStatus, snapshots.Select(snapshot => snapshot.CanonicalFields.AbridgedStatus), diagnostics);
        if (options.IsAudioWorkTypePreferenceFixed && options.PreferredAudioWorkType is AudioWorkType.Audiobook or AudioWorkType.AudioDrama)
            result.Add(new FieldCandidate(ProjectMetadataField.AudioWorkType, options.PreferredAudioWorkType.Value, MetadataOrigin.GlobalPreference));
        else
            AddEnum(result, ProjectMetadataField.AudioWorkType, snapshots.Select(snapshot => snapshot.CanonicalFields.AudioWorkType), diagnostics);
        AddGenres(result, snapshots, options, diagnostics);
        AddFilenameTitleFallback(result, snapshots, options, rejectedTitleEvidence);
        AddFolderFallbacks(result, snapshots, options, rejectedTitleEvidence);
        return result;
    }

    private MetadataDecisionCandidateDiagnostic? AddProjectTitle(
        List<FieldCandidate> result,
        IReadOnlyList<SourceMetadataSnapshot> snapshots)
    {
        var evaluation = _projectTitleEvidence.EvaluateTitle(snapshots);
        var evidence = evaluation.Selected;
        if (evidence is not null)
        {
            result.Add(new FieldCandidate(
                ProjectMetadataField.Title,
                evidence.Value,
                MetadataOrigin.SourceAggregation,
                CreateTitleEvidence(evidence, MetadataDecisionEvidenceType.DirectTitleAggregation)));
            return null;
        }

        return evaluation.StrongestRejected is null
            ? null
            : new MetadataDecisionCandidateDiagnostic(
                evaluation.StrongestRejected.Value,
                MetadataDecisionEvidenceType.DirectTitleAggregation,
                evaluation.StrongestRejected.SourceCount,
                evaluation.CandidateCount,
                Ratio(evaluation.StrongestRejected.SourceCount, evaluation.CandidateCount),
                evaluation.RejectionReason);
    }

    private void AddAlbumTitleFallback(
        List<FieldCandidate> result,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        MetadataDecisionCandidateDiagnostic? rejectedTitleEvidence)
    {
        if (HasCandidate(result, ProjectMetadataField.Title))
            return;

        var evidence = _projectTitleEvidence.SelectAlbumFallback(snapshots);
        if (evidence is not null)
        {
            var albumEvidence = CreateTitleEvidence(evidence, MetadataDecisionEvidenceType.AlbumFallback);
            result.Add(new FieldCandidate(
                ProjectMetadataField.Title,
                evidence.Value,
                MetadataOrigin.SourceAggregation,
                albumEvidence with { RunnerUp = rejectedTitleEvidence ?? albumEvidence.RunnerUp }));
        }
    }

    private void AddFolderFallbacks(
        List<FieldCandidate> result,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataInitializationOptions options,
        MetadataDecisionCandidateDiagnostic? rejectedTitleEvidence)
    {
        if (!options.AllowFolderStructureFallbacks)
            return;

        if (!HasCandidate(result, ProjectMetadataField.Title) ||
            !HasCandidate(result, ProjectMetadataField.Album))
        {
            var bookFolder = _normalization.NormalizeFolderCandidate(options.BookFolderCandidate);
            if (bookFolder.Length > 0 && !IsTechnicalFolderName(bookFolder))
            {
                if (!HasCandidate(result, ProjectMetadataField.Title))
                    result.Add(new FieldCandidate(
                        ProjectMetadataField.Title,
                        bookFolder,
                        MetadataOrigin.FolderStructure,
                        CreateSingleEvidence(bookFolder, MetadataDecisionEvidenceType.FolderStructure) with
                        {
                            RunnerUp = rejectedTitleEvidence
                        }));

                if (!HasCandidate(result, ProjectMetadataField.Album))
                    result.Add(new FieldCandidate(
                        ProjectMetadataField.Album,
                        bookFolder,
                        MetadataOrigin.FolderStructure,
                        CreateSingleEvidence(bookFolder, MetadataDecisionEvidenceType.FolderStructure)));
            }
        }

        if (!HasCandidate(result, ProjectMetadataField.Authors))
        {
            var parent = _normalization.NormalizeFolderCandidate(options.ParentFolderCandidate);
            if (parent.Length > 0 && !IsTechnicalFolderName(parent))
                result.Add(new FieldCandidate(
                    ProjectMetadataField.Authors,
                    new[] { parent },
                    MetadataOrigin.FolderStructure,
                    CreateSingleEvidence(parent, MetadataDecisionEvidenceType.FolderStructure)));
        }

        if (options.AllowSingleFileNameFallback &&
            options.IsSingleFile &&
            !HasCandidate(result, ProjectMetadataField.Title) &&
            snapshots.Count <= 1)
        {
            var singleFile = _normalization.NormalizeFolderCandidate(options.SingleFileNameCandidate);
            if (singleFile.Length > 0 && !IsTechnicalFolderName(singleFile))
                result.Add(new FieldCandidate(
                    ProjectMetadataField.Title,
                    singleFile,
                    MetadataOrigin.SingleFileName,
                    CreateSingleEvidence(singleFile, MetadataDecisionEvidenceType.SingleFileName) with
                    {
                        RunnerUp = rejectedTitleEvidence
                    }));
        }
    }

    private void AddFilenameTitleFallback(
        List<FieldCandidate> result,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataInitializationOptions options,
        MetadataDecisionCandidateDiagnostic? rejectedTitleEvidence)
    {
        if (!options.AllowFilenameTitleFallback || HasCandidate(result, ProjectMetadataField.Title))
            return;

        var names = (options.SourceFileNameCandidates is { Count: > 0 }
                ? options.SourceFileNameCandidates
                : snapshots
                    .Select(snapshot => snapshot.TechnicalInfo.SourcePath)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(System.IO.Path.GetFileName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        if (names.Length < 2)
            return;

        var evaluation = _projectTitleEvidence.EvaluateFilenameTitle(names);
        var evidence = evaluation.Selected;
        if (evidence is null)
            return;

        var candidateEvidence = CreateTitleEvidence(evidence, MetadataDecisionEvidenceType.FilenameAggregationFallback) with
        {
            RunnerUp = rejectedTitleEvidence
        };
        result.Add(new FieldCandidate(
            ProjectMetadataField.Title,
            evidence.Value,
            MetadataOrigin.Derived,
            candidateEvidence));

        if (!HasCandidate(result, ProjectMetadataField.Album))
        {
            result.Add(new FieldCandidate(
                ProjectMetadataField.Album,
                evidence.Value,
                MetadataOrigin.Derived,
                candidateEvidence));
        }
    }

    private void AddGenres(
        List<FieldCandidate> result,
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataInitializationOptions options,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        if (options.IsGenrePreferenceFixed)
        {
            var preferred = (options.PreferredGenres ??
                    (string.IsNullOrWhiteSpace(options.PreferredGenre) ? [] : [options.PreferredGenre]))
                .Select(_normalization.NormalizeText)
                .Where(value => value.Length > 0)
                .ToArray();
            if (preferred.Length > 0)
            {
                result.Add(new FieldCandidate(ProjectMetadataField.Genres, preferred, MetadataOrigin.GlobalPreference));
                return;
            }
        }

        var genres = MergeOrderedDistinctList(
            ProjectMetadataField.Genres,
            snapshots.Select(snapshot => snapshot.CanonicalFields.Genres));
        if (genres.Count > 0)
            result.Add(new FieldCandidate(ProjectMetadataField.Genres, genres));
    }

    private void AddString(
        List<FieldCandidate> result,
        ProjectMetadataField field,
        IEnumerable<string> values,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        var selected = TrySelectBestValue(
            field,
            values
                .Select(_normalization.NormalizeText)
                .Where(value => value.Length > 0)
                .Cast<object>(),
            value => _normalization.NormalizeForComparison(field, (string)value),
            diagnostics);
        if (selected?.Value is string text)
        {
            result.Add(new FieldCandidate(
                field,
                text,
                MetadataOrigin.SourceAggregation,
                field == ProjectMetadataField.Album
                    ? CreateCandidateEvidence(selected, MetadataDecisionEvidenceType.DirectAlbumAggregation)
                    : null));
        }
    }

    private void AddStringList(
        List<FieldCandidate> result,
        ProjectMetadataField field,
        IEnumerable<IReadOnlyList<string>> values,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        _ = diagnostics;
        var merged = MergeOrderedDistinctList(field, values);
        if (merged.Count > 0)
        {
            var sourceValues = values.ToArray();
            var contributingSources = sourceValues.Count(items => items.Any(item =>
                _normalization.NormalizeText(item).Length > 0));
            result.Add(new FieldCandidate(
                field,
                merged,
                MetadataOrigin.SourceAggregation,
                field is ProjectMetadataField.Authors or ProjectMetadataField.Narrators
                    ? new MetadataDecisionCandidateDiagnostic(
                        string.Join("; ", merged),
                        MetadataDecisionEvidenceType.OrderedDistinctSourceAggregation,
                        contributingSources,
                        sourceValues.Length,
                        Ratio(contributingSources, sourceValues.Length))
                    : null));
        }
    }

    private IReadOnlyList<string> MergeOrderedDistinctList(
        ProjectMetadataField field,
        IEnumerable<IReadOnlyList<string>> values)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceValues in values)
        {
            foreach (var sourceValue in sourceValues)
            {
                var normalized = _normalization.NormalizeText(sourceValue);
                if (normalized.Length == 0)
                    continue;

                var comparisonKey = _normalization.NormalizeForComparison(field, normalized);
                if (comparisonKey.Length == 0 || !seen.Add(comparisonKey))
                    continue;

                merged.Add(normalized);
            }
        }

        return merged;
    }

    private void AddSeries(
        List<FieldCandidate> result,
        IEnumerable<IReadOnlyList<SeriesEntry>> values,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        _ = diagnostics;
        var merged = new List<SeriesEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceValues in values)
        {
            foreach (var sourceValue in sourceValues)
            {
                var normalized = new SeriesEntry(
                    _normalization.NormalizeText(sourceValue.Name),
                    _normalization.NormalizeText(sourceValue.Part));
                if (normalized.Name.Length == 0)
                    continue;

                var comparisonKey =
                    _normalization.NormalizeForComparison(ProjectMetadataField.Series, normalized.Name) +
                    "\u001E" +
                    _normalization.NormalizeText(normalized.Part);
                if (!seen.Add(comparisonKey))
                    continue;

                merged.Add(normalized);
            }
        }

        if (merged.Count > 0)
            result.Add(new FieldCandidate(ProjectMetadataField.Series, merged));
    }

    private void AddMetadataDate(
        List<FieldCandidate> result,
        ProjectMetadataField field,
        IEnumerable<MetadataDate?> values,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        var selected = TrySelectBestValue(
            field,
            values.Where(value => value is not null).Cast<object>(),
            value => ((MetadataDate)value).ToString(),
            diagnostics);
        if (selected?.Value is MetadataDate date)
            result.Add(new FieldCandidate(field, date));
    }

    private void AddInteger(
        List<FieldCandidate> result,
        ProjectMetadataField field,
        IEnumerable<int?> values,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        var selected = TrySelectBestValue(
            field,
            values.Where(value => value is not null).Cast<object>(),
            value => ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture),
            diagnostics);
        if (selected?.Value is int integer)
            result.Add(new FieldCandidate(field, integer));
    }

    private void AddEnum<T>(
        List<FieldCandidate> result,
        ProjectMetadataField field,
        IEnumerable<T?> values,
        List<MetadataAggregationDiagnostic> diagnostics)
        where T : struct, Enum
    {
        var selected = TrySelectBestValue(
            field,
            values.Where(value => value is not null).Cast<object>(),
            value => value.ToString() ?? string.Empty,
            diagnostics);
        if (selected?.Value is T typed)
            result.Add(new FieldCandidate(field, typed));
    }

    private CandidateSelection? TrySelectBestValue(
        ProjectMetadataField field,
        IEnumerable<object> values,
        Func<object, string> keySelector,
        List<MetadataAggregationDiagnostic> diagnostics)
    {
        var indexed = values
            .Select((value, index) => new CandidateValue(value, keySelector(value), index))
            .Where(item => item.Key.Length > 0)
            .ToArray();
        if (indexed.Length == 0)
            return null;

        var groups = indexed
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Select(group => new CandidateGroup(group.Key, group.Count(), group.Min(item => item.Index), group.First().Value))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.FirstIndex)
            .ToArray();

        if (groups.Length > 1 && groups[0].Count == groups[1].Count)
        {
            diagnostics.Add(new MetadataAggregationDiagnostic(
                MetadataAggregationDiagnosticCode.AmbiguousMajority,
                field,
                $"Mehrdeutige SourceMetadata-Mehrheit für {field}; erster stabiler Wert wurde verwendet."));
        }

        return new CandidateSelection(
            groups[0].Value,
            groups[0].Count,
            indexed.Length,
            groups.Length > 1 ? groups[1] : null);
    }

    private FieldCandidate? ApplySuppressedValues(ProjectMetadata metadata, FieldCandidate candidate)
    {
        var suppressed = metadata.GetSuppressedValues(candidate.Field);
        if (suppressed.Count == 0)
            return candidate;

        var suppressedKeys = suppressed
            .Select(value => _normalization.NormalizeForComparison(candidate.Field, value))
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        if (candidate.Value is IReadOnlyList<string> list)
        {
            var filtered = list
                .Where(value => !suppressedKeys.Contains(
                    _normalization.NormalizeForComparison(candidate.Field, value)))
                .ToArray();
            return filtered.Length == 0 ? null : candidate with { Value = filtered };
        }

        if (candidate.Value is string scalar &&
            suppressedKeys.Contains(_normalization.NormalizeForComparison(candidate.Field, scalar)))
        {
            return null;
        }

        if (candidate.Value is int integer &&
            suppressedKeys.Contains(_normalization.NormalizeForComparison(
                candidate.Field, integer.ToString(System.Globalization.CultureInfo.InvariantCulture))))
        {
            return null;
        }

        return candidate;
    }

    private static bool CanApply(
        ProjectMetadata metadata,
        ProjectMetadataField field,
        ProjectMetadataInitializationState state,
        ProjectMetadataInitializationOptions options)
    {
        if (state == ProjectMetadataInitializationState.InitialSetComplete)
        {
            if (field == ProjectMetadataField.Genres &&
                IsReplaceableGenreSeed(metadata, options.ReplaceableGenreSeed))
            {
                return true;
            }

            return IsUnset(metadata, field);
        }

        // After the initial source set has been finalized, existing project values are
        // stable canonical decisions. Later sources may only fill a truly unset field;
        // they never rebuild an existing scalar, list or series value.
        return IsUnset(metadata, field);
    }

    private static bool IsReplaceableGenreSeed(ProjectMetadata metadata, string? replaceableGenreSeed)
    {
        var seed = replaceableGenreSeed?.Trim() ?? string.Empty;
        if (seed.Length == 0)
            return false;

        var current = metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value;
        return current is { Count: 1 } &&
               string.Equals(current[0]?.Trim(), seed, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnset(ProjectMetadata metadata, ProjectMetadataField field) =>
        ProjectMetadataFields.GetValueKind(field) switch
        {
            ProjectMetadataFieldValueKind.String => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.StringList => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.SeriesList => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.MetadataDate => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.Integer => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.AudioWorkType => IsNoneAndNull(metadata, field),
            ProjectMetadataFieldValueKind.AbridgedStatus => IsNoneAndNull(metadata, field),
            _ => false
        };

    private static bool IsNoneAndNull(ProjectMetadata metadata, ProjectMetadataField field) =>
        ProjectMetadataFields.GetValueKind(field) switch
        {
            ProjectMetadataFieldValueKind.String => metadata.GetValue<string>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.StringList => metadata.GetValue<IReadOnlyList<string>>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.SeriesList => metadata.GetValue<IReadOnlyList<SeriesEntry>>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.MetadataDate => metadata.GetValue<MetadataDate?>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.Integer => metadata.GetValue<int?>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.AudioWorkType => metadata.GetValue<AudioWorkType?>(field) is { Origin: MetadataOrigin.None, Value: null },
            ProjectMetadataFieldValueKind.AbridgedStatus => metadata.GetValue<AbridgedStatus?>(field) is { Origin: MetadataOrigin.None, Value: null },
            _ => false
        };

    private static bool SetField(ProjectMetadata metadata, ProjectMetadataField field, object value, MetadataOrigin origin) =>
        ProjectMetadataFields.GetValueKind(field) switch
        {
            ProjectMetadataFieldValueKind.String => metadata.SetValue(field, (string)value, origin),
            ProjectMetadataFieldValueKind.StringList => metadata.SetValue<IReadOnlyList<string>>(field, (IReadOnlyList<string>)value, origin),
            ProjectMetadataFieldValueKind.SeriesList => metadata.SetValue<IReadOnlyList<SeriesEntry>>(field, (IReadOnlyList<SeriesEntry>)value, origin),
            ProjectMetadataFieldValueKind.MetadataDate => metadata.SetValue(field, (MetadataDate?)value, origin),
            ProjectMetadataFieldValueKind.Integer => metadata.SetValue(field, (int?)value, origin),
            ProjectMetadataFieldValueKind.AudioWorkType => metadata.SetValue(field, (AudioWorkType?)value, origin),
            ProjectMetadataFieldValueKind.AbridgedStatus => metadata.SetValue(field, (AbridgedStatus?)value, origin),
            _ => false
        };

    private static IReadOnlyList<MetadataFieldDecisionDiagnostic> BuildFieldDecisions(
        ProjectMetadata metadata,
        IReadOnlyList<FieldCandidate> candidates,
        IReadOnlyCollection<ProjectMetadataField> appliedFields)
    {
        var result = new List<MetadataFieldDecisionDiagnostic>();
        foreach (var field in new[]
                 {
                     ProjectMetadataField.Title,
                     ProjectMetadataField.Album,
                     ProjectMetadataField.Authors,
                     ProjectMetadataField.Narrators
                 })
        {
            var (canonicalValue, origin) = GetCanonicalDisplayValue(metadata, field);
            var candidate = candidates.FirstOrDefault(item => item.Field == field);
            var wasApplied = appliedFields.Contains(field);
            MetadataDecisionCandidateDiagnostic? winner;
            MetadataDecisionCandidateDiagnostic? runnerUp;
            string reason;

            if (origin == MetadataOrigin.User)
            {
                winner = new MetadataDecisionCandidateDiagnostic(
                    canonicalValue,
                    MetadataDecisionEvidenceType.UserOverride,
                    null,
                    null,
                    null);
                runnerUp = candidate?.Evidence is null
                    ? null
                    : candidate.Evidence with { RejectedReason = "Durch kanonischen Benutzerwert geschützt." };
                reason = "Kanonischer Benutzerwert hat Vorrang vor automatischer Metadatenaggregation.";
            }
            else if (wasApplied && candidate is not null)
            {
                winner = candidate.Evidence ?? CreateExistingEvidence(candidate);
                runnerUp = winner.RunnerUp;
                reason = DescribeAppliedReason(winner.EvidenceType);
            }
            else if (canonicalValue.Length > 0)
            {
                winner = new MetadataDecisionCandidateDiagnostic(
                    canonicalValue,
                    EvidenceTypeForOrigin(origin),
                    null,
                    null,
                    null);
                runnerUp = candidate?.Evidence is null
                    ? null
                    : candidate.Evidence with { RejectedReason = "Bestehender kanonischer Wert bleibt stabil." };
                reason = "Bestehender kanonischer Wert wurde nicht durch spätere Quellen ersetzt.";
            }
            else
            {
                winner = null;
                runnerUp = candidate?.Evidence is null
                    ? null
                    : candidate.Evidence with { RejectedReason = "Im aktuellen Initialisierungszustand nicht angewendet." };
                reason = candidate is null
                    ? "Keine ausreichende verwertbare Source-Evidence vorhanden."
                    : "Automatischer Kandidat wurde im aktuellen Initialisierungszustand nicht angewendet.";
            }

            result.Add(new MetadataFieldDecisionDiagnostic(
                field,
                canonicalValue,
                origin,
                reason,
                winner,
                runnerUp));
        }

        return result;
    }

    private static (string Value, MetadataOrigin Origin) GetCanonicalDisplayValue(
        ProjectMetadata metadata,
        ProjectMetadataField field) =>
        ProjectMetadataFields.GetValueKind(field) switch
        {
            ProjectMetadataFieldValueKind.String => ToDisplay(metadata.GetValue<string>(field)),
            ProjectMetadataFieldValueKind.StringList => ToDisplay(metadata.GetValue<IReadOnlyList<string>>(field)),
            _ => (string.Empty, MetadataOrigin.None)
        };

    private static (string Value, MetadataOrigin Origin) ToDisplay(MetadataValue<string> value) =>
        (value.Value?.Trim() ?? string.Empty, value.Origin);

    private static (string Value, MetadataOrigin Origin) ToDisplay(MetadataValue<IReadOnlyList<string>> value) =>
        (value.Value is null ? string.Empty : string.Join("; ", value.Value), value.Origin);

    private static MetadataDecisionCandidateDiagnostic CreateExistingEvidence(FieldCandidate candidate) =>
        new(
            FormatCandidateValue(candidate.Value),
            EvidenceTypeForOrigin(candidate.Origin),
            null,
            null,
            null);

    private static MetadataDecisionEvidenceType EvidenceTypeForOrigin(MetadataOrigin origin) => origin switch
    {
        MetadataOrigin.User => MetadataDecisionEvidenceType.UserOverride,
        MetadataOrigin.FolderStructure => MetadataDecisionEvidenceType.FolderStructure,
        MetadataOrigin.SingleFileName => MetadataDecisionEvidenceType.SingleFileName,
        MetadataOrigin.Derived => MetadataDecisionEvidenceType.FilenameAggregationFallback,
        _ => MetadataDecisionEvidenceType.ExistingCanonical
    };

    private static string DescribeAppliedReason(MetadataDecisionEvidenceType evidenceType) => evidenceType switch
    {
        MetadataDecisionEvidenceType.DirectTitleAggregation => "Direkte Title-Evidence wurde nach Support ausgewählt.",
        MetadataDecisionEvidenceType.DirectAlbumAggregation => "Direkte Album-Evidence wurde nach Support ausgewählt.",
        MetadataDecisionEvidenceType.AlbumFallback => "Keine tragfähige Title-Evidence; Album wurde als Title-Fallback verwendet.",
        MetadataDecisionEvidenceType.OrderedDistinctSourceAggregation => "Mehrfachwerte wurden in stabiler Quellenreihenfolge zusammengeführt.",
        MetadataDecisionEvidenceType.FilenameAggregationFallback => "Keine stärkere Metadaten-Evidence; einheitliche Source-Dateinamen wurden als Title-Fallback verwendet.",
        MetadataDecisionEvidenceType.FolderStructure => "Keine stärkere Source-Evidence; Ordnerstruktur wurde verwendet.",
        MetadataDecisionEvidenceType.SingleFileName => "Keine stärkere Source-Evidence; Einzeldateiname wurde verwendet.",
        _ => "Kanonischer Wert wurde aus der verfügbaren Evidence übernommen."
    };

    private static MetadataDecisionCandidateDiagnostic CreateTitleEvidence(
        ProjectTitleSourceEvidence evidence,
        MetadataDecisionEvidenceType evidenceType)
    {
        var runnerUp = evidence.RunnerUp is null
            ? null
            : new MetadataDecisionCandidateDiagnostic(
                evidence.RunnerUp.Value,
                evidenceType,
                evidence.RunnerUp.SourceCount,
                evidence.CandidateCount,
                Ratio(evidence.RunnerUp.SourceCount, evidence.CandidateCount),
                "Geringerer Support als der Gewinner.");
        return new MetadataDecisionCandidateDiagnostic(
            evidence.Value,
            evidenceType,
            evidence.SourceCount,
            evidence.CandidateCount,
            Ratio(evidence.SourceCount, evidence.CandidateCount),
            RunnerUp: runnerUp);
    }

    private static MetadataDecisionCandidateDiagnostic CreateCandidateEvidence(
        CandidateSelection selection,
        MetadataDecisionEvidenceType evidenceType)
    {
        var runnerUp = selection.RunnerUp is null
            ? null
            : new MetadataDecisionCandidateDiagnostic(
                FormatCandidateValue(selection.RunnerUp.Value),
                evidenceType,
                selection.RunnerUp.Count,
                selection.EvidenceCount,
                Ratio(selection.RunnerUp.Count, selection.EvidenceCount),
                "Geringerer Support oder spätere stabile Quellenposition.");
        return new MetadataDecisionCandidateDiagnostic(
            FormatCandidateValue(selection.Value),
            evidenceType,
            selection.SupportCount,
            selection.EvidenceCount,
            Ratio(selection.SupportCount, selection.EvidenceCount),
            RunnerUp: runnerUp);
    }

    private static MetadataDecisionCandidateDiagnostic CreateSingleEvidence(
        string value,
        MetadataDecisionEvidenceType evidenceType) =>
        new(value, evidenceType, 1, 1, 1d);

    private static string FormatCandidateValue(object value) => value switch
    {
        IReadOnlyList<string> items => string.Join("; ", items),
        _ => value.ToString() ?? string.Empty
    };

    private static double? Ratio(int supportCount, int evidenceCount) =>
        evidenceCount > 0 ? (double)supportCount / evidenceCount : null;

    private static bool HasCandidate(IEnumerable<FieldCandidate> candidates, ProjectMetadataField field) =>
        candidates.Any(candidate => candidate.Field == field);

    private static bool IsTechnicalFolderName(string value) =>
        ChapterMarkerCatalog.IsTechnicalStructureName(value);

    private IEnumerable<MetadataAggregationDiagnostic> DetectStrongIdentityConflicts(
        IReadOnlyList<SourceMetadataSnapshot> snapshots)
    {
        var identifiers = snapshots
            .Select(snapshot => new
            {
                snapshot.CanonicalFields.Barcode,
                snapshot.CanonicalFields.ISBN,
                snapshot.CanonicalFields.ASIN,
                snapshot.CanonicalFields.Album,
                snapshot.CanonicalFields.Authors,
                snapshot.CanonicalFields.Series
            })
            .ToArray();

        foreach (var field in new[] { ProjectMetadataField.Barcode, ProjectMetadataField.ISBN, ProjectMetadataField.ASIN })
        {
            var groups = identifiers
                .Select(item => field switch
                {
                    ProjectMetadataField.Barcode => item.Barcode,
                    ProjectMetadataField.ISBN => item.ISBN,
                    ProjectMetadataField.ASIN => item.ASIN,
                    _ => string.Empty
                })
                .Select(value => _normalization.NormalizeForComparison(field, value))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (groups.Length > 1 && HasAdditionalWorkEvidenceConflict(snapshots))
            {
                yield return new MetadataAggregationDiagnostic(
                    MetadataAggregationDiagnosticCode.StrongIdentityConflict,
                    field,
                    $"Starker Identitätskonflikt in SourceMetadata: widersprüchliche {field}-Evidence.");
            }
        }

        var conflictCount = 0;
        if (DistinctNonEmpty(snapshots.Select(snapshot => snapshot.CanonicalFields.Album), ProjectMetadataField.Album) > 1)
            conflictCount++;
        if (DistinctNonEmpty(snapshots.SelectMany(snapshot => snapshot.CanonicalFields.Authors), ProjectMetadataField.Authors) > 1)
            conflictCount++;
        if (DistinctNonEmpty(snapshots.SelectMany(snapshot => snapshot.CanonicalFields.Series.Select(series => series.Name)), ProjectMetadataField.Series) > 1)
            conflictCount++;

        if (conflictCount >= 2)
        {
            yield return new MetadataAggregationDiagnostic(
                MetadataAggregationDiagnosticCode.StrongIdentityConflict,
                null,
                "Starker Identitätskonflikt in SourceMetadata: mehrere Werkmerkmale widersprechen sich.");
        }
    }

    private bool HasAdditionalWorkEvidenceConflict(IReadOnlyList<SourceMetadataSnapshot> snapshots) =>
        DistinctNonEmpty(snapshots.Select(snapshot => snapshot.CanonicalFields.Album), ProjectMetadataField.Album) > 1 ||
        DistinctNonEmpty(snapshots.SelectMany(snapshot => snapshot.CanonicalFields.Authors), ProjectMetadataField.Authors) > 1 ||
        DistinctNonEmpty(snapshots.SelectMany(snapshot => snapshot.CanonicalFields.Series.Select(series => series.Name)), ProjectMetadataField.Series) > 1;

    private int DistinctNonEmpty(IEnumerable<string> values, ProjectMetadataField field) =>
        values
            .Select(value => _normalization.NormalizeForComparison(field, value))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private sealed record FieldCandidate(
        ProjectMetadataField Field,
        object Value,
        MetadataOrigin Origin = MetadataOrigin.SourceAggregation,
        MetadataDecisionCandidateDiagnostic? Evidence = null);
    private sealed record CandidateValue(object Value, string Key, int Index);
    private sealed record CandidateGroup(string Key, int Count, int FirstIndex, object Value);
    private sealed record CandidateSelection(
        object Value,
        int SupportCount,
        int EvidenceCount,
        CandidateGroup? RunnerUp);

}
