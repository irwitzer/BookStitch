using BookStitch.Models;

namespace BookStitch.Services;

public static class ChapterSettingsService
{
    public const ChapterNumberingStyle DefaultNumberingStyle = ChapterNumberingStyle.ZeroPadded;
    public const string ModeAuto = "Auto";
    public const string ModeCustom = "Custom";
    public const string ModeOriginal = "Original";
    public const string ModeNone = "None";
    public const string ModeChapterMaster = "ChapterMaster";
    public const string PreviewDataSourceSample = "Sample";
    public const string PreviewDataSourceCurrentProject = "CurrentProject";

    public static ChapterSettings Normalize(ChapterSettings? settings, bool legacyUseLeadingZeros)
    {
        var result = settings ?? new ChapterSettings
        {
            NumberingStyle = FromUseLeadingZeros(legacyUseLeadingZeros).ToString()
        };

        result.Mode = NormalizeMode(result.Mode);
        result.NumberingStyle = NormalizeNumberingStyle(result.NumberingStyle);
        result.PreviewDataSource = NormalizePreviewDataSource(result.PreviewDataSource);
        result.Slot1 = NormalizeSlot(result.Slot1);
        result.Slot2 = NormalizeSlot(result.Slot2);
        result.Slot3 = NormalizeSlot(result.Slot3);
        result.Separator1 = NormalizeSeparator(result.Separator1);
        result.Separator2 = NormalizeSeparator(result.Separator2);

        if (string.Equals(result.NumberingStyle, ChapterNumberingStyle.None.ToString(), StringComparison.Ordinal))
        {
            if (string.Equals(result.Slot1, "Number", StringComparison.Ordinal))
                result.Slot1 = "Empty";
            if (string.Equals(result.Slot2, "Number", StringComparison.Ordinal))
                result.Slot2 = "Empty";
            if (string.Equals(result.Slot3, "Number", StringComparison.Ordinal))
                result.Slot3 = "Empty";
        }

        return result;
    }

    public static ChapterNumberingStyle GetNumberingStyle(ChapterSettings? settings, bool legacyUseLeadingZeros)
    {
        var normalized = Normalize(settings, legacyUseLeadingZeros);
        return Enum.TryParse<ChapterNumberingStyle>(normalized.NumberingStyle, out var style)
            ? style
            : FromUseLeadingZeros(legacyUseLeadingZeros);
    }

    public static ChapterNumberingStyle FromUseLeadingZeros(bool useLeadingZeros) =>
        useLeadingZeros ? ChapterNumberingStyle.ZeroPadded : ChapterNumberingStyle.Plain;

    public static string NormalizeMode(string? value)
    {
        if (string.Equals(value, ModeCustom, StringComparison.OrdinalIgnoreCase))
            return ModeCustom;
        if (string.Equals(value, ModeOriginal, StringComparison.OrdinalIgnoreCase))
            return ModeOriginal;
        if (string.Equals(value, ModeNone, StringComparison.OrdinalIgnoreCase))
            return ModeNone;
        if (string.Equals(value, ModeChapterMaster, StringComparison.OrdinalIgnoreCase))
            return ModeChapterMaster;

        return ModeAuto;
    }

    public static bool UsesAutomaticEngine(string? mode) =>
        string.Equals(mode, ModeAuto, StringComparison.Ordinal) ||
        string.Equals(mode, ModeChapterMaster, StringComparison.Ordinal);

    public static string NormalizeNumberingStyle(string? value)
    {
        if (Enum.TryParse<ChapterNumberingStyle>(value, ignoreCase: true, out var style) && Enum.IsDefined(style))
            return style.ToString();

        return DefaultNumberingStyle.ToString();
    }

    public static string NormalizeSlot(string? value)
    {
        return value switch
        {
            "Empty" or "Number" or "BookTitle" or "Album" or "OriginalTagTitle" or
                "CleanTagTitle" or "CleanFileName" or "OriginalFileName" => value,
            _ => "Empty"
        };
    }

    public static string NormalizePreviewDataSource(string? value)
    {
        return string.Equals(value, PreviewDataSourceCurrentProject, StringComparison.OrdinalIgnoreCase)
            ? PreviewDataSourceCurrentProject
            : PreviewDataSourceSample;
    }

    public static string NormalizeSeparator(string? value)
    {
        return value switch
        {
            "Space" or "Dash" or "EnDash" or "Dot" or "Colon" or "Pipe" or "None" => value,
            _ => "Dash"
        };
    }
}
