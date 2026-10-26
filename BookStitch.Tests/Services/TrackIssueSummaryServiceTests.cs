using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackIssueSummaryServiceTests
{
    private readonly TrackIssueSummaryService _service = new();

    [Fact]
    public void DisplayText_UsesUiCultureWhileKeepingTheErrorCount()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("Errores: 2", new TrackIssueSummary(2, 0).ToDisplayText());
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Create_UsesWarningCodeWithoutReadingDisplaySentence()
    {
        var track = new TrackInfo();
        track.SetWarning("A translated diagnostic", TrackWarningCodes.InvalidAudio);

        Assert.Equal(1, _service.Create([track]).ErrorCount);
    }

    [Fact]
    public void Create_CountsInvalidAudioWithoutLegacyHints()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                Warning = "Keine gültige Audiodatei erkannt.",
                Codec = "Ungültig",
                ProcessingAction = "Ungültig",
                AudioValidationPassed = false
            },
            new TrackInfo
            {
                Warning = "Keine Tracknummer erkannt; Kein Tag-Titel",
                Codec = "MP3",
                ProcessingAction = "Konvertieren",
                AudioValidationPassed = true
            },
            new TrackInfo
            {
                Codec = "AAC",
                ProcessingAction = "Konvertieren",
                AudioValidationPassed = true
            }
        };

        var result = _service.Create(tracks);

        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(0, result.HintCount);
        Assert.Equal("Fehler: 1", result.ToDisplayText());
    }

    [Fact]
    public void Create_CountsEachTrackOnlyOnce()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                Warning = "Keine gültige Audiodatei erkannt.",
                Codec = "Ungültig",
                ProcessingAction = "Ungültig",
                AudioValidationPassed = false
            }
        };

        var result = _service.Create(tracks);

        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(0, result.HintCount);
    }

    [Fact]
    public void Create_TreatsLegacyInvalidMarkersAsErrors()
    {
        var tracks = new[]
        {
            new TrackInfo { Warning = "Fehler", Codec = "Ungültig" },
            new TrackInfo { Warning = "Fehler", ProcessingAction = "Ungültig" }
        };

        var result = _service.Create(tracks);

        Assert.Equal(2, result.ErrorCount);
        Assert.Equal(0, result.HintCount);
    }
}
