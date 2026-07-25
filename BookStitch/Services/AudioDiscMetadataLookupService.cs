using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Performs an optional MusicBrainz lookup for an already-read audio CD TOC.
/// Network failures and missing matches are normal outcomes and must never block
/// disc detection or the future ripping workflow.
/// </summary>
public sealed class AudioDiscMetadataLookupService
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient;

    public AudioDiscMetadataLookupService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<AudioDiscMetadataLookupResult> LookupAsync(
        AudioDiscInfo disc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(disc);

        if (disc.Toc is null)
            return AudioDiscMetadataLookupResult.Unavailable(
                "Das Laufwerk hat keine ausreichend genauen TOC-Daten für die Online-Erkennung geliefert.");

        var discId = Uri.EscapeDataString(disc.Toc.MusicBrainzDiscId);
        var toc = Uri.EscapeDataString(disc.Toc.QueryString);
        var requestUri = $"https://musicbrainz.org/ws/2/discid/{discId}?inc=artists+recordings&toc={toc}&cdstubs=no&fmt=json";

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return AudioDiscMetadataLookupResult.NoMatch();

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ParseResponse(document.RootElement, disc.TrackCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AudioDiscMetadataLookupResult.Unavailable("Die Online-Erkennung wurde beendet.");
        }
        catch (HttpRequestException)
        {
            return AudioDiscMetadataLookupResult.Unavailable("MusicBrainz ist momentan nicht erreichbar.");
        }
        catch (JsonException)
        {
            return AudioDiscMetadataLookupResult.Unavailable("MusicBrainz hat eine unerwartete Antwort geliefert.");
        }
        catch
        {
            return AudioDiscMetadataLookupResult.Unavailable("Die Online-Erkennung konnte nicht abgeschlossen werden.");
        }
    }

    public static AudioDiscMetadataLookupResult ParseResponse(JsonElement root, int expectedTrackCount)
    {
        if (!root.TryGetProperty("releases", out var releasesElement) || releasesElement.ValueKind != JsonValueKind.Array)
            return AudioDiscMetadataLookupResult.NoMatch();

        var candidates = new List<AudioDiscMetadataCandidate>();
        foreach (var release in releasesElement.EnumerateArray())
        {
            var title = GetString(release, "title");
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var artist = ReadArtistCredit(release);
            var releaseId = GetString(release, "id");
            var date = GetString(release, "date");
            var country = GetString(release, "country");

            var media = FindMatchingMedium(release, expectedTrackCount);
            var discNumber = media is null ? null : GetNullableInt(media.Value, "position");
            int? discCount = release.TryGetProperty("media", out var allMedia) && allMedia.ValueKind == JsonValueKind.Array
                ? allMedia.GetArrayLength()
                : null;
            var trackTitles = media is null ? [] : ReadTrackTitles(media.Value);

            candidates.Add(new AudioDiscMetadataCandidate(
                releaseId,
                title,
                artist,
                discNumber,
                discCount,
                date,
                country,
                trackTitles));
        }

        var distinctCandidates = candidates
            .GroupBy(candidate => new
            {
                candidate.Title,
                candidate.Artist,
                candidate.DiscNumber,
                TrackKey = string.Join("\u001f", candidate.TrackTitles)
            })
            .Select(group => group.First())
            .Take(12)
            .ToList();

        return distinctCandidates.Count == 0
            ? AudioDiscMetadataLookupResult.NoMatch()
            : AudioDiscMetadataLookupResult.Success(distinctCandidates);
    }

    private static JsonElement? FindMatchingMedium(JsonElement release, int expectedTrackCount)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
            return null;

        JsonElement? fallback = null;
        foreach (var medium in media.EnumerateArray())
        {
            fallback ??= medium;
            var trackCount = GetNullableInt(medium, "track-count");
            if (trackCount == expectedTrackCount)
                return medium;
        }

        return fallback;
    }

    private static IReadOnlyList<string> ReadTrackTitles(JsonElement medium)
    {
        if (!medium.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            return [];

        return tracks.EnumerateArray()
            .Select(track => GetString(track, "title"))
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToList();
    }

    private static string ReadArtistCredit(JsonElement release)
    {
        if (!release.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array)
            return string.Empty;

        return string.Concat(credits.EnumerateArray().Select(credit =>
        {
            var name = GetString(credit, "name");
            var joinPhrase = GetString(credit, "joinphrase");
            return name + joinPhrase;
        })).Trim();
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int? GetNullableInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : null;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BookStitch", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/irwitzer/BookStitch)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
