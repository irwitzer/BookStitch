using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalizationPresentationRegressionTests
{
    [Fact]
    public void CorePresentationOwnersUseTheCurrentUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");

            var track = new TrackInfo
            {
                FileName = "book.mp3",
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo(),
                    new TrackEmbeddedChapterInfo { Title = "Dos" }
                ]
            };
            Assert.Equal("2 capítulos incrustados", track.DisplayChapterProposal);
            Assert.Equal("Mostrar capítulos", track.DisplayChapterDetailsToggleText);
            Assert.Contains("book.mp3", track.DisplayChapterDetailsAutomationName);
            Assert.Equal("Capítulo 1", track.EmbeddedChapterRows[0].Title);

            var counts = ExportPreviewCountPresentationService.Create(2, 5);
            Assert.Equal("Pistas: 2", counts.TrackSummary);
            Assert.Equal("Capítulos: 5 planificados", counts.ChapterSummary);
            Assert.Equal("Duración total: 01:00:05 (2/3 conocidas)",
                ExportDurationPresentationService.Create(3, [TimeSpan.FromHours(1), TimeSpan.FromSeconds(5)]));

            var shutdown = new ApplicationShutdownService().CreatePrompt(ApplicationActivity.AudioDiscProcessing);
            Assert.Contains("CD de audio", shutdown.Heading);
            Assert.DoesNotContain("Verarbeitung", shutdown.ProgressText);

            Assert.Equal("… y 2 entradas más", AppDialogService.LimitDetails(["1", "2", "3", "4"], 2)[2]);

            var project = new ProjectIndexItem { IsSelectableProject = true };
            Assert.Contains("Sin título", project.ListDisplayName);
            Assert.Contains("Listo para exportar", project.ListDisplayName);
            Assert.Contains("Fecha desconocida", project.ListDisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void SourcePresentationUsesStructuralStateAndKeepsTheOpenPathTechnical()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var empty = MainWindowSourcePresentationService.Create(1, null, null, null, false);
            Assert.Equal("Aucun dossier sélectionné.", empty.DisplayText);
            Assert.Empty(empty.OpenPath);

            var drive = MainWindowSourcePresentationService.Create(1, @"G:\", null, null, true);
            Assert.Equal("Lecteur CD G", drive.DisplayText);
            Assert.Equal(@"G:\", drive.OpenPath);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
