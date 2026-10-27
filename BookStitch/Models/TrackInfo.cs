using BookStitch.Services;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BookStitch.Models;

public sealed class TrackEmbeddedChapterInfo
{
    public string Title { get; set; } = "";
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }

    public long DurationTicks => Math.Max(0, EndTicks - StartTicks);
}

public sealed class TrackEmbeddedChapterDisplayInfo : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _chapterProposal = "";
    private string _timelineStart = "";

    public TrackInfo? SourceTrack { get; init; }
    public TrackEmbeddedChapterInfo? SourceChapter { get; init; }
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public long StartTicks { get; init; }
    public long EndTicks { get; init; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }
    public string ChapterProposal
    {
        get => _chapterProposal;
        private set
        {
            if (string.Equals(_chapterProposal, value, StringComparison.Ordinal))
                return;

            _chapterProposal = value;
            OnPropertyChanged();
        }
    }
    public string TimelineStart
    {
        get => _timelineStart;
        private set
        {
            if (string.Equals(_timelineStart, value, StringComparison.Ordinal))
                return;

            _timelineStart = value;
            OnPropertyChanged();
        }
    }
    public string Start { get; init; } = "";
    public string Duration { get; init; } = "";

    public void ApplyTimelinePresentation(string chapterProposal, string timelineStart)
    {
        ChapterProposal = chapterProposal ?? string.Empty;
        TimelineStart = timelineStart ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class TrackInfo : INotifyPropertyChanged
{
    public const string WarningMessageSeparator = " · ";

    private string _status = "";
    private string _displayChapterProposalOverride = "";
    private string _displayChapterStart = "";
    private bool _isChapterDetailsExpanded;
    private long? _selectedEmbeddedChapterStartTicks;
    private IReadOnlyList<TrackEmbeddedChapterDisplayInfo>? _embeddedChapterRows;
    private List<TrackEmbeddedChapterInfo>? _embeddedChapterRowsSource;
    private int _embeddedChapterRowsSourceCount;
    public Guid SourceId { get; set; }

    public int Index { get; set; }

    public bool IsExcluded { get; set; }
    public string ExcludedChapterTitle { get; set; } = "";
    public string DisplayIndex => IsExcluded ? "" : Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Source tag structure is immutable evidence and must not be confused with
    // working/import order or the independent output track fields in ProjectMetadata.
    public int? SourceDiscNumber { get; set; }
    public int? SourceDiscTotal { get; set; }
    public int? SourceTrackNumber { get; set; }
    public int? SourceTrackTotal { get; set; }

    public int? DiscNumber { get; set; }
    public int? TrackNumber { get; set; }
    public int? AutomaticOrderPosition { get; set; }
    public string DisplayAutomaticOrderPosition => AutomaticOrderPosition?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativeFolder { get; set; } = "";
    public string ProjectRelativeFolder { get; set; } = "";
    public bool HideFolderDisplay { get; set; }
    public string DisplayFolder => HideFolderDisplay
        ? string.Empty
        : string.IsNullOrWhiteSpace(ProjectRelativeFolder)
        ? RelativeFolder
        : ProjectRelativeFolder;
    public string Extension { get; set; } = "";

    public string TagTitle { get; set; } = "";
    public string Artist { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public SourceMetadataCanonicalFields? SourceMetadataFields { get; set; }
    public string ChapterTitle { get; set; } = "";
    public List<TrackEmbeddedChapterInfo> EmbeddedChapters { get; set; } = [];
    public bool HasEmbeddedChapterDetails => EmbeddedChapters.Count > 1;
    public string DisplayChapterProposal => !string.IsNullOrWhiteSpace(_displayChapterProposalOverride)
        ? _displayChapterProposalOverride
        : HasEmbeddedChapterDetails
        ? LocalizationText.Format("Track.Chapters.EmbeddedCount", EmbeddedChapters.Count)
        : ChapterTitle;
    public string DisplayChapterStart => _displayChapterStart;
    public bool IsChapterDetailsExpanded
    {
        get => _isChapterDetailsExpanded;
        set
        {
            if (_isChapterDetailsExpanded == value)
                return;

            _isChapterDetailsExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayChapterDetailsToggleText));
            OnPropertyChanged(nameof(DisplayChapterDetailsAutomationName));
        }
    }
    public long? SelectedEmbeddedChapterStartTicks
    {
        get => _selectedEmbeddedChapterStartTicks;
        set
        {
            if (_selectedEmbeddedChapterStartTicks == value)
                return;

            SelectEmbeddedChapter(value);
        }
    }
    public string DisplayChapterDetailsToggleText => LocalizationText.Get(IsChapterDetailsExpanded
        ? "Track.Chapters.HideDetails"
        : "Track.Chapters.ShowDetails");
    public string DisplayChapterDetailsAutomationName => LocalizationText.Format(
        "Track.Chapters.DetailsAutomation", DisplayChapterDetailsToggleText, FileName);
    public bool HasMaterializedEmbeddedChapterRows => _embeddedChapterRows is not null;
    public IReadOnlyList<TrackEmbeddedChapterDisplayInfo> EmbeddedChapterRows
    {
        get
        {
            if (_embeddedChapterRows is null ||
                !ReferenceEquals(_embeddedChapterRowsSource, EmbeddedChapters) ||
                _embeddedChapterRowsSourceCount != EmbeddedChapters.Count)
            {
                _embeddedChapterRows = BuildEmbeddedChapterRows();
                _embeddedChapterRowsSource = EmbeddedChapters;
                _embeddedChapterRowsSourceCount = EmbeddedChapters.Count;
            }

            return _embeddedChapterRows;
        }
    }

    public void SelectEmbeddedChapter(long? startTicks)
    {
        if (_selectedEmbeddedChapterStartTicks == startTicks)
            return;

        var previousStartTicks = _selectedEmbeddedChapterStartTicks;
        _selectedEmbeddedChapterStartTicks = startTicks;

        UpdateEmbeddedChapterRowSelection(previousStartTicks, false);
        UpdateEmbeddedChapterRowSelection(startTicks, true);
        OnPropertyChanged(nameof(SelectedEmbeddedChapterStartTicks));
    }

    public void ApplyTimelinePresentation(string chapterProposal, string chapterStart)
    {
        var normalizedProposal = chapterProposal ?? string.Empty;
        var normalizedStart = chapterStart ?? string.Empty;

        if (!string.Equals(_displayChapterProposalOverride, normalizedProposal, StringComparison.Ordinal))
        {
            _displayChapterProposalOverride = normalizedProposal;
            OnPropertyChanged(nameof(DisplayChapterProposal));
        }

        if (!string.Equals(_displayChapterStart, normalizedStart, StringComparison.Ordinal))
        {
            _displayChapterStart = normalizedStart;
            OnPropertyChanged(nameof(DisplayChapterStart));
        }
    }

    private IReadOnlyList<TrackEmbeddedChapterDisplayInfo> BuildEmbeddedChapterRows() =>
        EmbeddedChapters
            .OrderBy(chapter => chapter.StartTicks)
            .Select((chapter, index) => new TrackEmbeddedChapterDisplayInfo
            {
                SourceTrack = this,
                SourceChapter = chapter,
                Number = index + 1,
                Title = string.IsNullOrWhiteSpace(chapter.Title)
                    ? LocalizationText.Format("Track.Chapters.FallbackTitle", index + 1)
                    : chapter.Title.Trim(),
                StartTicks = chapter.StartTicks,
                EndTicks = chapter.EndTicks,
                IsSelected = SelectedEmbeddedChapterStartTicks == chapter.StartTicks,
                Start = FormatTicks(chapter.StartTicks),
                Duration = FormatTicks(chapter.DurationTicks)
            })
            .ToList();

    private void UpdateEmbeddedChapterRowSelection(long? startTicks, bool isSelected)
    {
        if (startTicks is null || _embeddedChapterRows is null)
            return;

        foreach (var row in _embeddedChapterRows)
        {
            if (row.StartTicks == startTicks.Value)
                row.IsSelected = isSelected;
        }
    }
    public string Status
    {
        get => !string.IsNullOrWhiteSpace(_status) ? _status : ResolveStatusText();
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_status, normalized, StringComparison.Ordinal))
                return;

            _status = normalized;
            OnPropertyChanged();
        }
    }
    private string _warning = "";
    private string _fileWarningText = "";
    private string _chapterWarningText = "";
    public string Warning
    {
        get => _warning;
        set { _warning = value ?? ""; WarningCodes = LegacyTrackWarningReader.Read(_warning); }
    }
    public string FileWarningText
    {
        get => _fileWarningText;
        set => _fileWarningText = value ?? "";
    }
    public string ChapterWarningText
    {
        get => _chapterWarningText;
        set => _chapterWarningText = value ?? "";
    }
    public TrackWarningCodes WarningCodes { get; set; }
    public void SetWarning(string text, TrackWarningCodes codes)
    {
        _warning = text ?? string.Empty;
        WarningCodes = codes;
    }
    public string DisplayFileWarning => ResolveFileWarningText();
    public string DisplayChapterWarning => ResolveChapterWarningText();
    public bool HasFileWarning => !string.IsNullOrWhiteSpace(DisplayFileWarning);
    public bool HasChapterWarning => !string.IsNullOrWhiteSpace(DisplayChapterWarning);

    public string DisplayHintWarning
    {
        get => string.Empty;
    }

    public bool HasBlockingFileWarning
    {
        get
        {
            var codes = WarningCodes | LegacyTrackWarningReader.Read(FileWarningText);
            return AudioValidationPassed == false ||
                   (codes & (TrackWarningCodes.InvalidAudio | TrackWarningCodes.MissingSource |
                             TrackWarningCodes.EmptySource | TrackWarningCodes.EmptyAac |
                             TrackWarningCodes.Undecodable)) != 0;
        }
    }

    public string Duration { get; set; } = "";
    public long? DurationTicks { get; set; }
    private int? _bitrateKbps;
    public int? BitrateKbps
    {
        get => _bitrateKbps;
        set
        {
            if (_bitrateKbps == value) return;
            _bitrateKbps = value;
            OnPropertyChanged();
        }
    }
    private int? _channels;
    public int? Channels
    {
        get => _channels;
        set
        {
            if (_channels == value) return;
            _channels = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayOriginalChannelLayout));
        }
    }
    public string DisplayOriginalChannelLayout => Channels is > 0
        ? AudioProcessingService.FormatChannelLayout(Channels) : string.Empty;
    private int? _sampleRateHz;
    public int? SampleRateHz
    {
        get => _sampleRateHz;
        set
        {
            if (_sampleRateHz == value) return;
            _sampleRateHz = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayOriginalSampleRateHz));
        }
    }
    public string DisplayOriginalSampleRateHz => SampleRateHz is > 0
        ? SampleRateHz.Value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))
        : string.Empty;

    // Processing context, never source metadata. Folder manifest owns the durable decision.
    public int TargetSampleRateHz { get; set; } = 44100;
    // Runtime-only evidence from the successful technical AAC probe/decode path.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AacOptimizationEligible { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AacConfigurationId { get; set; }
    public bool AllowAacReuse { get; set; } = true;
    public string ChannelLayout { get; set; } = "";

    public double SizeMb { get; set; }
    public bool SourceSizeAvailable { get; set; }
    public string DisplaySizeMb => SourceSizeAvailable
        ? SizeMb.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        : "";

    public bool ConvertedSizeAvailable { get; set; }
    public string Codec { get; set; } = "";
    public string DisplayCodec => CodecIds.IsInvalid(Codec) ? LocalizationText.Get("CodecInvalid") : Codec;
    public string ProcessingAction { get; set; } = "";
    public string PreparedConvertedPath { get; set; } = "";
    private string _preparedConvertedPreset = "";
    public string PreparedConvertedPreset
    {
        get => _preparedConvertedPreset;
        set
        {
            if (_preparedConvertedPreset == value) return;
            _preparedConvertedPreset = value;
            OnPropertyChanged();
            NotifyPreparedPresentationChanged();
        }
    }
    private bool _hasReusableConvertedFile;
    public bool HasReusableConvertedFile
    {
        get => _hasReusableConvertedFile;
        set
        {
            if (_hasReusableConvertedFile == value) return;
            _hasReusableConvertedFile = value;
            OnPropertyChanged();
            NotifyPreparedPresentationChanged();
        }
    }
    public int? DisplayPreparedBitrateKbps => HasReusableConvertedFile && !string.IsNullOrWhiteSpace(PreparedConvertedPreset)
        ? ExportPreset.Parse(PreparedConvertedPreset).BitrateKbps : null;
    public string DisplayPreparedChannelLayout => DisplayPreparedBitrateKbps.HasValue
        ? ExportPreset.Parse(PreparedConvertedPreset).Channels == 1 ? "Mono" : "Stereo" : "";

    private void NotifyPreparedPresentationChanged()
    {
        OnPropertyChanged(nameof(DisplayPreparedBitrateKbps));
        OnPropertyChanged(nameof(DisplayPreparedChannelLayout));
        OnPropertyChanged(nameof(DisplayProcessingAction));
    }
    public string DisplayProcessingActionOverride { get; set; } = "";
    public string DisplayProcessingAction => !string.IsNullOrWhiteSpace(DisplayProcessingActionOverride)
        ? DisplayProcessingActionOverride
        : HasReusableConvertedFile
            ? LocalizationText.Get("ActionReusePrepared")
            : ProcessingActionPresentation.Format(ProcessingAction);
    public bool? AudioValidationPassed { get; set; }

    private string ResolveStatusText()
    {
        if (IsExcluded)
            return LocalizationText.Get("StatusExcluded");

        if (HasReusableConvertedFile || ConvertedSizeAvailable)
            return LocalizationText.Get("StatusConverted");

        if (ProcessingActionIds.Normalize(ProcessingAction) == ProcessingActionIds.RipFlac ||
            (string.IsNullOrWhiteSpace(FilePath) && !SourceSizeAvailable && !ConvertedSizeAvailable))
        {
            return LocalizationText.Get("StatusNotRipped");
        }

        if (ProcessingActionIds.Normalize(ProcessingAction) == ProcessingActionIds.Inspect && HasBlockingFileWarning)
            return LocalizationText.Get("StatusError");

        return LocalizationText.Get("StatusReady");
    }

    private string ResolveFileWarningText()
    {
        if (!string.IsNullOrWhiteSpace(FileWarningText))
            return FileWarningText;

        var messages = new List<string>();

        if (WarningCodes.HasFlag(TrackWarningCodes.InvalidAudio) ||
            CodecIds.IsInvalid(Codec) ||
            AudioValidationPassed == false)
        {
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.InvalidAudio));
        }

        if (WarningCodes.HasFlag(TrackWarningCodes.MissingSource))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.MissingSource));

        if (WarningCodes.HasFlag(TrackWarningCodes.EmptySource))
        {
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.EmptySource));
        }

        if (WarningCodes.HasFlag(TrackWarningCodes.EmptyAac))
        {
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.EmptyAac));
        }

        if (WarningCodes.HasFlag(TrackWarningCodes.Undecodable))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.Undecodable));

        return JoinWarningMessages(messages);
    }

    private string ResolveChapterWarningText()
    {
        if (!string.IsNullOrWhiteSpace(ChapterWarningText))
            return ChapterWarningText;

        var messages = new List<string>();

        if (WarningCodes.HasFlag(TrackWarningCodes.DuplicateChapterName))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapterName));

        if (WarningCodes.HasFlag(TrackWarningCodes.MissingChapter))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter));

        if (WarningCodes.HasFlag(TrackWarningCodes.DuplicateChapter))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter));

        if (WarningCodes.HasFlag(TrackWarningCodes.CheckOrder))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder));

        if (WarningCodes.HasFlag(TrackWarningCodes.UnreadableTags))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags));

        return JoinWarningMessages(messages);
    }

    private static string JoinWarningMessages(IEnumerable<string> messages)
    {
        var normalized = new List<string>();
        foreach (var message in messages)
            AddWarningMessage(normalized, message);

        return string.Join(WarningMessageSeparator, normalized);
    }

    public static IReadOnlyList<string> SplitWarningMessages(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split([WarningMessageSeparator, "\r\n", "\n", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string AppendWarningMessage(string? value, string? message)
    {
        var messages = SplitWarningMessages(value).ToList();
        AddWarningMessage(messages, message);
        return string.Join(WarningMessageSeparator, messages);
    }

    private static void AddWarningMessage(ICollection<string> messages, string? message)
    {
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            messages.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        messages.Add(normalized);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatTicks(long ticks)
    {
        var duration = TimeSpan.FromTicks(Math.Max(0, ticks));
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
