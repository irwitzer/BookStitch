using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscPollingServiceTests
{
    [Fact]
    public void EvaluateReadResult_WhenNoAudioDiscWasRead_ReturnsWaitingResult()
    {
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.NotAudioDisc(),
            discNumber: 2,
            totalDiscs: 4,
            new HashSet<string>(),
            duplicateDiscWasEjected: false);

        Assert.False(result.PollingResult.CanImport);
        Assert.Null(result.Disc);
        Assert.Contains("Audio-CD 2", result.PollingResult.DialogText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateReadResult_WhenDiscIsAlreadyKnown_ReturnsDuplicateAndEjectMessage()
    {
        var disc = CreateDisc("known-disc");

        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc),
            discNumber: 2,
            totalDiscs: 3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known-disc" },
            duplicateDiscWasEjected: true);

        Assert.False(result.PollingResult.CanImport);
        Assert.Same(disc, result.Disc);
        Assert.Contains("bereits aufgenommen", result.PollingResult.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bereits verwendete Audio-CD erkannt", result.PollingResult.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ausgeworfen", result.PollingResult.DialogText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ausgeworfen", result.PollingResult.ProgressText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiscPollingDisplayState.Duplicate, result.PollingResult.DisplayState);
    }

    [Fact]
    public void EvaluateReadResult_WhenDiscIsNew_ReturnsAcceptedDiscAndDiagnostics()
    {
        var disc = CreateDisc("new-disc");
        var driveInfo = new DiscDriveInfo("G:\\", "G:", true, "Audio CD");

        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc),
            discNumber: 2,
            totalDiscs: 3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "first-disc" },
            duplicateDiscWasEjected: false,
            driveInfo);

        Assert.True(result.PollingResult.CanImport);
        Assert.Same(disc, result.Disc);
        Assert.Same(driveInfo, result.DriveInfo);
        Assert.Contains("Ripping startet", result.PollingResult.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatUnsupportedDiscMessage_UsesUnifiedWording()
    {
        var message = AudioDiscPollingService.FormatUnsupportedDiscMessage(ejectedWrongType: true, discNumber: 2, totalDiscs: 3);

        Assert.StartsWith("Keine Audio-CD erkannt. / Die CD wurde wieder ausgeworfen.", message, StringComparison.Ordinal);
        Assert.Contains("Bitte Audio-CD 2 von 3 einlegen.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatWrongRequiredDiscMessage_UsesExplicitRequiredDiscWording()
    {
        var message = AudioDiscPollingService.FormatWrongRequiredDiscMessage(ejectedWrongDisc: true, discNumber: 2, totalDiscs: 3);

        Assert.StartsWith("Das ist nicht die benötigte Audio-CD. Die falsche Audio-CD wurde wieder ausgeworfen.", message, StringComparison.Ordinal);
        Assert.Contains("Bitte Audio-CD 2 von 3 einlegen.", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DiscMediaKind.Mp3Disc)]
    [InlineData(DiscMediaKind.DataDisc)]
    public void IsClearlyNotAudioDisc_WhenDataMediaWasDetected_ReturnsTrue(DiscMediaKind mediaKind)
    {
        Assert.True(AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind));
    }

    [Theory]
    [InlineData(DiscMediaKind.AudioCd)]
    [InlineData(DiscMediaKind.Empty)]
    [InlineData(DiscMediaKind.Unknown)]
    public void IsClearlyNotAudioDisc_WhenMediaIsNotClearlyData_ReturnsFalse(DiscMediaKind mediaKind)
    {
        Assert.False(AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind));
    }

    private static AudioDiscInfo CreateDisc(string identity)
    {
        var track = new AudioDiscTrackInfo(
            1,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromMinutes(3),
            "track-1",
            150);

        return new AudioDiscInfo(
            "G:\\",
            "G:",
            [track],
            track.Duration,
            identity,
            new AudioDiscToc(1, 1, 13650, [150], "mb-id"));
    }
}
