using System.Globalization;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class WorkflowDialogLocalizationTests
{
    [Fact]
    public void Startup_attention_dialog_cannot_own_main_window_or_implicitly_shutdown_the_first_instance()
    {
        var app = RepositorySource.Read(["BookStitch", "App.xaml.cs"]);
        var dialog = SourceContract.ExtractMember(app, "private static void ShowStandaloneMessage(");
        SourceContract.AssertInOrder(dialog, "previousMode =", "previousMainWindow =",
            "ShutdownMode.OnExplicitShutdown", "dialog.ShowDialog()", "finally",
            "application.MainWindow = previousMainWindow", "application.ShutdownMode = previousMode");
        Assert.Contains("ProjectAttempt.Recovery.Message", app);
        var startup = SourceContract.ExtractMember(app, "protected override void OnStartup(");
        SourceContract.AssertInOrder(startup, "ReadUiLanguageAtStartup()", "UiLanguageService.Resolve",
            "LocalizationText.Get(\"Startup.AlreadyRunning.Message\", culture)", "Shutdown()");
    }

    [Fact]
    public void DriveCircle_diagnostic_events_use_the_product_category()
    {
        var main = RepositorySource.Read(["BookStitch","MainWindow.xaml.cs"]);
        foreach(var member in new[]{
            "private async Task<Mp3DriveRoundWaitResult> CheckMp3DriveRoundForWaitDialogAsync",
            "private async Task<bool> TrySelectNextMp3DiscFromDriveRoundAsync",
            "private void WriteDriveRoundSkip",
            "private async Task<AudioDriveRoundWaitResult> CheckAudioDriveRoundForWaitDialogAsync",
            "private async Task<AudioDiscPollingResult?> TrySelectNextAudioDiscFromDriveRoundAsync"})
        {
            var source = SourceContract.ExtractMember(main,member);
            Assert.Contains("\"DRIVECIRCLE\"",source);
            Assert.DoesNotContain("\"DRIVE ROUND\"",source);
        }
    }

    [Fact]
    public void Drive_round_and_review_texts_resolve_from_a_non_german_catalog()
    {
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Contains("unidad", LocalizationText.Format("Disc.DriveRound.Unreadable.Dialog", "D:"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CD", LocalizationText.Format("Disc.DriveRound.NoDrive.Dialog", "CD MP3", 2, 3));
            Assert.DoesNotContain("Laufwerk", LocalizationText.Format("Disc.DriveRound.NoDrive.Dialog", "CD MP3", 2, 3));
            Assert.Equal("Conversión completada", LocalizationText.Get("Workflow.Review.Heading"));
            Assert.Equal("Unir", LocalizationText.Get("Workflow.Review.MergeAction"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Add_more_discs_code_and_review_dialog_use_resource_keys()
    {
        var addMore = RepositorySource.Read(["BookStitch", "Dialog", "AddMoreDiscsDialog.xaml.cs"]);
        Assert.Contains("Disc.AddMore.Description", addMore);
        Assert.DoesNotContain("LocalizationText.Get(\"Disc.AddMore.SelectDrive\")", addMore);
        Assert.Contains("LocalizationText.Format(\"Disc.AddMore.TotalRange\"", addMore);
        Assert.DoesNotContain("Bitte ein verfügbares CD-Laufwerk", addMore);

        var mainWindow = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var review = SourceContract.ExtractBetween(mainWindow,
            "private void PauseBeforeMergeForManualReview", "private void WriteFinalTags");
        Assert.Contains("LocalizationText.Get(\"Workflow.Review.Title\")", review);
        Assert.Contains("LocalizationText.Get(\"Workflow.Review.Heading\")", review);
        Assert.Contains("LocalizationText.Get(\"Workflow.Review.Message\")", review);
        Assert.DoesNotContain("Konvertierung abgeschlossen", review);

        var mp3DriveRound = SourceContract.ExtractBetween(mainWindow,
            "private async Task<DiscWaitDialogOutcome> WaitForNextMp3DiscWithDriveRoundDialogAsync",
            "private async Task<bool> TrySelectNextMp3DiscFromDriveRoundAsync");
        var audioDriveRound = SourceContract.ExtractBetween(mainWindow,
            "private async Task<AudioDiscProjectManifestDisc?> WaitForNextAudioDiscAsync",
            "private async Task<AudioDiscPollingResult?> TrySelectNextAudioDiscFromDriveRoundAsync");
        foreach (var path in new[] { mp3DriveRound, audioDriveRound })
        {
            Assert.Contains("LocalizationText.Format(\"Disc.DriveRound.InsertInstruction\"", path);
            Assert.Contains("LocalizationText.Format(\"Disc.DriveRound.NoDrive.Dialog\"", path);
            Assert.Contains("LocalizationText.Format(\"Disc.DriveRound.Unreadable.Dialog\"", path);
            Assert.DoesNotContain("Laufwerksrunde prüft automatisch", path);
        }
    }

    [Fact]
    public void Boxed_disc_hint_is_plain_text_in_the_dialog_flow()
    {
        var xaml = RepositorySource.LoadXaml(["BookStitch", "Dialog", "BoxedDiscWaitDialog.xaml"]);
        var hint = xaml.Descendants().Single(element =>
            element.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value == "HintTextBlock");
        Assert.Equal("TextBlock", hint.Name.LocalName);
        Assert.Equal("StackPanel", hint.Parent?.Name.LocalName);
    }

    [Fact]
    public void Export_completion_and_output_decisions_use_localized_messages()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("Exportación completada", LocalizationText.Get("MainWindow.Export.Completed.Heading"));
            Assert.Contains("archivo", LocalizationText.Format("MainWindow.Export.Conflict.Message", "C:\\final.m4b", "final-2.m4b"));
            Assert.Contains("escritorio", LocalizationText.Format("MainWindow.Export.Failure.Message", "C:\\final.m4b", "final.m4b"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var mainWindow = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        Assert.Contains("LocalizationText.Get(\"MainWindow.Export.Completed.Title\")", mainWindow);
        Assert.Contains("LocalizationText.Format(\"MainWindow.Export.Conflict.Message\"", mainWindow);
        Assert.Contains("LocalizationText.Format(\"MainWindow.Export.Failure.Message\"", mainWindow);
        Assert.Contains("trackCount == 1 ? \"MainWindow.Summary.AudioFiles.One\"", mainWindow);
    }

    [Fact]
    public void Main_window_disc_analysis_and_failure_paths_use_the_active_ui_catalog()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("Leyendo el CD…", LocalizationText.Get("MainWindow.DiscAnalysis.Reading"));
            Assert.Contains("lentamente", LocalizationText.Get("MainWindow.DiscAnalysis.Slow"));
            Assert.Equal("Comprobando el CD…", LocalizationText.Get("MainWindow.DiscAnalysis.Checking"));
            Assert.Contains("audio", LocalizationText.Get("MainWindow.DiscAnalysis.AudioReading"));
            Assert.Equal("Error en la preparación local", LocalizationText.Get("MainWindow.LocalPreparation.Failed.Title"));
            Assert.Contains("2", LocalizationText.Format("MainWindow.Setup.Mp3SourceInformation", 2, "00:10:00"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var mainWindow = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var xaml = RepositorySource.Read(["BookStitch", "MainWindow.xaml"]);
        Assert.Contains("Text=\"{Binding DiscSourceAnalysisText}\"", xaml);
        Assert.Contains("LocalizationText.Get(\"MainWindow.DiscAnalysis.Reading\")", mainWindow);
        Assert.Contains("LocalizationText.Get(\"MainWindow.LocalPreparation.Failed.Title\")", mainWindow);
        Assert.Contains("details: new[] { ex.Message }", mainWindow);
    }
}
