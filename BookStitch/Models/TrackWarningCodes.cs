namespace BookStitch.Models;

[Flags]
public enum TrackWarningCodes
{
    None = 0,
    InvalidAudio = 1 << 0,
    MissingSource = 1 << 1,
    EmptySource = 1 << 2,
    EmptyAac = 1 << 3,
    Undecodable = 1 << 4,
    DuplicateChapterName = 1 << 5,
    MissingChapter = 1 << 6,
    DuplicateChapter = 1 << 7,
    CheckOrder = 1 << 8,
    MissingTagTitle = 1 << 9, // Legacy only; no longer generated or presented as a warning.
    UnreadableTags = 1 << 10
}

// Only imported and older free-text warnings enter here.
public static class LegacyTrackWarningReader
{
    public static bool IsSingleInvalidAudioWarning(string? text) =>
        string.Equals(text, "Keine gültige Audiodatei erkannt.", StringComparison.Ordinal);

    public static TrackWarningCodes Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return TrackWarningCodes.None;
        var codes = TrackWarningCodes.None;
        if (Has(text, "Keine gültige Audiodatei")) codes |= TrackWarningCodes.InvalidAudio;
        if (Has(text, "Quelldatei fehlt")) codes |= TrackWarningCodes.MissingSource;
        if (Has(text, "Quelldatei ist leer") || Has(text, "Quelldatei leer")) codes |= TrackWarningCodes.EmptySource;
        if (Has(text, "AAC-Datei ist leer") || Has(text, "AAC-Datei leer")) codes |= TrackWarningCodes.EmptyAac;
        if (Has(text, "Nicht dekodierbar")) codes |= TrackWarningCodes.Undecodable;
        if (Has(text, "Doppelter Kapitelname")) codes |= TrackWarningCodes.DuplicateChapterName;
        if (Has(text, "Kapitel fehlt")) codes |= TrackWarningCodes.MissingChapter;
        if (Has(text, "Kapitel doppelt")) codes |= TrackWarningCodes.DuplicateChapter;
        if (Has(text, "Sortierung prüfen")) codes |= TrackWarningCodes.CheckOrder;
        if (Has(text, "Kein Tag-Titel")) codes |= TrackWarningCodes.MissingTagTitle;
        if (Has(text, "Tags konnten nicht gelesen werden")) codes |= TrackWarningCodes.UnreadableTags;
        return codes;
    }

    private static bool Has(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
