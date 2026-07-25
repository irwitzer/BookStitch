using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscPollingServiceV1AutomationTests
{
    [Fact]
    public void EvaluateReadResult_NonAudioDisc_WaitsWithoutImport()
    {
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.NotAudioDisc(),
            discNumber: 2,
            totalDiscs: 4,
            importedDiscIdentities: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            duplicateDiscWasEjected: false);

        Assert.False(result.PollingResult.CanImport);
        Assert.Equal(DiscPollingDisplayState.Waiting, result.PollingResult.DisplayState);
        Assert.Null(result.Disc);
    }

    [Fact]
    public void EvaluateReadResult_DuplicateAudioDisc_WaitsAndReportsDuplicate()
    {
        var disc = Disc("duplicate-disc");
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc),
            discNumber: 2,
            totalDiscs: 4,
            importedDiscIdentities: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "duplicate-disc" },
            duplicateDiscWasEjected: true);

        Assert.False(result.PollingResult.CanImport);
        Assert.Equal(DiscPollingDisplayState.Duplicate, result.PollingResult.DisplayState);
        Assert.Same(disc, result.Disc);
    }

    [Fact]
    public void EvaluateReadResult_NewAudioDisc_IsReadyForImport()
    {
        var disc = Disc("new-disc");
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc),
            discNumber: 2,
            totalDiscs: 4,
            importedDiscIdentities: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "old-disc" },
            duplicateDiscWasEjected: false,
            driveInfo: new DiscDriveInfo(@"G:\", "G:", true, "Audio", DiscMediaKind.AudioCd));

        Assert.True(result.PollingResult.CanImport);
        Assert.Equal(DiscPollingDisplayState.Ready, result.PollingResult.DisplayState);
        Assert.Same(disc, result.Disc);
        Assert.NotNull(result.DriveInfo);
    }

    private static AudioDiscInfo Disc(string identity) => new(
        @"G:\",
        "G:",
        [new AudioDiscTrackInfo(1, TimeSpan.Zero, TimeSpan.FromMinutes(3), identity + ":track:1")],
        TimeSpan.FromMinutes(3),
        identity);
}
