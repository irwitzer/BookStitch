using BookStitch.Models;

namespace BookStitch.Dialog;

public sealed class MetadataChapterPresentationRow
{
    public MetadataChapterPresentationRow(
        TrackInfo track,
        TrackEmbeddedChapterDisplayInfo? embeddedChapter = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        Track = track;
        EmbeddedChapter = embeddedChapter;
    }

    public TrackInfo Track { get; }
    public TrackEmbeddedChapterDisplayInfo? EmbeddedChapter { get; }
    public bool IsEmbeddedChapter => EmbeddedChapter is not null;
    public bool IsExcluded => Track.IsExcluded;
    public string ChapterProposal => EmbeddedChapter?.ChapterProposal ?? Track.DisplayChapterProposal;
    public string Duration => EmbeddedChapter?.Duration ?? Track.Duration;
    public string ChapterWarning => IsEmbeddedChapter ? string.Empty : Track.DisplayChapterWarning;
    public bool HasEmbeddedChapterDetails => !IsEmbeddedChapter && Track.HasEmbeddedChapterDetails;
    public string ChapterDetailsAutomationName => IsEmbeddedChapter
        ? string.Empty
        : Track.DisplayChapterDetailsAutomationName;

    public bool IsChapterDetailsExpanded
    {
        get => !IsEmbeddedChapter && Track.IsChapterDetailsExpanded;
        set
        {
            if (!IsEmbeddedChapter)
                Track.IsChapterDetailsExpanded = value;
        }
    }
}
