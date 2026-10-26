namespace BookStitch.Services;

public static class ResumeProjectSummaryService
{
    public static string Create(int projectCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(projectCount);

        return projectCount switch
        {
            0 => "Keine Projekte im Projektordner",
            1 => "1 Projekt gefunden",
            _ => $"{projectCount} Projekte gefunden"
        };
    }
}