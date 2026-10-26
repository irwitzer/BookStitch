namespace BookStitch.Services;

public sealed class ChapterScopeBuilder
{
    public IReadOnlyList<ChapterAnalysisScope> Build(ChapterCandidateProjectionResult projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var activeMembers = projection.EffectiveChapters
            .OrderBy(member => member.ScopeOrder)
            .ThenBy(member => member.EffectiveChapterId, StringComparer.Ordinal)
            .ToArray();

        var groups = activeMembers.GroupBy(member => ResolveScopeKey(member));
        var scopes = new List<ChapterAnalysisScope>();

        foreach (var group in groups)
        {
            var orderedMembers = group
                .OrderBy(member => member.ScopeOrder)
                .ThenBy(member => member.EffectiveChapterId, StringComparer.Ordinal)
                .ToArray();
            var memberIds = orderedMembers
                .Select(member => member.EffectiveChapterId)
                .ToHashSet(StringComparer.Ordinal);
            var candidates = projection.Candidates
                .Where(candidate => memberIds.Contains(candidate.Provenance.EffectiveChapterId))
                .OrderBy(candidate => candidate.Provenance.ScopeOrder)
                .ThenBy(candidate => candidate.Source)
                .ThenBy(candidate => candidate.Provenance.EffectiveChapterId, StringComparer.Ordinal)
                .ToArray();

            scopes.Add(new ChapterAnalysisScope(
                group.Key.ScopeId,
                group.Key.Kind,
                group.Key.ProvenanceIdentity,
                Array.AsReadOnly(orderedMembers),
                Array.AsReadOnly(candidates)));
        }

        return scopes
            .OrderBy(scope => scope.Members.Min(member => member.ScopeOrder))
            .ThenBy(scope => scope.ScopeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static ScopeKey ResolveScopeKey(ChapterCandidateProvenance member)
    {
        if (member.HasMultipleEmbeddedChapters)
        {
            var provenance = member.ProjectIdentity + ":source:" + member.SourceId.ToString("N") +
                (member.SourceOccurrence > 1 ? $":occurrence:{member.SourceOccurrence}" : string.Empty);
            return new ScopeKey(
                ChapterScopeKind.Embedded,
                provenance,
                "embedded:" + provenance);
        }

        var kind = member.ProjectKind switch
        {
            ChapterProjectSourceKind.LocalFolder => ChapterScopeKind.FolderTrack,
            ChapterProjectSourceKind.Mp3Cd => ChapterScopeKind.Mp3DiscTrack,
            ChapterProjectSourceKind.AudioCd => ChapterScopeKind.AudioDisc,
            _ => throw new ArgumentOutOfRangeException(nameof(member))
        };

        var provenanceIdentity = member.ProjectKind == ChapterProjectSourceKind.LocalFolder
            ? member.ProjectIdentity
            : member.ProjectIdentity + ":" + member.ScopeProvenanceIdentity;

        return new ScopeKey(
            kind,
            provenanceIdentity,
            kind.ToString().ToLowerInvariant() + ":" + provenanceIdentity);
    }

    private readonly record struct ScopeKey(
        ChapterScopeKind Kind,
        string ProvenanceIdentity,
        string ScopeId);
}
