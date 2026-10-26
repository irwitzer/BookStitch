using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConceptMetadataCandidate(
    ProjectMetadataField Field,
    string DisplayValue,
    object ValueToCommit,
    int SourceCount,
    MetadataOrigin EvidenceOrigin,
    bool IsCanonicalWinner = false,
    bool IsAutomaticWinner = false)
{
    public bool HasSourceEvidence =>
        SourceCount > 0 && ConceptCandidateEvidence.IsSourceEvidence(EvidenceOrigin);

    public bool ShowSourceCount => HasSourceEvidence;
    public string EvidenceLabel =>
        MetadataOriginEvidenceLabels.Format(EvidenceOrigin) + (IsAutomaticWinner ? " ★" : string.Empty);
}

public static class ConceptCandidateEvidence
{
    public static bool IsSourceEvidence(MetadataOrigin origin) => origin is
        MetadataOrigin.SourceAggregation or
        MetadataOrigin.FolderStructure or
        MetadataOrigin.SingleFileName or
        MetadataOrigin.Derived or
        MetadataOrigin.LegacyMigrated;
}

public sealed class ConceptCandidateSessionService
{
    private readonly MetadataNormalizationService _normalization;
    private readonly ScalarCandidatePresentationService _scalarPresentation;
    private readonly Dictionary<ProjectMetadataField, Dictionary<string, ConceptMetadataCandidate>> _retainedCandidates = [];
    private readonly Dictionary<ProjectMetadataField, string> _rememberedScalarAutomaticWinners = [];

    public ConceptCandidateSessionService(
        MetadataNormalizationService? normalization = null,
        ScalarCandidatePresentationService? scalarPresentation = null)
    {
        _normalization = normalization ?? new MetadataNormalizationService();
        _scalarPresentation = scalarPresentation ?? new ScalarCandidatePresentationService(_normalization);
    }

    public void Reset()
    {
        _retainedCandidates.Clear();
        _rememberedScalarAutomaticWinners.Clear();
    }

    public void RetainCurrentScalarWinners(
        ProjectMetadataField field,
        IEnumerable<ConceptMetadataCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var retained = GetRetained(field);
        foreach (var candidate in candidates.Where(candidate => candidate.IsAutomaticWinner || candidate.IsCanonicalWinner))
        {
            var key = GetIdentityKey(field, candidate.ValueToCommit);
            if (key.Length == 0)
                continue;

            retained[key] = candidate with { IsCanonicalWinner = false };
        }
    }

    public void RetainUserValue(
        ProjectMetadataField field,
        object value,
        string? displayValue = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        object valueToCommit = value;
        var display = displayValue ?? FormatValue(value);

        if (value is string text)
        {
            var normalizedValue = _normalization.NormalizeText(text);
            if (IsLanguageField(field) &&
                MetadataPreferencesService.TryCanonicalizeLanguage(normalizedValue, out var canonicalLanguage))
            {
                normalizedValue = canonicalLanguage;
            }

            valueToCommit = normalizedValue;
            display = displayValue ?? normalizedValue;
        }

        var key = GetIdentityKey(field, valueToCommit);
        if (key.Length == 0)
            return;

        var retained = GetRetained(field);
        var byKey = new Dictionary<string, ConceptMetadataCandidate>(retained, StringComparer.Ordinal);
        MergeCandidate(
            field,
            byKey,
            new ConceptMetadataCandidate(
                field,
                display,
                valueToCommit,
                0,
                MetadataOrigin.User),
            null);
        retained.Clear();
        foreach (var pair in byKey)
            retained[pair.Key] = pair.Value;
    }

    public IReadOnlyList<ConceptMetadataCandidate> PresentScalar(
        ProjectMetadataField field,
        IReadOnlyList<ScalarSourceMetadataAlternative> sourceCandidates,
        MetadataValue<string> canonical,
        IReadOnlyList<MetadataFieldDecisionDiagnostic> decisions,
        IEnumerable<ConceptMetadataCandidate>? staticCandidates = null,
        IEnumerable<ConceptMetadataCandidate>? userCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidates);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(decisions);

        var retained = GetRetained(field);
        var byKey = new Dictionary<string, ConceptMetadataCandidate>(StringComparer.Ordinal);
        var sourceOrder = new List<string>();

        foreach (var source in sourceCandidates)
        {
            var candidate = FromScalarSource(source);
            MergeCandidate(field, byKey, candidate, sourceOrder);
        }

        MergeAdditional(field, byKey, staticCandidates, sourceOrder);

        var retainedOrder = new List<string>();
        foreach (var candidate in retained.Values)
        {
            var key = GetIdentityKey(field, candidate.ValueToCommit);
            if (key.Length == 0)
                continue;

            if (!retainedOrder.Contains(key, StringComparer.Ordinal))
                retainedOrder.Add(key);
            MergeCandidate(field, byKey, candidate with { IsCanonicalWinner = false }, null);
        }

        MergeAdditional(field, byKey, userCandidates, retainedOrder);

        var winnerKey = GetIdentityKey(field, canonical.Value);
        if (winnerKey.Length > 0)
        {
            if (byKey.TryGetValue(winnerKey, out var existing))
            {
                var winner = existing with { IsCanonicalWinner = true };
                byKey[winnerKey] = winner;
                retained[winnerKey] = winner;
            }
            else if (canonical.Origin != MetadataOrigin.None)
            {
                var displayValue = _normalization.NormalizeText(canonical.Value);
                if (displayValue.Length > 0)
                {
                    var winner = new ConceptMetadataCandidate(
                        field,
                        displayValue,
                        displayValue,
                        0,
                        canonical.Origin,
                        IsCanonicalWinner: true);
                    byKey[winnerKey] = winner;
                    retained[winnerKey] = winner;
                }
            }
        }

        var stableOrder = sourceOrder
            .Concat(retainedOrder)
            .Concat(winnerKey.Length > 0 ? [winnerKey] : [])
            .Distinct(StringComparer.Ordinal)
            .Where(byKey.ContainsKey)
            .Select(key => byKey[key])
            .ToArray();

        _rememberedScalarAutomaticWinners.TryGetValue(field, out var rememberedAutomaticWinner);
        var scalarProjection = stableOrder
            .Select(candidate => new ScalarSourceMetadataAlternative(
                field,
                candidate.ValueToCommit,
                candidate.SourceCount,
                candidate.DisplayValue)
            {
                EvidenceOrigin = candidate.EvidenceOrigin,
                IsCanonicalWinner = candidate.IsCanonicalWinner,
                IsAutomaticWinner = candidate.IsAutomaticWinner
            })
            .ToArray();

        var automaticWinner = _scalarPresentation.ResolveAutomaticWinnerValue(
            field,
            canonical,
            decisions,
            scalarProjection,
            rememberedAutomaticWinner);

        if (!string.IsNullOrWhiteSpace(automaticWinner))
            _rememberedScalarAutomaticWinners[field] = automaticWinner;

        var sorted = _scalarPresentation.ApplyAutomaticWinnerAndSort(
            field,
            canonical,
            scalarProjection,
            automaticWinner);

        return sorted
            .Select(projected =>
            {
                var key = GetIdentityKey(field, projected.Value);
                var original = byKey[key];
                return original with
                {
                    IsCanonicalWinner = projected.IsCanonicalWinner,
                    IsAutomaticWinner = projected.IsAutomaticWinner
                };
            })
            .ToArray();
    }

    public IReadOnlyList<ConceptMetadataCandidate> PresentValueScalar<T>(
        ProjectMetadataField field,
        IReadOnlyList<ScalarSourceMetadataAlternative> sourceCandidates,
        MetadataValue<T> canonical,
        IEnumerable<ConceptMetadataCandidate>? staticCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidates);
        ArgumentNullException.ThrowIfNull(canonical);

        var retained = GetRetained(field);
        var byKey = new Dictionary<string, ConceptMetadataCandidate>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var source in sourceCandidates)
            MergeCandidate(field, byKey, FromScalarSource(source), order);

        MergeAdditional(field, byKey, staticCandidates, order);

        foreach (var candidate in retained.Values)
            MergeCandidate(field, byKey, candidate with { IsCanonicalWinner = false }, order);

        var winnerKey = GetIdentityKey(field, canonical.Value);
        if (winnerKey.Length > 0)
        {
            if (byKey.TryGetValue(winnerKey, out var existing))
            {
                var winner = existing with
                {
                    IsCanonicalWinner = true,
                    IsAutomaticWinner = existing.IsAutomaticWinner ||
                                        (existing.SourceCount > 0 && ConceptCandidateEvidence.IsSourceEvidence(existing.EvidenceOrigin))
                };
                byKey[winnerKey] = winner;
                retained[winnerKey] = winner;
            }
            else if (canonical.Origin != MetadataOrigin.None && canonical.Value is not null)
            {
                var displayValue = FormatValue(canonical.Value);
                var winner = new ConceptMetadataCandidate(
                    field,
                    displayValue,
                    canonical.Value,
                    0,
                    canonical.Origin,
                    IsCanonicalWinner: true);
                byKey[winnerKey] = winner;
                retained[winnerKey] = winner;
                if (!order.Contains(winnerKey, StringComparer.Ordinal))
                    order.Add(winnerKey);
            }
        }

        return order
            .Where(byKey.ContainsKey)
            .Select(key => byKey[key])
            .Select((candidate, index) => new { candidate, index })
            .OrderByDescending(item => item.candidate.IsCanonicalWinner)
            .ThenByDescending(item => item.candidate.IsAutomaticWinner)
            .ThenByDescending(item => item.candidate.SourceCount)
            .ThenBy(item => item.index)
            .Select(item => item.candidate)
            .ToArray();
    }

    public IReadOnlyList<ConceptMetadataCandidate> PresentList(
        ProjectMetadataField field,
        IReadOnlyList<ListSourceMetadataAlternative> sourceCandidates,
        MetadataValue<IReadOnlyList<string>> canonical,
        IEnumerable<ConceptMetadataCandidate>? staticCandidates = null,
        IEnumerable<ConceptMetadataCandidate>? userCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(sourceCandidates);
        ArgumentNullException.ThrowIfNull(canonical);

        var retained = GetRetained(field);
        var byKey = new Dictionary<string, ConceptMetadataCandidate>(StringComparer.Ordinal);
        var sourceOrder = new List<string>();

        foreach (var source in sourceCandidates)
        {
            var value = source.Values.FirstOrDefault() ?? source.DisplayValue;
            if (IsLanguageField(field) &&
                MetadataPreferencesService.TryCanonicalizeLanguage(value, out var canonicalLanguage))
            {
                value = canonicalLanguage;
            }

            var candidate = new ConceptMetadataCandidate(
                field,
                source.DisplayValue,
                value,
                source.SourceCount,
                source.EvidenceOrigin,
                source.IsCanonicalWinner,
                source.IsAutomaticWinner);
            MergeCandidate(field, byKey, candidate, sourceOrder);
        }

        MergeAdditional(field, byKey, staticCandidates, sourceOrder);

        foreach (var candidate in retained.Values)
            MergeCandidate(field, byKey, candidate with { IsCanonicalWinner = false }, sourceOrder);

        MergeAdditional(field, byKey, userCandidates, sourceOrder);

        var winnerKeys = new List<string>();
        foreach (var rawValue in canonical.Value ?? [])
        {
            var displayValue = _normalization.NormalizeText(rawValue);
            var key = GetIdentityKey(field, displayValue);
            if (key.Length == 0 || winnerKeys.Contains(key, StringComparer.Ordinal))
                continue;

            winnerKeys.Add(key);
            if (byKey.TryGetValue(key, out var existing))
            {
                var winner = existing with { IsCanonicalWinner = true };
                byKey[key] = winner;
                retained[key] = winner;
            }
            else if (canonical.Origin != MetadataOrigin.None)
            {
                var winner = new ConceptMetadataCandidate(
                    field,
                    displayValue,
                    displayValue,
                    0,
                    canonical.Origin,
                    IsCanonicalWinner: true);
                byKey[key] = winner;
                retained[key] = winner;
            }
        }

        return winnerKeys
            .Where(byKey.ContainsKey)
            .Select(key => byKey[key])
            .Concat(sourceOrder
                .Where(key => !winnerKeys.Contains(key, StringComparer.Ordinal))
                .Where(byKey.ContainsKey)
                .Select(key => byKey[key]))
            .Select((candidate, index) => new { candidate, index })
            .OrderByDescending(item => item.candidate.IsAutomaticWinner)
            .ThenBy(item => item.index)
            .Select(item => item.candidate)
            .ToArray();
    }

    private void MergeAdditional(
        ProjectMetadataField field,
        Dictionary<string, ConceptMetadataCandidate> byKey,
        IEnumerable<ConceptMetadataCandidate>? candidates,
        List<string>? order)
    {
        foreach (var candidate in candidates ?? [])
        {
            if (candidate.Field != field)
                throw new ArgumentException($"Candidate field '{candidate.Field}' does not match '{field}'.", nameof(candidates));
            MergeCandidate(field, byKey, candidate, order);
        }
    }

    private void MergeCandidate(
        ProjectMetadataField field,
        Dictionary<string, ConceptMetadataCandidate> byKey,
        ConceptMetadataCandidate incoming,
        List<string>? order)
    {
        var key = GetIdentityKey(field, incoming.ValueToCommit);
        if (key.Length == 0)
            return;

        if (order is not null && !order.Contains(key, StringComparer.Ordinal))
            order.Add(key);

        if (!byKey.TryGetValue(key, out var existing))
        {
            byKey[key] = incoming;
            return;
        }

        var preferredEvidence = PreferEvidence(existing, incoming);
        var displayValue = PreferDisplayValue(existing, incoming);
        byKey[key] = preferredEvidence with
        {
            DisplayValue = displayValue,
            SourceCount = Math.Max(existing.SourceCount, incoming.SourceCount),
            IsCanonicalWinner = existing.IsCanonicalWinner || incoming.IsCanonicalWinner,
            IsAutomaticWinner = existing.IsAutomaticWinner || incoming.IsAutomaticWinner
        };
    }

    private static ConceptMetadataCandidate PreferEvidence(
        ConceptMetadataCandidate existing,
        ConceptMetadataCandidate incoming)
    {
        var existingIsSource = ConceptCandidateEvidence.IsSourceEvidence(existing.EvidenceOrigin);
        var incomingIsSource = ConceptCandidateEvidence.IsSourceEvidence(incoming.EvidenceOrigin);
        if (existingIsSource != incomingIsSource)
            return existingIsSource ? existing : incoming;

        if (existing.EvidenceOrigin == MetadataOrigin.StaticList && incoming.EvidenceOrigin != MetadataOrigin.StaticList)
            return incoming;

        return existing;
    }

    private static string PreferDisplayValue(
        ConceptMetadataCandidate existing,
        ConceptMetadataCandidate incoming)
    {
        if (existing.EvidenceOrigin == MetadataOrigin.StaticList &&
            incoming.EvidenceOrigin != MetadataOrigin.StaticList)
            return existing.DisplayValue;

        if (incoming.EvidenceOrigin == MetadataOrigin.StaticList &&
            existing.EvidenceOrigin != MetadataOrigin.StaticList)
            return incoming.DisplayValue;

        return existing.DisplayValue;
    }


    private ConceptMetadataCandidate FromScalarSource(ScalarSourceMetadataAlternative source) =>
        new(
            source.Field,
            source.DisplayValue,
            source.Value,
            source.SourceCount,
            source.EvidenceOrigin,
            source.IsCanonicalWinner,
            source.IsAutomaticWinner);

    private static string FormatValue(object value) => value switch
    {
        MetadataDate date => MetadataDateParser.FormatDisplay(date),
        AudioWorkType workType => MetadataDisplayLabels.AudioWorkType(workType),
        AbridgedStatus status => MetadataDisplayLabels.AbridgedStatus(status),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.CurrentCulture),
        _ => value.ToString() ?? string.Empty
    };

    private string GetIdentityKey(ProjectMetadataField field, object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
        {
            if (IsLanguageField(field) &&
                MetadataPreferencesService.TryCanonicalizeLanguage(text, out var canonicalLanguage))
            {
                return canonicalLanguage;
            }

            return _normalization.NormalizeForComparison(field, text);
        }

        return $"{value.GetType().FullName}:{value}";
    }

    private static bool IsLanguageField(ProjectMetadataField field) => field is
        ProjectMetadataField.Languages or
        ProjectMetadataField.OriginalLanguages or
        ProjectMetadataField.TargetLanguages;

    private Dictionary<string, ConceptMetadataCandidate> GetRetained(ProjectMetadataField field)
    {
        if (_retainedCandidates.TryGetValue(field, out var retained))
            return retained;

        retained = new Dictionary<string, ConceptMetadataCandidate>(StringComparer.Ordinal);
        _retainedCandidates[field] = retained;
        return retained;
    }
}
