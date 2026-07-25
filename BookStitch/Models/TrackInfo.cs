namespace BookStitch.Models;

public sealed class TrackEmbeddedChapterInfo
{
    public string Title { get; set; } = "";
    public long StartTicks { get; set; }
    public long EndTicks { get; set; }

    public long DurationTicks => Math.Max(0, EndTicks - StartTicks);
}

public sealed class TrackInfo
{
    private string _status = "";
    public int Index { get; set; }

    public bool IsExcluded { get; set; }
    public string ExcludedChapterTitle { get; set; } = "";
    public string DisplayIndex => IsExcluded ? "" : Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public int? DiscNumber { get; set; }
    public int? TrackNumber { get; set; }

    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RelativeFolder { get; set; } = "";
    public string Extension { get; set; } = "";

    public string TagTitle { get; set; } = "";
    public string Artist { get; set; } = "";
    public string ChapterTitle { get; set; } = "";
    public List<TrackEmbeddedChapterInfo> EmbeddedChapters { get; set; } = [];
    public string Status
    {
        get => !string.IsNullOrWhiteSpace(_status) ? _status : ResolveStatusText();
        set => _status = value ?? string.Empty;
    }
    public string Warning { get; set; } = "";
    public string FileWarningText { get; set; } = "";
    public string ChapterWarningText { get; set; } = "";
    public string DisplayFileWarning
    {
        get
        {
            var warning = ResolveFileWarningText();
            return string.IsNullOrWhiteSpace(warning)
                ? ""
                : "⛔ " + warning;
        }
    }
    public string DisplayChapterWarning => string.IsNullOrWhiteSpace(ChapterWarningText)
        ? ""
        : "⚠️ " + ChapterWarningText;

    public bool HasBlockingFileWarning
    {
        get
        {
            var warning = ResolveFileWarningText();
            return string.Equals(warning, "Keine gültige Audiodatei", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(warning, "Quelldatei fehlt", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(warning, "Quelldatei leer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(warning, "AAC-Datei leer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(warning, "Nicht dekodierbar", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string Duration { get; set; } = "";
    public long? DurationTicks { get; set; }
    public int? BitrateKbps { get; set; }
    public int? Channels { get; set; }
    public string ChannelLayout { get; set; } = "";

    public double SizeMb { get; set; }
    public bool SourceSizeAvailable { get; set; }
    public string DisplaySizeMb => SourceSizeAvailable
        ? SizeMb.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        : "";

    public double ConvertedSizeMb { get; set; }
    public bool ConvertedSizeAvailable { get; set; }
    public string DisplayConvertedSizeMb => DisplayOutputSizeMb;
    public string DisplayOutputSizeMb => ConvertedSizeAvailable
        ? ConvertedSizeMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
        : "–";
    public string Codec { get; set; } = "";
    public string ProcessingAction { get; set; } = "";
    public string PreparedConvertedPath { get; set; } = "";
    public string PreparedConvertedPreset { get; set; } = "";
    public bool HasReusableConvertedFile { get; set; }
    public string DisplayProcessingActionOverride { get; set; } = "";
    public string DisplayProcessingAction => !string.IsNullOrWhiteSpace(DisplayProcessingActionOverride)
        ? DisplayProcessingActionOverride
        : HasReusableConvertedFile
            ? "Wiederverwenden"
            : ProcessingAction;
    public bool? AudioValidationPassed { get; set; }

    private string ResolveStatusText()
    {
        if (IsExcluded)
            return "Ausgeschlossen";

        if (HasReusableConvertedFile || ConvertedSizeAvailable)
            return "Konvertiert";

        if (ProcessingAction.Contains("rippen", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(FilePath) && !SourceSizeAvailable && !ConvertedSizeAvailable))
        {
            return "Noch nicht gerippt";
        }

        if (string.Equals(ProcessingAction, "Prüfen", StringComparison.OrdinalIgnoreCase) && HasBlockingFileWarning)
            return "Fehler";

        return "Bereit";
    }

    private string ResolveFileWarningText()
    {
        if (!string.IsNullOrWhiteSpace(FileWarningText))
            return FileWarningText;

        if (Warning.Contains("Keine gültige Audiodatei", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Codec, "Ungültig", StringComparison.OrdinalIgnoreCase) ||
            AudioValidationPassed == false)
        {
            return "Keine gültige Audiodatei";
        }

        if (Warning.Contains("Quelldatei fehlt", StringComparison.OrdinalIgnoreCase))
            return "Quelldatei fehlt";

        if (Warning.Contains("Quelldatei ist leer", StringComparison.OrdinalIgnoreCase) ||
            Warning.Contains("Quelldatei leer", StringComparison.OrdinalIgnoreCase))
        {
            return "Quelldatei leer";
        }

        if (Warning.Contains("AAC-Datei ist leer", StringComparison.OrdinalIgnoreCase) ||
            Warning.Contains("AAC-Datei leer", StringComparison.OrdinalIgnoreCase))
        {
            return "AAC-Datei leer";
        }

        if (Warning.Contains("Nicht dekodierbar", StringComparison.OrdinalIgnoreCase))
            return "Nicht dekodierbar";

        return string.Empty;
    }
}
