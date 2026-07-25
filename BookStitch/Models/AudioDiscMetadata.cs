namespace BookStitch.Models;

public enum AudioDiscMetadataLookupStatus
{
    Unavailable,
    NoMatch,
    MatchesFound
}

public sealed record AudioDiscMetadataCandidate(
    string ReleaseId,
    string Title,
    string Artist,
    int? DiscNumber,
    int? DiscCount,
    string Date,
    string Country,
    IReadOnlyList<string> TrackTitles)
{
    public string DiscText => DiscNumber is null
        ? string.Empty
        : DiscCount is > 1
            ? $"CD {DiscNumber} von {DiscCount}"
            : $"CD {DiscNumber}";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist)
        ? Title
        : $"{Artist} – {Title}";

    public string SecondaryText => string.Join(" · ", new[] { DiscText, Date, Country }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record AudioDiscMetadataLookupResult(
    AudioDiscMetadataLookupStatus Status,
    IReadOnlyList<AudioDiscMetadataCandidate> Candidates,
    string Message)
{
    public static AudioDiscMetadataLookupResult Unavailable(string message) =>
        new(AudioDiscMetadataLookupStatus.Unavailable, [], message);

    public static AudioDiscMetadataLookupResult NoMatch() =>
        new(AudioDiscMetadataLookupStatus.NoMatch, [], "Keine passenden Online-Metadaten gefunden.");

    public static AudioDiscMetadataLookupResult Success(IReadOnlyList<AudioDiscMetadataCandidate> candidates) =>
        new(AudioDiscMetadataLookupStatus.MatchesFound, candidates, string.Empty);
}
