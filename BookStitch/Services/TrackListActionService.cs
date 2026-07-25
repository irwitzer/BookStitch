using BookStitch.Models;
using System.ComponentModel;
using System.Globalization;

namespace BookStitch.Services;

public sealed class TrackListActionService
{
    private readonly NaturalStringComparer _naturalStringComparer;
    private readonly ChapterNumberingService _chapterNumberingService;

    public TrackListActionService()
        : this(new NaturalStringComparer(), new ChapterNumberingService())
    {
    }

    public TrackListActionService(
        NaturalStringComparer naturalStringComparer,
        ChapterNumberingService chapterNumberingService)
    {
        _naturalStringComparer = naturalStringComparer ?? throw new ArgumentNullException(nameof(naturalStringComparer));
        _chapterNumberingService = chapterNumberingService ?? throw new ArgumentNullException(nameof(chapterNumberingService));
    }

    public List<TrackInfo> Sort(
        IReadOnlyList<TrackInfo> tracks,
        string sortKey,
        ListSortDirection direction)
    {
        var sortedTracks = tracks
            .Select((track, originalPosition) => new { Track = track, OriginalPosition = originalPosition })
            .ToList();

        sortedTracks.Sort((left, right) =>
        {
            var result = CompareTracksForSorting(left.Track, right.Track, sortKey);

            if (result != 0 && direction == ListSortDirection.Descending)
                result = -result;

            if (result == 0)
                result = left.OriginalPosition.CompareTo(right.OriginalPosition);

            return result;
        });

        return sortedTracks
            .Select(item => item.Track)
            .ToList();
    }

    public List<TrackInfo> MoveSelectedUp(
        IReadOnlyList<TrackInfo> visibleTracks,
        IReadOnlyList<TrackInfo> selectedInVisibleOrder)
    {
        var result = visibleTracks.ToList();
        var selectedSet = selectedInVisibleOrder.ToHashSet();

        foreach (var track in selectedInVisibleOrder)
        {
            var index = result.IndexOf(track);

            if (index > 0 && !selectedSet.Contains(result[index - 1]))
            {
                result.RemoveAt(index);
                result.Insert(index - 1, track);
            }
        }

        return result;
    }

    public List<TrackInfo> MoveSelectedDown(
        IReadOnlyList<TrackInfo> visibleTracks,
        IReadOnlyList<TrackInfo> selectedInVisibleOrder)
    {
        var result = visibleTracks.ToList();
        var selectedSet = selectedInVisibleOrder.ToHashSet();

        for (var i = selectedInVisibleOrder.Count - 1; i >= 0; i--)
        {
            var track = selectedInVisibleOrder[i];
            var index = result.IndexOf(track);

            if (index >= 0 && index < result.Count - 1 && !selectedSet.Contains(result[index + 1]))
            {
                result.RemoveAt(index);
                result.Insert(index + 1, track);
            }
        }

        return result;
    }

    public List<TrackInfo> MoveSelectedToTop(
        IReadOnlyList<TrackInfo> visibleTracks,
        IReadOnlyList<TrackInfo> selectedInVisibleOrder)
    {
        var selectedSet = selectedInVisibleOrder.ToHashSet();

        return selectedInVisibleOrder
            .Concat(visibleTracks.Where(track => !selectedSet.Contains(track)))
            .ToList();
    }

    public List<TrackInfo> MoveSelectedToBottom(
        IReadOnlyList<TrackInfo> visibleTracks,
        IReadOnlyList<TrackInfo> selectedInVisibleOrder)
    {
        var selectedSet = selectedInVisibleOrder.ToHashSet();

        return visibleTracks
            .Where(track => !selectedSet.Contains(track))
            .Concat(selectedInVisibleOrder)
            .ToList();
    }

    public int ExcludeSelected(IReadOnlyCollection<TrackInfo> selectedTracks)
    {
        var changed = 0;
        foreach (var track in selectedTracks.Where(track => !track.IsExcluded))
        {
            track.IsExcluded = true;
            track.ExcludedChapterTitle = track.ChapterTitle;
            track.ChapterTitle = string.Empty;
            changed++;
        }
        return changed;
    }

    public int RestoreSelected(IReadOnlyCollection<TrackInfo> selectedTracks)
    {
        var changed = 0;
        foreach (var track in selectedTracks.Where(track => track.IsExcluded))
        {
            track.IsExcluded = false;
            track.ChapterTitle = track.ExcludedChapterTitle;
            track.ExcludedChapterTitle = string.Empty;
            changed++;
        }
        return changed;
    }


    public TrackExclusionToggleResult ToggleSelectedForDelete(IReadOnlyCollection<TrackInfo> selectedTracks)
    {
        ArgumentNullException.ThrowIfNull(selectedTracks);

        if (selectedTracks.Count == 0)
            return new TrackExclusionToggleResult(TrackExclusionToggleAction.None, 0);

        if (selectedTracks.Any(track => track.IsExcluded))
        {
            return new TrackExclusionToggleResult(
                TrackExclusionToggleAction.Restored,
                RestoreSelected(selectedTracks));
        }

        return new TrackExclusionToggleResult(
            TrackExclusionToggleAction.Excluded,
            ExcludeSelected(selectedTracks));
    }

    public void Renumber(IList<TrackInfo> tracks, bool useLeadingZeros = true)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var activeChapterCount = tracks.Count(track => !track.IsExcluded);
        var activeIndex = 0;
        foreach (var track in tracks)
        {
            if (track.IsExcluded)
            {
                track.Index = 0;
                continue;
            }

            activeIndex++;
            track.Index = activeIndex;
            track.ChapterTitle = RenumberChapterTitle(
                track.ChapterTitle,
                activeIndex,
                activeChapterCount,
                useLeadingZeros);
        }
    }


    public int UpdateGeneratedChapterTitles(
        IList<TrackInfo> tracks,
        string? previousBookTitle,
        string? currentBookTitle,
        bool useLeadingZeros = true)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var previousTitle = (previousBookTitle ?? string.Empty).Trim();
        var currentTitle = (currentBookTitle ?? string.Empty).Trim();
        var activeChapterCount = tracks.Count(track => !track.IsExcluded);
        var changed = 0;

        foreach (var track in tracks)
        {
            if (track.IsExcluded)
                continue;

            if (!_chapterNumberingService.TryGetTitleWithoutNumber(track.ChapterTitle, out var currentSuffix))
                continue;

            var isGeneratedTitle = string.Equals(currentSuffix, "Kapitel", StringComparison.Ordinal)
                                   || (!string.IsNullOrWhiteSpace(previousTitle)
                                       && string.Equals(currentSuffix, previousTitle, StringComparison.Ordinal));
            if (!isGeneratedTitle)
                continue;

            var updatedTitle = _chapterNumberingService.BuildTitle(
                track.Index,
                activeChapterCount,
                string.IsNullOrWhiteSpace(currentTitle) ? "Kapitel" : currentTitle,
                useLeadingZeros);
            if (string.Equals(track.ChapterTitle, updatedTitle, StringComparison.Ordinal))
                continue;

            track.ChapterTitle = updatedTitle;
            changed++;
        }

        return changed;
    }

    public string RenumberChapterTitle(
        string? chapterTitle,
        int newIndex,
        int chapterCount,
        bool useLeadingZeros = true)
    {
        return _chapterNumberingService.ReformatLeadingNumber(
            chapterTitle,
            newIndex,
            chapterCount,
            useLeadingZeros);
    }

    private int CompareTracksForSorting(TrackInfo left, TrackInfo right, string sortKey)
    {
        return sortKey switch
        {
            "Index" => left.Index.CompareTo(right.Index),
            "DiscNumber" => CompareNullableInts(left.DiscNumber, right.DiscNumber),
            "TrackNumber" => CompareNullableInts(left.TrackNumber, right.TrackNumber),
            "FileName" => CompareNatural(left.FileName, right.FileName),
            "RelativeFolder" => CompareNatural(left.RelativeFolder, right.RelativeFolder),
            "TagTitle" => CompareNatural(left.TagTitle, right.TagTitle),
            "ChapterTitle" => CompareNatural(left.ChapterTitle, right.ChapterTitle),
            "Duration" => CompareDurations(left.Duration, right.Duration),
            "BitrateKbps" => CompareNullableInts(left.BitrateKbps, right.BitrateKbps),
            "ChannelLayout" => CompareNatural(left.ChannelLayout, right.ChannelLayout),
            "Status" => CompareNatural(left.Status, right.Status),
            "Warning" => CompareNatural(left.Warning, right.Warning),
            "FileWarningText" => CompareNatural(left.FileWarningText, right.FileWarningText),
            "ChapterWarningText" => CompareNatural(left.ChapterWarningText, right.ChapterWarningText),
            "Extension" => CompareNatural(left.Extension, right.Extension),
            "Codec" => CompareNatural(left.Codec, right.Codec),
            "ProcessingAction" => CompareNatural(left.ProcessingAction, right.ProcessingAction),
            "SizeMb" => left.SizeMb.CompareTo(right.SizeMb),
            "ConvertedSizeMb" => left.ConvertedSizeMb.CompareTo(right.ConvertedSizeMb),
            _ => CompareNatural(left.FileName, right.FileName)
        };
    }

    private int CompareNatural(string? left, string? right)
    {
        return _naturalStringComparer.Compare(left ?? "", right ?? "");
    }

    private static int CompareNullableInts(int? left, int? right)
    {
        if (left.HasValue && right.HasValue)
            return left.Value.CompareTo(right.Value);

        if (left.HasValue)
            return -1;

        if (right.HasValue)
            return 1;

        return 0;
    }

    private static int CompareDurations(string? left, string? right)
    {
        var leftTicks = TryParseDurationTicks(left);
        var rightTicks = TryParseDurationTicks(right);

        if (leftTicks.HasValue && rightTicks.HasValue)
            return leftTicks.Value.CompareTo(rightTicks.Value);

        if (leftTicks.HasValue)
            return -1;

        if (rightTicks.HasValue)
            return 1;

        return string.Compare(left ?? "", right ?? "", StringComparison.CurrentCultureIgnoreCase);
    }

    private static long? TryParseDurationTicks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length is < 2 or > 3)
            return null;

        if (!parts.All(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return null;

        try
        {
            if (parts.Length == 2)
            {
                var minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var seconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
                return new TimeSpan(0, 0, minutes, seconds).Ticks;
            }

            var hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var mins = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var secs = int.Parse(parts[2], CultureInfo.InvariantCulture);
            return new TimeSpan(hours, mins, secs).Ticks;
        }
        catch
        {
            return null;
        }
    }
}


public enum TrackExclusionToggleAction
{
    None,
    Excluded,
    Restored
}

public sealed record TrackExclusionToggleResult(TrackExclusionToggleAction Action, int ChangedCount);
