using System.Globalization;
using System.Text.RegularExpressions;
using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>Evidence for independent chapter labels, used only by the custom clean-tag slot.</summary>
public sealed class ChapterCustomTagTitlePreservationService
{
    private static readonly Regex OrdinalPrefix = new(
        @"^(?<number>[0-9]+)(?:\s*[-–—.:]\s*|\s+)(?<text>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> NonChapterMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Track", "Titel", "Title", "Pista", "Título", "Titulo", "Piste", "Titre",
        "Traccia", "Titolo", "CD", "Disc"
    };
    private const string ProtectedLabel = "BookStitchProtectedChapterLabel";
    private readonly ChapterCandidateNormalizer _normalizer = new();

    public IReadOnlyDictionary<TrackInfo, string> Build(IEnumerable<TrackInfo> tracks)
    {
        var result = new Dictionary<TrackInfo, string>(ReferenceEqualityComparer<TrackInfo>.Instance);
        var parsed = tracks.Distinct(ReferenceEqualityComparer<TrackInfo>.Instance)
            .Select(Parse).OfType<Candidate>();
        foreach (var scope in parsed.GroupBy(item => (
                     item.Track.DiscNumber ?? item.Track.SourceDiscNumber ??
                         item.Track.SourceMetadataFields?.SourceDiscNumber ?? 0,
                     item.Family)))
        {
            var members = scope.ToArray();
            // Repetition across distinct source ordinals proves that this label
            // does not enumerate tracks. A second label establishes a chapter family.
            if (members.Length < 3 || members.Select(item => item.Ordinal).Distinct().Count() != members.Length)
                continue;
            var labels = members.GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
            if (labels.Length < 2 || !labels.Any(group => group.Count() >= 2))
                continue;
            foreach (var member in members)
            {
                var shielded = member.Text.Replace(member.Label, ProtectedLabel, StringComparison.Ordinal);
                result[member.Track] = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, shielded)
                    .NormalizedText.Replace(ProtectedLabel, member.Label, StringComparison.Ordinal);
            }
        }
        return result;
    }

    private Candidate? Parse(TrackInfo track)
    {
        var ordinal = track.SourceTrackNumber ?? track.SourceMetadataFields?.SourceTrackNumber;
        if (ordinal is not > 0 || string.IsNullOrWhiteSpace(track.TagTitle))
            return null;
        // Reuse Unicode/control/whitespace handling without stripping number labels.
        var text = _normalizer.NormalizeUserDefinedCanonical(ChapterCandidateSource.CanonicalTitle,
            track.TagTitle.Replace('_', ' ')).NormalizedText;
        var prefix = OrdinalPrefix.Match(text);
        if (!prefix.Success || !int.TryParse(prefix.Groups["number"].Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != ordinal)
            return null;
        text = prefix.Groups["text"].Value;
        if (text.Contains(ProtectedLabel, StringComparison.Ordinal))
            return null;
        var labels = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, text).AppliedTransforms
            .Where(transform => transform.Kind == ChapterNormalizationTransformKind.RemovedTechnicalNumbering)
            .Select(transform => transform.Value!).ToList();
        // The shared structured matcher is anchored. Probe word boundaries here
        // as well, so a part heading may precede an Asian or Nordic label.
        for (var index = 0; index < text.Length; index++)
        {
            if (index > 0 && !char.IsWhiteSpace(text[index - 1]))
                continue;
            if (ChapterMarkerCatalog.TryMatchStructuredChapterPrefix(text[index..], out var prefixLength))
            {
                var structured = text.Substring(index, prefixLength).Trim();
                if (!labels.Contains(structured, StringComparer.Ordinal))
                    labels.Add(structured);
            }
        }
        // Ambiguous multi-marker titles retain the existing cleanup behavior.
        if (labels.Count != 1)
            return null;
        var label = labels[0];
        var family = ChapterMarkerCatalog.TechnicalNumberingMarkers.FirstOrDefault(marker =>
            !NonChapterMarkers.Contains(marker) && label.StartsWith(marker, StringComparison.OrdinalIgnoreCase) &&
            label.Length > marker.Length && !char.IsLetter(label[marker.Length]));
        if (family is null && ChapterMarkerCatalog.TryMatchStructuredChapterPrefix(label, out var length) &&
            length == label.Length)
        {
            // Structured localized labels (e.g. 第三章) need no numeric translation:
            // repeated labels on different source tracks provide the same evidence.
            family = Regex.Replace(label, @"[0-9〇零一二三四五六七八九十百千]+", "#");
        }
        if (family is null || !text.Contains(label, StringComparison.Ordinal))
            return null;
        return new Candidate(track, number, text, label, family.ToUpperInvariant());
    }

    private sealed record Candidate(TrackInfo Track, int Ordinal, string Text, string Label, string Family);
}
