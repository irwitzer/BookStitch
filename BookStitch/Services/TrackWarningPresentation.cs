using BookStitch.Models;

namespace BookStitch.Services;

public static class TrackWarningPresentation
{
    public static string Format(TrackWarningCodes code) => LocalizationText.Get(code switch
    {
        TrackWarningCodes.InvalidAudio => "WarningInvalidAudio",
        TrackWarningCodes.MissingSource => "WarningMissingSource",
        TrackWarningCodes.EmptySource => "WarningEmptySource",
        TrackWarningCodes.EmptyAac => "WarningEmptyAac",
        TrackWarningCodes.Undecodable => "WarningUndecodable",
        TrackWarningCodes.DuplicateChapterName => "WarningDuplicateChapterName",
        TrackWarningCodes.MissingChapter => "WarningMissingChapter",
        TrackWarningCodes.DuplicateChapter => "WarningDuplicateChapter",
        TrackWarningCodes.CheckOrder => "WarningCheckOrder",
        TrackWarningCodes.MissingTagTitle => "WarningMissingTagTitle",
        TrackWarningCodes.UnreadableTags => "WarningUnreadableTags",
        _ => throw new ArgumentOutOfRangeException(nameof(code))
    });
}
