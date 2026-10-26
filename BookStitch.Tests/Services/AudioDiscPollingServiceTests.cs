using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscPollingServiceTests
{
    [Fact]
    public void Mixed_mode_disc_is_rejected_with_its_specific_message()
    {
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.MixedModeUnsupported("unsupported mixed mode"), 1, 1, new HashSet<string>(), false);
        Assert.False(result.PollingResult.CanImport);
        Assert.Equal(DiscPollingDisplayState.Unsupported, result.PollingResult.DisplayState);
        Assert.Equal("unsupported mixed mode", result.PollingResult.DialogText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Required_disc_search_respects_the_selected_drive_and_allowed_circle_order(bool circle)
    {
        var drives = new[]
        {
            new DiscDriveInfo(@"U:\", "U:", true, "First"),
            new DiscDriveInfo(@"V:\", "V:", true, "Second"),
            new DiscDriveInfo(@"Y:\", "Y:", true, "Third")
        };
        var roots = circle ? new[] { @"v:\", @"Y:\", @"U:\" } : new[] { @"V:\" };
        var ordered = AudioDiscPollingService.OrderRequiredDiscDrives(drives, roots);
        Assert.Equal(circle ? new[] { "V:", "Y:", "U:" } : new[] { "V:" }, ordered.Select(d => d.DriveLetter));
        Assert.Empty(AudioDiscPollingService.OrderRequiredDiscDrives(drives, []));
        Assert.Equal(drives, AudioDiscPollingService.OrderRequiredDiscDrives(drives, null));
    }

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
        Assert.Equal(DiscPollingDisplayState.Waiting, result.PollingResult.DisplayState);
        Assert.Null(result.Disc);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.NoReadableDisc.Dialog", 2, 4), result.PollingResult.DialogText);
    }

    [Fact]
    public void EvaluateReadResult_WhenDiscIsAlreadyKnown_ReturnsDuplicateAndEjectMessage()
    {
        var disc = CreateDisc("known-disc");
        var result = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc), 2, 3,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known-disc" },
            duplicateDiscWasEjected: true);

        Assert.False(result.PollingResult.CanImport);
        Assert.Same(disc, result.Disc);
        Assert.Equal(DiscPollingDisplayState.Duplicate, result.PollingResult.DisplayState);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.Duplicate.Ejected.Dialog", 2, 3), result.PollingResult.DialogText);
        Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.Duplicate.Ejected.Progress", 2), result.PollingResult.ProgressText);
    }

    [Fact]
    public void EvaluateReadResult_DuplicateOverrideDoesNotChangeIdentityHistoryAndMustBeRequestedAgain()
    {
        var disc = CreateDisc("known-disc");
        var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known-disc" };

        var accepted = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc), 2, 3, imported,
            duplicateDiscWasEjected: false,
            acceptDuplicate: true);
        var rejectedAgain = AudioDiscPollingService.EvaluateReadResult(
            AudioDiscReadResult.Success(disc), 2, 3, imported,
            duplicateDiscWasEjected: false);

        Assert.True(accepted.PollingResult.CanImport);
        Assert.False(rejectedAgain.PollingResult.CanImport);
        Assert.Equal(DiscPollingDisplayState.Duplicate, rejectedAgain.PollingResult.DisplayState);
        Assert.Equal(["known-disc"], imported);
    }

    [Fact]
    public void EvaluateReadResult_WhenDiscIsNew_LocalizesDialogStatusAndProgressFromCatalog()
    {
        WithUiCulture("en-US", () =>
        {
            var disc = CreateDisc("new-disc");
            var driveInfo = new DiscDriveInfo("G:\\", "G:", true, "Audio CD");
            var result = AudioDiscPollingService.EvaluateReadResult(
                AudioDiscReadResult.Success(disc), 2, 3,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "first-disc" },
                duplicateDiscWasEjected: false,
                driveInfo);

            Assert.True(result.PollingResult.CanImport);
            Assert.Equal(DiscPollingDisplayState.Ready, result.PollingResult.DisplayState);
            Assert.Same(disc, result.Disc);
            Assert.Same(driveInfo, result.DriveInfo);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.Next.Ready.Dialog", 2, 3), result.PollingResult.DialogText);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.Ready.Status", 2, 3), result.PollingResult.StatusText);
            Assert.Equal(LocalizationText.Format("Disc.Polling.Audio.Next.Ready.Progress", 2), result.PollingResult.ProgressText);
        });
    }

    [Theory]
    [InlineData(true, "Disc.Polling.Audio.Unsupported.Ejected.Dialog")]
    [InlineData(false, "Disc.Polling.Audio.Unsupported.ManualEject.Dialog")]
    public void FormatUnsupportedDiscMessage_UsesLocalizedCompleteTemplate(bool ejected, string resourceKey)
    {
        WithUiCulture("en-US", () =>
        {
            var message = AudioDiscPollingService.FormatUnsupportedDiscMessage(ejected, 2, 3);
            Assert.Equal(LocalizationText.Format(resourceKey, 2, 3), message);
        });
    }

    [Theory]
    [InlineData(true, "Disc.Polling.Audio.WrongRequired.Ejected.Dialog")]
    [InlineData(false, "Disc.Polling.Audio.WrongRequired.ManualEject.Dialog")]
    public void FormatWrongRequiredDiscMessage_UsesLocalizedCompleteTemplate(bool ejected, string resourceKey)
    {
        WithUiCulture("en-US", () =>
        {
            var message = AudioDiscPollingService.FormatWrongRequiredDiscMessage(ejected, 2, 3);
            Assert.Equal(LocalizationText.Format(resourceKey, 2, 3), message);
        });
    }

    [Theory]
    [InlineData(DiscMediaKind.Mp3Disc)]
    [InlineData(DiscMediaKind.DataDisc)]
    public void IsClearlyNotAudioDisc_WhenDataMediaWasDetected_ReturnsTrue(DiscMediaKind mediaKind) =>
        Assert.True(AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind));

    [Theory]
    [InlineData(DiscMediaKind.AudioCd)]
    [InlineData(DiscMediaKind.Empty)]
    [InlineData(DiscMediaKind.Unknown)]
    public void IsClearlyNotAudioDisc_WhenMediaIsNotClearlyData_ReturnsFalse(DiscMediaKind mediaKind) =>
        Assert.False(AudioDiscPollingService.IsClearlyNotAudioDisc(mediaKind));

    private static void WithUiCulture(string cultureName, Action action)
    {
        var original = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName); action(); }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    private static AudioDiscInfo CreateDisc(string identity)
    {
        var track = new AudioDiscTrackInfo(1, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3), "track-1", 150);
        return new AudioDiscInfo("G:\\", "G:", [track], track.Duration, identity,
            new AudioDiscToc(1, 1, 13650, [150], "mb-id"));
    }
}
