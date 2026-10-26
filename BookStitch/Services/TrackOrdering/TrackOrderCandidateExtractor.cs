using System.IO;
using System.Text.RegularExpressions;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services;

namespace BookStitch.Services.TrackOrdering;

public sealed class TrackOrderCandidateExtractor
{
    private static readonly Regex NumericTokenRegex = new(
        @"(?<!\p{L})(?<number>\d{1,6})(?<suffix>[a-zA-Z]?)(?!\p{L})",
        RegexOptions.Compiled);

    public TrackOrderCandidateSet Extract(TrackOrderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candidates = new List<TrackOrderCandidate>();

        AddConfirmedCandidates(input, candidates);
        AddMetadataCandidates(input, candidates);
        AddFolderCandidates(input, candidates);
        AddFileNameCandidates(input, candidates);

        return new TrackOrderCandidateSet(input.TrackKey, candidates);
    }

    public IReadOnlyList<TrackOrderCandidateSet> Extract(AutomaticTrackOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Tracks.Select(Extract).ToList();
    }

    private static void AddConfirmedCandidates(
        TrackOrderInput input,
        List<TrackOrderCandidate> candidates)
    {
        if (input.ConfirmedContext is not { } confirmed)
            return;

        AddPositive(candidates, TrackOrderCandidateKind.Sequence, confirmed.GlobalPosition, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedGlobalPosition, null, confirmed.GlobalPosition?.ToString());
        AddPositive(candidates, TrackOrderCandidateKind.Disc, confirmed.DiscNumber, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedDisc, null, confirmed.DiscNumber?.ToString());
        AddPositive(candidates, TrackOrderCandidateKind.Track, confirmed.TrackNumber, TrackOrderEvidenceSource.ConfirmedImportContext, TrackOrderPattern.ConfirmedTrack, null, confirmed.TrackNumber?.ToString());
    }

    private static void AddMetadataCandidates(
        TrackOrderInput input,
        List<TrackOrderCandidate> candidates)
    {
        AddPositive(candidates, TrackOrderCandidateKind.Disc, input.Metadata.DiscNumber, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataDisc, null, input.Metadata.DiscNumber?.ToString());
        AddPositive(candidates, TrackOrderCandidateKind.Track, input.Metadata.TrackNumber, TrackOrderEvidenceSource.Metadata, TrackOrderPattern.MetadataTrack, null, input.Metadata.TrackNumber?.ToString());
    }

    private static void AddFolderCandidates(
        TrackOrderInput input,
        List<TrackOrderCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(input.RelativeFolder))
            return;

        var folders = input.RelativeFolder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var folder in folders)
        {
            foreach (Match match in ChapterMarkerCatalog.ExplicitDiscNumberRegex.Matches(folder))
                AddMatch(candidates, TrackOrderCandidateKind.Disc, TrackOrderEvidenceSource.FolderName, TrackOrderPattern.ExplicitDiscMarker, null, match, folder);
        }

        if (folders.Length == 1)
        {
            var folder = folders[0].Trim();
            if (Regex.IsMatch(folder, @"^\d{1,4}$", RegexOptions.CultureInvariant) &&
                int.TryParse(folder, out var numericFolder) &&
                numericFolder > 0)
            {
                candidates.Add(new TrackOrderCandidate(
                    TrackOrderCandidateKind.Disc,
                    numericFolder,
                    TrackOrderEvidenceSource.FolderName,
                    TrackOrderPattern.NumericFolderName,
                    0,
                    folder));
            }
        }
    }

    private static void AddFileNameCandidates(
        TrackOrderInput input,
        List<TrackOrderCandidate> candidates)
    {
        var name = Path.GetFileNameWithoutExtension(input.FileName);
        if (string.IsNullOrWhiteSpace(name))
            return;

        foreach (Match match in ChapterMarkerCatalog.ExplicitDiscNumberRegex.Matches(name))
            AddMatch(candidates, TrackOrderCandidateKind.Disc, TrackOrderEvidenceSource.FileName, TrackOrderPattern.ExplicitDiscMarker, null, match, name);

        foreach (Match match in ChapterMarkerCatalog.ExplicitTrackNumberRegex.Matches(name))
            AddMatch(candidates, TrackOrderCandidateKind.Track, TrackOrderEvidenceSource.FileName, TrackOrderPattern.ExplicitTrackMarker, null, match, name);

        var tokenIndex = 0;
        foreach (Match match in NumericTokenRegex.Matches(name))
        {
            if (!int.TryParse(match.Groups["number"].Value, out var value) || value <= 0)
                continue;

            var suffix = match.Groups["suffix"].Value;
            var pattern = tokenIndex == 0
                ? string.IsNullOrEmpty(suffix)
                    ? TrackOrderPattern.LeadingNumericToken
                    : TrackOrderPattern.AlphaSuffixNumericToken
                : name.Contains('_') || name.Contains('.') || name.Contains('-')
                    ? TrackOrderPattern.HierarchicalNumericToken
                    : TrackOrderPattern.GenericNumericToken;

            candidates.Add(new TrackOrderCandidate(
                TrackOrderCandidateKind.Sequence,
                value,
                TrackOrderEvidenceSource.FileName,
                pattern,
                tokenIndex,
                match.Value));
            tokenIndex++;
        }
    }

    private static void AddMatch(
        List<TrackOrderCandidate> candidates,
        TrackOrderCandidateKind kind,
        TrackOrderEvidenceSource source,
        TrackOrderPattern pattern,
        int? tokenIndex,
        Match match,
        string rawText)
    {
        if (int.TryParse(match.Groups["number"].Value, out var value) && value > 0)
            candidates.Add(new TrackOrderCandidate(kind, value, source, pattern, tokenIndex, rawText));
    }

    private static void AddPositive(
        List<TrackOrderCandidate> candidates,
        TrackOrderCandidateKind kind,
        int? value,
        TrackOrderEvidenceSource source,
        TrackOrderPattern pattern,
        int? tokenIndex,
        string? rawText)
    {
        if (value is > 0)
            candidates.Add(new TrackOrderCandidate(kind, value.Value, source, pattern, tokenIndex, rawText ?? value.Value.ToString()));
    }
}
