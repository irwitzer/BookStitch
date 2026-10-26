using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterScopeInputSignatureBuilderTests
{
    private readonly ChapterScopeInputSignatureBuilder _builder = new();

    [Fact]
    public void Build_ChangesForImmutableSourceInputsAndRulesRevision()
    {
        var original = ChapterAnalysisTestFactory.Scope(("Source A", 1), ("Source B", 2));
        var changedTitle = ReplaceRawText(original, 0, "Changed Source A");
        var changedFilename = AddFilename(original, "001_Source.mp3");
        var changedStructure = ReplaceFirstProvenance(original, provenance => provenance with
        {
            EmbeddedOrdinal = 1,
            EmbeddedStartTicks = 10,
            EmbeddedEndTicks = 20
        });
        var changedOrder = SwapScopeOrder(original);
        var excludedMember = RemoveLastMember(original);

        var signature = _builder.Build(original, 1);

        Assert.NotEqual(signature.Hash, _builder.Build(changedTitle, 1).Hash);
        Assert.NotEqual(signature.Hash, _builder.Build(changedFilename, 1).Hash);
        Assert.NotEqual(signature.Hash, _builder.Build(changedStructure, 1).Hash);
        Assert.NotEqual(signature.Hash, _builder.Build(changedOrder, 1).Hash);
        Assert.NotEqual(signature.Hash, _builder.Build(excludedMember, 1).Hash);
        Assert.NotEqual(signature.Hash, _builder.Build(original, 2).Hash);
    }

    [Fact]
    public void Build_IgnoresLiveProjectMetadataAndOutputIndex()
    {
        var original = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, "Source A", 1, 1),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.CanonicalTitle, "Project One", 1),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.CanonicalAlbum, "Album One", 1));
        var liveMetadataChanged = ReplaceProjectCandidates(original, "Project Two", "Album Two");
        var outputIndexChanged = ReplaceFirstProvenance(original, provenance => provenance with
        {
            CurrentOutputChapterIndex = 99
        });

        var signature = _builder.Build(original, 1);

        Assert.Equal(signature.Hash, _builder.Build(liveMetadataChanged, 1).Hash);
        Assert.Equal(signature.Hash, _builder.Build(outputIndexChanged, 1).Hash);
    }

    [Fact]
    public void Build_IsDeterministicAndCultureIndependent()
    {
        var scope = ChapterAnalysisTestFactory.Scope(("Ärger 1984", 1), ("Öl 2001", 2));

        var first = _builder.Build(scope, 1);
        var second = _builder.Build(scope, 1);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Hash.Length);
    }

    private static ChapterAnalysisScope ReplaceRawText(ChapterAnalysisScope scope, int index, string rawText)
    {
        var candidates = scope.Candidates.ToArray();
        var candidate = candidates[index];
        candidates[index] = candidate with
        {
            Normalization = new ChapterCandidateNormalizer().Normalize(candidate.Source, rawText)
        };
        return scope with { Candidates = candidates };
    }

    private static ChapterAnalysisScope AddFilename(ChapterAnalysisScope scope, string filename)
    {
        var member = scope.Members[0];
        var candidate = new ProjectedChapterCandidate(
            ChapterCandidateSource.Filename,
            member,
            new ChapterCandidateNormalizer().Normalize(ChapterCandidateSource.Filename, filename));
        return scope with { Candidates = scope.Candidates.Append(candidate).ToArray() };
    }

    private static ChapterAnalysisScope ReplaceProjectCandidates(
        ChapterAnalysisScope scope,
        string title,
        string album)
    {
        var normalizer = new ChapterCandidateNormalizer();
        return scope with
        {
            Candidates = scope.Candidates.Select(candidate => candidate.Source switch
            {
                ChapterCandidateSource.CanonicalTitle => candidate with
                {
                    Normalization = normalizer.NormalizeUserDefinedCanonical(candidate.Source, title)
                },
                ChapterCandidateSource.CanonicalAlbum => candidate with
                {
                    Normalization = normalizer.NormalizeUserDefinedCanonical(candidate.Source, album)
                },
                _ => candidate
            }).ToArray()
        };
    }

    private static ChapterAnalysisScope ReplaceFirstProvenance(
        ChapterAnalysisScope scope,
        Func<ChapterCandidateProvenance, ChapterCandidateProvenance> replace)
    {
        var original = scope.Members[0];
        var changed = replace(original);
        return scope with
        {
            Members = scope.Members.Select(member => member == original ? changed : member).ToArray(),
            Candidates = scope.Candidates.Select(candidate =>
                candidate.Provenance == original ? candidate with { Provenance = changed } : candidate).ToArray()
        };
    }

    private static ChapterAnalysisScope SwapScopeOrder(ChapterAnalysisScope scope)
    {
        var first = scope.Members[0];
        var second = scope.Members[1];
        var replacements = new Dictionary<string, ChapterCandidateProvenance>(StringComparer.Ordinal)
        {
            [first.EffectiveChapterId] = first with { ScopeOrder = second.ScopeOrder },
            [second.EffectiveChapterId] = second with { ScopeOrder = first.ScopeOrder }
        };
        return scope with
        {
            Members = scope.Members.Select(member => replacements[member.EffectiveChapterId]).ToArray(),
            Candidates = scope.Candidates.Select(candidate => candidate with
            {
                Provenance = replacements[candidate.Provenance.EffectiveChapterId]
            }).ToArray()
        };
    }

    private static ChapterAnalysisScope RemoveLastMember(ChapterAnalysisScope scope)
    {
        var removedId = scope.Members[^1].EffectiveChapterId;
        return scope with
        {
            Members = scope.Members.Where(member => member.EffectiveChapterId != removedId).ToArray(),
            Candidates = scope.Candidates.Where(candidate =>
                candidate.Provenance.EffectiveChapterId != removedId).ToArray()
        };
    }
}
