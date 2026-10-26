using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class WindowMetricsPresentationServiceTests
{
    [Fact]
    public void Update_AlwaysShowsMainAndRemembersLastSecondaryWhileDeveloperToolsIsActive()
    {
        var service = new WindowMetricsPresentationService();
        var main = Window("Hauptfenster", isMain: true);
        var settings = Window("Einstellungen", isActive: true);

        var first = service.Update([main, settings]);
        var whileReading = service.Update([main, Window("Developer Tools", isDeveloperTools: true, isActive: true)]);

        Assert.Contains("Hauptfenster", first.MainWindowText, StringComparison.Ordinal);
        Assert.Contains("Einstellungen", first.SecondaryWindowText, StringComparison.Ordinal);
        Assert.Contains("Einstellungen", whileReading.SecondaryWindowText, StringComparison.Ordinal);
        Assert.DoesNotContain("Developer Tools", whileReading.SecondaryWindowText, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_ClearsSecondaryWhenMainWindowBecomesActive()
    {
        var service = new WindowMetricsPresentationService();
        service.Update([Window("Main", isMain: true), Window("Settings", isActive: true)]);

        var result = service.Update([Window("Main", isMain: true, isActive: true)]);

        Assert.Equal("–", result.SecondaryWindowText);
    }

    [Fact]
    public void FormatWindow_UsesInvariantDipValuesAndReadableInfinity()
    {
        var text = WindowMetricsPresentationService.FormatWindow(new WindowMetricsCandidate(
            "Dialog", 1020.5, 650, 820, 440, double.PositiveInfinity, double.PositiveInfinity,
            false, false, false));

        Assert.Contains("Aktuell: 1020.5 × 650 DIP", text, StringComparison.Ordinal);
        Assert.Contains("Min: 820 × 440 DIP", text, StringComparison.Ordinal);
        Assert.Contains("Max: ∞ × ∞ DIP", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1020,5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatWindow_IncludesTechnicalTypeNameWithoutChangingInfinityFormatting()
    {
        var text = WindowMetricsPresentationService.FormatWindow(new WindowMetricsCandidate(
            "Einstellungen", 1030, 600, 1030, 600, double.PositiveInfinity, double.PositiveInfinity,
            false, false, true, "SettingsWindow"));

        Assert.Contains("Technisch: SettingsWindow", text, StringComparison.Ordinal);
        Assert.Contains("Max: ∞ × ∞ DIP", text, StringComparison.Ordinal);
    }

    private static WindowMetricsCandidate Window(
        string name,
        bool isMain = false,
        bool isDeveloperTools = false,
        bool isActive = false) =>
        new(name, 1000, 700, 800, 500, double.PositiveInfinity, double.PositiveInfinity,
            isMain, isDeveloperTools, isActive);
}
