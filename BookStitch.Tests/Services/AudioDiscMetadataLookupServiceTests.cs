using System.Text.Json;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscMetadataLookupServiceTests
{
    [Fact]
    public void ParseResponse_ReadsReleaseDiscAndTrackMetadata()
    {
        const string json = """
        {
          "releases": [
            {
              "id": "release-1",
              "title": "Kummer aller Art",
              "date": "2024",
              "country": "DE",
              "artist-credit": [
                { "name": "Katharina Quast", "joinphrase": "" }
              ],
              "media": [
                {
                  "position": 2,
                  "track-count": 2,
                  "tracks": [
                    { "title": "Kapitel eins" },
                    { "title": "Kapitel zwei" }
                  ]
                }
              ]
            }
          ]
        }
        """;
        using var document = JsonDocument.Parse(json);

        var result = AudioDiscMetadataLookupService.ParseResponse(document.RootElement, 2);

        Assert.Equal(AudioDiscMetadataLookupStatus.MatchesFound, result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Kummer aller Art", candidate.Title);
        Assert.Equal("Katharina Quast", candidate.Artist);
        Assert.Equal(2, candidate.DiscNumber);
        Assert.Equal(1, candidate.DiscCount);
        Assert.Equal(["Kapitel eins", "Kapitel zwei"], candidate.TrackTitles);
    }

    [Fact]
    public void ParseResponse_ReturnsNoMatchForMissingReleases()
    {
        using var document = JsonDocument.Parse("{}");

        var result = AudioDiscMetadataLookupService.ParseResponse(document.RootElement, 1);

        Assert.Equal(AudioDiscMetadataLookupStatus.NoMatch, result.Status);
        Assert.Empty(result.Candidates);
    }
}
