using System.Globalization;

namespace BookStitch.Models;

public sealed class ProjectIndexItem
{
    public string ProjectFolder { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanResume { get; set; }
    public bool IsSelectableProject { get; set; }
    public bool IsCompletedProject { get; set; }
    public bool IsExpired { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public string SourceFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Album { get; set; } = "";
    public string Narrator { get; set; } = "";
    public string Genre { get; set; } = "";
    public int TotalDiscs { get; set; }
    public int ImportedDiscCount { get; set; }
    public string PrimaryManifestPath { get; set; } = "";

    public string ListDisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(DisplayName) ? "Unbenannt" : DisplayName.Trim();
            var created = FormatListDate(CreatedUtc);
            var state = IsCompletedProject ? "Export abgeschlossen" : "Bereit zur Weiterverarbeitung";
            return ProjectTypePrefix + " | " + name + " | " + created + " | " + state;
        }
    }


    public string ProjectTypePrefix => ProjectType switch
    {
        ProjectManifestTypes.Mp3DiscProject => "MP3-PRJ.",
        "Mp3Disc" => "MP3-PRJ.",
        ProjectManifestTypes.AudioCdProject => "AUDIO-PRJ.",
        ProjectManifestTypes.FolderProject => "ORD-PRJ.",
        _ => "PRJ."
    };

    public override string ToString()
    {
        return ListDisplayName;
    }

    private static string FormatListDate(DateTime value)
    {
        if (value == default)
            return "Datum unbekannt";

        var local = value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    }


}
