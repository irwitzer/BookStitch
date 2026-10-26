using BookStitch.Models.TrackOrdering;

namespace BookStitch.Services.TrackOrdering;

public sealed class AutomaticTrackOrderService
{
    public const int AlgorithmVersion = 2;

    private readonly TrackOrderCandidateExtractor _candidateExtractor;
    private readonly NaturalStringComparer _naturalComparer;

    public AutomaticTrackOrderService()
        : this(new TrackOrderCandidateExtractor(), new NaturalStringComparer())
    {
    }

    public AutomaticTrackOrderService(
        TrackOrderCandidateExtractor candidateExtractor,
        NaturalStringComparer naturalComparer)
    {
        _candidateExtractor = candidateExtractor ?? throw new ArgumentNullException(nameof(candidateExtractor));
        _naturalComparer = naturalComparer ?? throw new ArgumentNullException(nameof(naturalComparer));
    }

    public AutomaticTrackOrderResult Analyze(AutomaticTrackOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tracks = request.Tracks.ToList();
        if (tracks.Count == 0)
            return new AutomaticTrackOrderResult([], TrackOrderConfidence.Unresolved, []);

        var fallbackOrder = CreateFallbackOrder(tracks);
        if (tracks.Count == 1)
        {
            return new AutomaticTrackOrderResult(
                [new AutomaticTrackOrderItemResult(tracks[0].TrackKey, 1, TrackOrderConfidence.VeryHigh)],
                TrackOrderConfidence.VeryHigh,
                []);
        }

        var candidates = _candidateExtractor.Extract(request)
            .ToDictionary(item => item.TrackKey, StringComparer.OrdinalIgnoreCase);
        var models = BuildModels(request, candidates);
        var diagnostics = CreateInputDiagnostics(tracks, candidates);

        if (models.Count == 0)
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.FallbackOrderUsed,
                TrackOrderDiagnosticSeverity.Info,
                null,
                "Keine belastbare Sortier-Evidence gefunden; Natural-Path-Fallback verwendet."));
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.LowOrderConfidence,
                TrackOrderDiagnosticSeverity.Warning,
                null,
                "Die automatische Reihenfolge basiert nur auf deterministischem Fallback."));
            return CreateResult(fallbackOrder, TrackOrderConfidence.Unresolved, diagnostics);
        }

        var groups = MergeCompatibleModels(models);
        if (groups.Count == 0)
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.FallbackOrderUsed,
                TrackOrderDiagnosticSeverity.Info,
                null,
                "Mehrdeutige Sortier-Evidence wurde verworfen; Natural-Path-Fallback verwendet."));
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.LowOrderConfidence,
                TrackOrderDiagnosticSeverity.Warning,
                null,
                "Die automatische Reihenfolge basiert nur auf deterministischem Fallback."));
            return CreateResult(fallbackOrder, TrackOrderConfidence.Unresolved, diagnostics);
        }

        var conflictExists = groups.Count > 1 && groups.SelectMany(group => group.Models).Any();
        if (conflictExists)
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.ConflictingTrackOrder,
                TrackOrderDiagnosticSeverity.Warning,
                null,
                "Mindestens zwei Quellen widersprechen sich in der relativen Trackreihenfolge."));
        }

        var best = groups
            .OrderByDescending(group => group.HasAuthority)
            .ThenByDescending(group => group.IndependentSourceCount)
            .ThenByDescending(group => group.Coverage)
            .ThenBy(group => group.AmbiguousTieCount)
            .ThenByDescending(group => group.Specificity)
            .ThenByDescending(group => group.Regularity)
            .First();

        var ordered = OrderByModelGroup(best, fallbackOrder);
        var fallbackShare = CalculateFallbackShare(best, tracks.Count);
        if (fallbackShare > 0)
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.FallbackOrderUsed,
                TrackOrderDiagnosticSeverity.Info,
                null,
                "Ein Teil der Gleichstände oder fehlenden Werte wurde deterministisch per Natural Path aufgelöst."));
        }

        var confidence = DetermineConfidence(request, best, groups, conflictExists, fallbackShare, tracks.Count);
        if (confidence is TrackOrderConfidence.Low or TrackOrderConfidence.Unresolved)
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.LowOrderConfidence,
                TrackOrderDiagnosticSeverity.Warning,
                null,
                "Die automatische Reihenfolge besitzt nur geringe fachliche Sicherheit."));
        }

        return CreateResult(ordered, confidence, diagnostics);
    }

    private List<OrderModel> BuildModels(
        AutomaticTrackOrderRequest request,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates)
    {
        var models = new List<OrderModel>();

        AddConfirmedModels(request, candidates, models);
        AddMetadataModels(request, candidates, models);
        AddFolderFileModels(request, candidates, models);
        AddFileNameModels(request, candidates, models);

        return models
            .Where(model => model.CoveredCount >= 2 && model.DistinctValueCount >= 2)
            .ToList();
    }

    private static void AddConfirmedModels(
        AutomaticTrackOrderRequest request,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates,
        List<OrderModel> models)
    {
        var global = request.Tracks
            .Select(track => new ModelValue(track, GetCandidate(candidates, track, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedGlobalPosition)))
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "ConfirmedGlobal", global, true, TrackOrderEvidenceSource.ConfirmedImportContext, 100);

        var discOnly = request.Tracks
            .Select(track => new ModelValue(track, GetCandidate(candidates, track, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedDisc)))
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "ConfirmedDisc", discOnly, true, TrackOrderEvidenceSource.ConfirmedImportContext, 90);

        var discTrack = request.Tracks
            .Select(track =>
            {
                var disc = GetCandidate(candidates, track, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedDisc);
                var trackNo = GetCandidate(candidates, track, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedTrack);
                return new ModelValue(track, CombineDiscTrack(disc, trackNo));
            })
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "ConfirmedDiscTrack", discTrack, true, TrackOrderEvidenceSource.ConfirmedImportContext, 100);
    }

    private static void AddMetadataModels(
        AutomaticTrackOrderRequest request,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates,
        List<OrderModel> models)
    {
        var discTrack = request.Tracks
            .Select(track =>
            {
                var disc = GetCandidate(candidates, track, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataDisc);
                var trackNo = GetCandidate(candidates, track, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataTrack);
                return new ModelValue(track, CombineDiscTrack(disc, trackNo));
            })
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "MetadataDiscTrack", discTrack, false, TrackOrderEvidenceSource.Metadata, 80);

        var trackOnly = request.Tracks
            .Select(track => new ModelValue(track, GetCandidate(candidates, track, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataTrack)))
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "MetadataTrack", trackOnly, false, TrackOrderEvidenceSource.Metadata, 60);
    }

    private static void AddFolderFileModels(
        AutomaticTrackOrderRequest request,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates,
        List<OrderModel> models)
    {
        var folderDiscTrack = request.Tracks
            .Select(track =>
            {
                var disc = GetCandidate(candidates, track, TrackOrderEvidenceSource.FolderName, TrackOrderPattern.ExplicitDiscMarker)
                           ?? GetCandidate(candidates, track, TrackOrderEvidenceSource.FolderName, TrackOrderPattern.NumericFolderName);
                var trackNo = GetCandidate(candidates, track, TrackOrderEvidenceSource.FileName, TrackOrderPattern.ExplicitTrackMarker)
                              ?? GetTokenCandidate(candidates, track, 0);
                return new ModelValue(track, CombineDiscTrack(disc, trackNo));
            })
            .Where(item => item.Value.HasValue)
            .ToList();

        AddModel(
            models,
            "FolderDiscPlusFilenameTrack",
            folderDiscTrack,
            false,
            [TrackOrderEvidenceSource.FolderName, TrackOrderEvidenceSource.FileName],
            85);
    }

    private static void AddFileNameModels(
        AutomaticTrackOrderRequest request,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates,
        List<OrderModel> models)
    {
        var explicitTrack = request.Tracks
            .Select(track => new ModelValue(track, GetCandidate(candidates, track, TrackOrderEvidenceSource.FileName, TrackOrderPattern.ExplicitTrackMarker)))
            .Where(item => item.Value.HasValue)
            .ToList();
        AddModel(models, "ExplicitFilenameTrack", explicitTrack, false, TrackOrderEvidenceSource.FileName, 70);

        var tokenIndexes = candidates.Values
            .SelectMany(set => set.Candidates)
            .Where(candidate => candidate.Source == TrackOrderEvidenceSource.FileName &&
                                candidate.Kind == TrackOrderCandidateKind.Sequence &&
                                candidate.TokenIndex.HasValue)
            .Select(candidate => candidate.TokenIndex!.Value)
            .Distinct()
            .Order()
            .ToList();

        foreach (var tokenIndex in tokenIndexes)
        {
            var values = request.Tracks
                .Select(track => new ModelValue(track, GetTokenCandidate(candidates, track, tokenIndex)))
                .Where(item => item.Value.HasValue)
                .ToList();
            AddModel(
                models,
                tokenIndex == 0 ? "FilenameLeadingSequence" : $"FilenameToken{tokenIndex}Sequence",
                values,
                false,
                TrackOrderEvidenceSource.FileName,
                tokenIndex == 0 ? 65 : 50 - Math.Min(tokenIndex, 10));
        }
    }

    private static void AddModel(
        List<OrderModel> models,
        string name,
        IReadOnlyList<ModelValue> values,
        bool hasAuthority,
        TrackOrderEvidenceSource source,
        int specificity)
    {
        AddModel(models, name, values, hasAuthority, [source], specificity);
    }

    private static void AddModel(
        List<OrderModel> models,
        string name,
        IReadOnlyList<ModelValue> values,
        bool hasAuthority,
        IReadOnlyCollection<TrackOrderEvidenceSource> sources,
        int specificity)
    {
        if (values.Count < 2)
            return;

        models.Add(new OrderModel(name, values, hasAuthority, sources, specificity));
    }

    private static int? GetCandidate(
        IReadOnlyDictionary<string, TrackOrderCandidateSet> sets,
        TrackOrderInput track,
        TrackOrderEvidenceSource source,
        TrackOrderPattern pattern)
    {
        if (!sets.TryGetValue(track.TrackKey, out var set))
            return null;

        return set.Candidates
            .Where(candidate => candidate.Source == source && candidate.Pattern == pattern)
            .Select(candidate => (int?)candidate.Value)
            .FirstOrDefault();
    }

    private static int? GetTokenCandidate(
        IReadOnlyDictionary<string, TrackOrderCandidateSet> sets,
        TrackOrderInput track,
        int tokenIndex)
    {
        if (!sets.TryGetValue(track.TrackKey, out var set))
            return null;

        return set.Candidates
            .Where(candidate => candidate.Source == TrackOrderEvidenceSource.FileName &&
                                candidate.Kind == TrackOrderCandidateKind.Sequence &&
                                candidate.TokenIndex == tokenIndex)
            .Select(candidate => (int?)candidate.Value)
            .FirstOrDefault();
    }

    private static int? CombineDiscTrack(int? major, int? minor)
    {
        return major.HasValue && minor.HasValue
            ? major.Value * 1_000_000 + minor.Value
            : null;
    }

    private List<TrackOrderInput> CreateFallbackOrder(IReadOnlyList<TrackOrderInput> tracks)
    {
        return tracks
            .OrderBy(track => track.RelativeFolder ?? string.Empty, _naturalComparer)
            .ThenBy(track => track.FileName ?? string.Empty, _naturalComparer)
            .ThenBy(track => track.FullPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(track => track.DeterministicInputPosition)
            .ToList();
    }

    private List<OrderModelGroup> MergeCompatibleModels(IReadOnlyList<OrderModel> models)
    {
        var groups = new List<OrderModelGroup>();
        foreach (var model in models.OrderByDescending(model => model.HasAuthority).ThenByDescending(model => model.CoveredCount))
        {
            var compatible = groups.FirstOrDefault(group => group.Models.All(existing => AreCompatible(existing, model)));
            if (compatible is null)
            {
                if (model.AmbiguousTieCount > 0)
                    continue;

                groups.Add(new OrderModelGroup([model]));
            }
            else
            {
                compatible.Models.Add(model);
            }
        }

        return groups;
    }

    private static bool AreCompatible(OrderModel left, OrderModel right)
    {
        var leftValues = left.ValuesByTrack;
        var rightValues = right.ValuesByTrack;
        var common = leftValues.Keys.Intersect(rightValues.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (common.Count < 2)
            return true;

        for (var i = 0; i < common.Count; i++)
        {
            for (var j = i + 1; j < common.Count; j++)
            {
                var leftCompare = leftValues[common[i]].CompareTo(leftValues[common[j]]);
                var rightCompare = rightValues[common[i]].CompareTo(rightValues[common[j]]);
                if (leftCompare == 0 || rightCompare == 0)
                    continue;

                if (Math.Sign(leftCompare) != Math.Sign(rightCompare))
                    return false;
            }
        }

        return true;
    }

    private List<TrackOrderInput> OrderByModelGroup(
        OrderModelGroup group,
        IReadOnlyList<TrackOrderInput> fallbackOrder)
    {
        var fallbackIndex = fallbackOrder
            .Select((track, index) => new { track.TrackKey, index })
            .ToDictionary(item => item.TrackKey, item => item.index, StringComparer.OrdinalIgnoreCase);

        var primary = group.PrimaryModel;
        return fallbackOrder
            .OrderBy(track => primary.ValuesByTrack.TryGetValue(track.TrackKey, out var value) ? value : int.MaxValue)
            .ThenBy(track => fallbackIndex[track.TrackKey])
            .ToList();
    }

    private static double CalculateFallbackShare(OrderModelGroup group, int totalTracks)
    {
        if (totalTracks <= 0)
            return 1;

        var missing = totalTracks - group.CoveredTrackKeys.Count;
        var ambiguous = group.AmbiguousTieCount;
        return Math.Clamp((missing + ambiguous) / (double)totalTracks, 0, 1);
    }

    private static TrackOrderConfidence DetermineConfidence(
        AutomaticTrackOrderRequest request,
        OrderModelGroup best,
        IReadOnlyList<OrderModelGroup> groups,
        bool conflictExists,
        double fallbackShare,
        int totalTracks)
    {
        var fullCoverage = best.CoveredTrackKeys.Count == totalTracks;
        var strongConflict = conflictExists && groups.Count > 1;

        TrackOrderConfidence confidence;
        if (best.HasAuthority && fullCoverage && request.ProjectType == TrackOrderProjectType.AudioDisc)
            confidence = TrackOrderConfidence.VeryHigh;
        else if (best.HasAuthority && fullCoverage)
            confidence = TrackOrderConfidence.High;
        else if (!strongConflict && best.IndependentSourceCount >= 2 && fullCoverage)
            confidence = TrackOrderConfidence.VeryHigh;
        else if (!strongConflict && fullCoverage)
            confidence = TrackOrderConfidence.High;
        else if (strongConflict)
            confidence = best.HasAuthority ? TrackOrderConfidence.High : TrackOrderConfidence.Medium;
        else
            confidence = TrackOrderConfidence.Medium;

        if (fallbackShare > 0.4)
            confidence = Min(confidence, TrackOrderConfidence.Low);
        else if (fallbackShare > 0.2)
            confidence = Min(confidence, TrackOrderConfidence.Medium);

        if (strongConflict && IsCompleteReversal(groups))
            confidence = Min(confidence, TrackOrderConfidence.Low);

        return confidence;
    }

    private static bool IsCompleteReversal(IReadOnlyList<OrderModelGroup> groups)
    {
        if (groups.Count < 2)
            return false;

        var first = groups[0].PrimaryModel;
        var second = groups[1].PrimaryModel;
        var common = first.ValuesByTrack.Keys.Intersect(second.ValuesByTrack.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (common.Count < 3)
            return false;

        var inversions = 0;
        var comparable = 0;
        for (var i = 0; i < common.Count; i++)
        {
            for (var j = i + 1; j < common.Count; j++)
            {
                var a = first.ValuesByTrack[common[i]].CompareTo(first.ValuesByTrack[common[j]]);
                var b = second.ValuesByTrack[common[i]].CompareTo(second.ValuesByTrack[common[j]]);
                if (a == 0 || b == 0)
                    continue;
                comparable++;
                if (Math.Sign(a) != Math.Sign(b))
                    inversions++;
            }
        }

        return comparable > 0 && inversions == comparable;
    }

    private static TrackOrderConfidence Min(TrackOrderConfidence left, TrackOrderConfidence right)
    {
        return left < right ? left : right;
    }

    private static AutomaticTrackOrderResult CreateResult(
        IReadOnlyList<TrackOrderInput> ordered,
        TrackOrderConfidence confidence,
        IReadOnlyList<TrackOrderDiagnostic> diagnostics)
    {
        return new AutomaticTrackOrderResult(
            ordered.Select((track, index) => new AutomaticTrackOrderItemResult(track.TrackKey, index + 1, confidence)).ToList(),
            confidence,
            diagnostics);
    }

    private static List<TrackOrderDiagnostic> CreateInputDiagnostics(
        IReadOnlyList<TrackOrderInput> tracks,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates)
    {
        var diagnostics = new List<TrackOrderDiagnostic>();

        foreach (var track in tracks)
        {
            if (track.Metadata.TrackNumber is > 0 &&
                track.Metadata.TrackTotal is > 0 &&
                track.Metadata.TrackNumber > track.Metadata.TrackTotal)
            {
                diagnostics.Add(new TrackOrderDiagnostic(
                    TrackOrderDiagnosticCode.InvalidTrackTotal,
                    TrackOrderDiagnosticSeverity.Warning,
                    track.TrackKey,
                    "Tracknummer ist größer als TrackTotal."));
            }

            if (track.Metadata.DiscNumber is > 0 &&
                track.Metadata.DiscTotal is > 0 &&
                track.Metadata.DiscNumber > track.Metadata.DiscTotal)
            {
                diagnostics.Add(new TrackOrderDiagnostic(
                    TrackOrderDiagnosticCode.InvalidDiscTotal,
                    TrackOrderDiagnosticSeverity.Warning,
                    track.TrackKey,
                    "Discnummer ist größer als DiscTotal."));
            }
        }

        foreach (var gap in FindDiscSequenceGaps(tracks, candidates))
        {
            diagnostics.Add(new TrackOrderDiagnostic(
                TrackOrderDiagnosticCode.DiscSequenceGap,
                TrackOrderDiagnosticSeverity.Warning,
                null,
                gap.Start == gap.End
                    ? $"Disc {gap.Start} fehlt möglicherweise."
                    : $"Disc {gap.Start}-{gap.End} fehlen möglicherweise."));
        }

        return diagnostics;
    }

    private static IReadOnlyList<NumberGap> FindDiscSequenceGaps(
        IReadOnlyList<TrackOrderInput> tracks,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates)
    {
        var discNumbers = tracks
            .Select(track => GetDiscNumberForGapDetection(track, candidates))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .Distinct()
            .Order()
            .ToList();

        if (discNumbers.Count < 2)
            return [];

        var gaps = new List<NumberGap>();
        for (var index = 1; index < discNumbers.Count; index++)
        {
            var previous = discNumbers[index - 1];
            var current = discNumbers[index];
            if (current > previous + 1)
                gaps.Add(new NumberGap(previous + 1, current - 1));
        }

        return gaps;
    }

    private static int? GetDiscNumberForGapDetection(
        TrackOrderInput track,
        IReadOnlyDictionary<string, TrackOrderCandidateSet> candidates)
    {
        return GetCandidate(candidates, track, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedDisc)
               ?? GetCandidate(candidates, track, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataDisc)
               ?? GetCandidate(candidates, track, TrackOrderEvidenceSource.FolderName, TrackOrderPattern.ExplicitDiscMarker)
               ?? GetCandidate(candidates, track, TrackOrderEvidenceSource.FolderName, TrackOrderPattern.NumericFolderName)
               ?? GetCandidate(candidates, track, TrackOrderEvidenceSource.FileName, TrackOrderPattern.ExplicitDiscMarker);
    }

    private sealed record ModelValue(TrackOrderInput Track, int? Value);

    private sealed record NumberGap(int Start, int End);

    private sealed class OrderModel
    {
        public OrderModel(
            string name,
            IReadOnlyList<ModelValue> values,
            bool hasAuthority,
            IReadOnlyCollection<TrackOrderEvidenceSource> sources,
            int specificity)
        {
            Name = name;
            Values = values;
            HasAuthority = hasAuthority;
            Sources = sources;
            Specificity = specificity;
            ValuesByTrack = values
                .Where(item => item.Value.HasValue)
                .GroupBy(item => item.Track.TrackKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value!.Value, StringComparer.OrdinalIgnoreCase);
        }

        public string Name { get; }
        public IReadOnlyList<ModelValue> Values { get; }
        public bool HasAuthority { get; }
        public IReadOnlyCollection<TrackOrderEvidenceSource> Sources { get; }
        public int Specificity { get; }
        public IReadOnlyDictionary<string, int> ValuesByTrack { get; }
        public int CoveredCount => ValuesByTrack.Count;
        public int DistinctValueCount => ValuesByTrack.Values.Distinct().Count();
        public int AmbiguousTieCount => ValuesByTrack.Count - DistinctValueCount;
        public int Regularity => CalculateRegularity(ValuesByTrack.Values.Order().ToList());

        private static int CalculateRegularity(IReadOnlyList<int> values)
        {
            if (values.Count < 3)
                return 0;

            var deltas = values.Zip(values.Skip(1), (left, right) => right - left).ToList();
            return deltas.Count > 0 && deltas.Distinct().Count() == 1 ? 1 : 0;
        }
    }

    private sealed class OrderModelGroup
    {
        public OrderModelGroup(IEnumerable<OrderModel> models)
        {
            Models = models.ToList();
        }

        public List<OrderModel> Models { get; }
        public OrderModel PrimaryModel => Models
            .OrderByDescending(model => model.HasAuthority)
            .ThenByDescending(model => model.CoveredCount)
            .ThenBy(model => model.AmbiguousTieCount)
            .ThenByDescending(model => model.Specificity)
            .First();

        public bool HasAuthority => Models.Any(model => model.HasAuthority);
        public int IndependentSourceCount => Models.SelectMany(model => model.Sources).Distinct().Count();
        public int Specificity => Models.Max(model => model.Specificity);
        public int Regularity => Models.Max(model => model.Regularity);
        public HashSet<string> CoveredTrackKeys => Models.SelectMany(model => model.ValuesByTrack.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        public int Coverage => CoveredTrackKeys.Count;
        public int AmbiguousTieCount => PrimaryModel.AmbiguousTieCount;
    }
}
