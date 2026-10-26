using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed class ChapterScopeInputSignatureBuilder
{
    public ChapterScopeInputSignature Build(ChapterAnalysisScope scope, int analysisRulesRevision)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (analysisRulesRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(analysisRulesRevision));

        var canonical = new StringBuilder();
        Append(canonical, "rules", analysisRulesRevision.ToString(CultureInfo.InvariantCulture));
        Append(canonical, "kind", scope.Kind.ToString());
        Append(canonical, "scope", scope.ProvenanceIdentity);

        foreach (var member in scope.Members
                     .OrderBy(item => item.ScopeOrder)
                     .ThenBy(item => item.EffectiveChapterId, StringComparer.Ordinal))
        {
            Append(canonical, "member", member.EffectiveChapterId);
            Append(canonical, "source", member.SourceId.ToString("N"));
            Append(canonical, "order", member.ScopeOrder.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "track", Nullable(member.SourceTrackNumber));
            Append(canonical, "disc", Nullable(member.SourceDiscNumber));
            Append(canonical, "embeddedOrdinal", Nullable(member.EmbeddedOrdinal));
            Append(canonical, "embeddedStart", Nullable(member.EmbeddedStartTicks));
            Append(canonical, "embeddedEnd", Nullable(member.EmbeddedEndTicks));

            foreach (var candidate in scope.Candidates
                         .Where(candidate =>
                             candidate.Provenance.EffectiveChapterId == member.EffectiveChapterId &&
                             IsImmutableSource(candidate.Source))
                         .OrderBy(candidate => candidate.Source))
            {
                Append(canonical, "candidateSource", candidate.Source.ToString());
                Append(canonical, "candidateRaw", candidate.Normalization.RawText ?? string.Empty);
            }
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        return new ChapterScopeInputSignature(analysisRulesRevision, hash);
    }

    private static bool IsImmutableSource(ChapterCandidateSource source) =>
        source is ChapterCandidateSource.EmbeddedChapterTitle or
            ChapterCandidateSource.TrackTitle or
            ChapterCandidateSource.Filename;

    private static string Nullable<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder.Append(name.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(name);
        builder.Append('=');
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
