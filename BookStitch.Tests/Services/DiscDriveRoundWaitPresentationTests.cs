using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscDriveRoundWaitPresentationTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("cs")]
    [InlineData("es")]
    [InlineData("fr")]
    public void Activity_cycles_one_to_five_without_drive_or_disc_details(string language)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(language);
            var prefix = LocalizationText.Get("Disc.DriveRound.Activity.Progress");
            for (var frame = 0; frame < 11; frame++)
                Assert.Equal(prefix + " " + new string('.', frame % 5 + 1), DiscDriveRoundWaitPresentation.ActivityProgress(frame));
            Assert.DoesNotContain("{", prefix);
            Assert.DoesNotContain("Disc.DriveRound", prefix);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("Audio-CD")]
    [InlineData("MP3-CD")]
    public void Unsuccessful_five_drive_round_has_neutral_expected_disc_and_waiting_progress(string media)
    {
        DiscPollingResult? notice = null;
        foreach (var drive in new[] { "U", "V", "X", "Y", "Z" })
            notice = DiscDriveRoundWaitPresentation.KeepActionable(notice,
                new(false, drive + " empty", drive, "checking " + drive), false);
        var result = DiscDriveRoundWaitPresentation.Complete(media, 3, 5, notice);
        Assert.False(result.CanImport);
        Assert.Equal(DiscPollingDisplayState.Waiting, result.DisplayState);
        Assert.Equal(LocalizationText.Format("Disc.DriveRound.Waiting.Dialog", media, 3, 5), result.DialogText);
        Assert.Equal(DiscDriveRoundWaitPresentation.WaitingProgress(media, 3, 5), result.ProgressText);
        Assert.DoesNotContain("Z", result.ProgressText);
    }

    [Theory]
    [InlineData(DiscPollingDisplayState.Waiting)]
    [InlineData(DiscPollingDisplayState.Duplicate)]
    [InlineData(DiscPollingDisplayState.Unsupported)]
    public void Actionable_error_or_duplicate_survives_later_empty_drive_but_finished_progress_is_neutral(DiscPollingDisplayState state)
    {
        var original = new DiscPollingResult(false, "action needed", "action status", "checking U", state);
        var notice = DiscDriveRoundWaitPresentation.KeepActionable(null, original, true);
        notice = DiscDriveRoundWaitPresentation.KeepActionable(notice, new(false, "Z empty", "Z", "checking Z"), false);
        var result = DiscDriveRoundWaitPresentation.Complete("fixture", 1, 1, notice);
        Assert.Equal(original.DialogText, result.DialogText);
        Assert.Equal(original.StatusText, result.StatusText);
        Assert.Equal(original.DisplayState, result.DisplayState);
        Assert.Equal(DiscDriveRoundWaitPresentation.WaitingProgress("fixture", 1, 1), result.ProgressText);
    }
}
