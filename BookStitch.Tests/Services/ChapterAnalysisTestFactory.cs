using BookStitch.Services;

namespace BookStitch.Tests.Services;

internal static class ChapterAnalysisTestFactory
{
    private static readonly ChapterCandidateNormalizer Normalizer = new();

    public static ChapterAnalysisScope Scope(
        params (string Text, int? TrackNumber)[] items) =>
        Scope(items.Select((item, index) => new CandidateSpec(
            ChapterCandidateSource.TrackTitle,
            item.Text,
            index + 1,
            item.TrackNumber)).ToArray());

    public static ChapterAnalysisScope Scope(params CandidateSpec[] specs)
    {
        var memberSpecs = specs
            .GroupBy(spec => spec.MemberNumber)
            .OrderBy(group => group.Key)
            .ToArray();
        var provenances = memberSpecs.ToDictionary(
            group => group.Key,
            group => Provenance(group.Key, group.First().TrackNumber));
        var candidates = specs.Select(spec => new ProjectedChapterCandidate(
            spec.Source,
            provenances[spec.MemberNumber],
            Normalizer.Normalize(spec.Source, spec.Text))).ToArray();

        return new ChapterAnalysisScope(
            "test-scope",
            ChapterScopeKind.FolderTrack,
            "test-project",
            provenances.Values.OrderBy(item => item.ScopeOrder).ToArray(),
            candidates);
    }

    public static CandidateSpec Candidate(
        ChapterCandidateSource source,
        string text,
        int memberNumber,
        int? trackNumber = null) =>
        new(source, text, memberNumber, trackNumber);

    public static ChapterCandidateProvenance Provenance(int memberNumber, int? trackNumber = null)
    {
        var sourceId = new Guid(memberNumber, 0, 0, new byte[8]);
        return new ChapterCandidateProvenance(
            ChapterProjectSourceKind.LocalFolder,
            "test-project",
            sourceId,
            sourceId.ToString("N") + ":track",
            "test-project",
            memberNumber,
            memberNumber,
            trackNumber,
            SourceDiscNumber: null,
            EmbeddedOrdinal: null,
            EmbeddedStartTicks: null,
            EmbeddedEndTicks: null,
            HasMultipleEmbeddedChapters: false);
    }

    internal sealed record CandidateSpec(
        ChapterCandidateSource Source,
        string Text,
        int MemberNumber,
        int? TrackNumber = null);
}
