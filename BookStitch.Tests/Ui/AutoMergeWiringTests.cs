using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class AutoMergeWiringTests
{
    [Fact]
    public void Mp3_import_checks_export_cache_without_reading_the_disc_import_manifest_as_export_state()
    {
        var import = SourceContract.ExtractMember(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private async Task<bool> ImportMp3DiscProjectAsync(");
        var cachePreparation = SourceContract.ExtractBetween(
            import,
            "var workManifest = _services.Foundation.WorkManifestService.LoadOrCreate(",
            "var importedDiscSignatures =");

        Assert.Contains("ProjectFolderLayout.ResolveExportManifestPath(projectFolder)", cachePreparation);
        Assert.DoesNotContain("ProjectFolderLayout.ResolveWorkManifestPath(projectFolder)", cachePreparation);
    }

    [Fact]
    public void Main_window_uses_one_settings_toggle_with_the_live_two_way_binding()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");
        var toggle = SingleElementContaining(document, "CheckBox", "IsChecked", "MergeAutomaticallyAfterConversion");

        Assert.Equal("{services:LocalizedText Key=UiAutoMerge}", Attr(toggle, "Content"));
        Assert.Equal("{StaticResource SettingsToggleStyle}", Attr(toggle, "Style"));
        Assert.Contains("Mode=TwoWay", Attr(toggle, "IsChecked"), StringComparison.Ordinal);
        Assert.DoesNotContain("AutoMergeLabel_MouseLeftButtonUp", document.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AutoMergeLabel_MouseLeftButtonUp",
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Project_setup_toggle_forwards_changes_to_the_shared_setting()
    {
        var document = LoadXaml("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml");
        var toggle = SingleElement(document, "CheckBox", "x:Name", "AutoMergeCheckBox");
        Assert.Equal("{services:LocalizedText Key=UiAuto}", Attr(toggle, "Content"));
        Assert.Equal("{StaticResource SettingsToggleStyle}", Attr(toggle, "Style"));
        Assert.Equal("AutoMergeCheckBox_Changed", Attr(toggle, "Checked"));
        Assert.Equal("AutoMergeCheckBox_Changed", Attr(toggle, "Unchecked"));

        var handler = SourceContract.ExtractMember(
            LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs"),
            "private void AutoMergeCheckBox_Changed");
        Assert.Contains("_setAutoMerge?.Invoke(AutoMergeCheckBox.IsChecked == true);", handler, StringComparison.Ordinal);

        var dialogFactory = SourceContract.ExtractBetween(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private DiscProjectSetupDialog CreateProjectSetupDialog",
            "private void ApplyProjectSetupDialogInitialWidth");
        Assert.Contains("setAutoMerge: enabled =>", dialogFactory, StringComparison.Ordinal);
        Assert.Contains("MergeAutomaticallyAfterConversion = enabled;", dialogFactory, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_toggle_forwards_changes_to_the_shared_setting_and_persists_them()
    {
        var document = LoadXaml("BookStitch", "Controls", "SettingsGeneralControl.xaml");
        var toggle = SingleElement(document, "CheckBox", "x:Name", "MergeAutomaticallyCheckBox");
        Assert.Equal("{StaticResource SettingsToggleStyle}", Attr(toggle, "Style"));
        Assert.Equal("MergeAutomaticallyCheckBox_Changed", Attr(toggle, "Checked"));
        Assert.Equal("MergeAutomaticallyCheckBox_Changed", Attr(toggle, "Unchecked"));

        var handler = SourceContract.ExtractMember(
            LoadText("BookStitch", "Controls", "SettingsGeneralControl.xaml.cs"),
            "private void MergeAutomaticallyCheckBox_Changed");
        SourceContract.AssertInOrder(
            handler,
            "_setMergeAutomaticallyAfterConversion!(MergeAutomaticallyCheckBox.IsChecked == true);",
            "_settingsManager!.SaveImmediate();");

        var settingsFactory = SourceContract.ExtractBetween(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private void ShowSettingsDialog",
            "private IReadOnlyDictionary<Guid, string>? CreateCurrentResolvedAutoContentsBySourceId");
        Assert.Contains(
            "ApplyMergeAutomaticallyFromSettingsWindow",
            settingsFactory,
            StringComparison.Ordinal);

        var sharedUpdate = SourceContract.ExtractMember(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private void ApplyMergeAutomaticallyFromSettingsWindow");
        SourceContract.AssertInOrder(
            sharedUpdate,
            "_settings.MergeAutomaticallyAfterConversion = enabled;",
            "OnPropertyChanged(nameof(MergeAutomaticallyAfterConversion));");
    }

    [Fact]
    public void Export_workflow_queries_the_live_auto_merge_setting_at_the_merge_boundary()
    {
        var export = SourceContract.ExtractBetween(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private async Task ExportToAacWithWorkFolderAsync",
            "private void ReturnToReviewAfterInterruptedExport");

        Assert.Contains("MergeAutomaticallyAfterConversion = () => _settings.MergeAutomaticallyAfterConversion", export);
        Assert.Contains("ManualMergeRequested: manualMergeRequested", export);
        Assert.DoesNotContain("ShouldPauseBeforeMerge =", export);
        Assert.DoesNotContain("ProjectReviewState.IsWaitingForManualMergeReview", export);
    }

    [Fact]
    public void Adding_sources_invalidates_manual_review_only_after_the_return_state_is_saved()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var mp3 = SourceContract.ExtractBetween(mainWindow,
            "private async void AddProjectSources_Click", "private async Task AddMoreLocalSourcesAsync");
        var local = SourceContract.ExtractBetween(mainWindow,
            "private async Task AddMoreLocalSourcesAsync", "private void RollbackLocalProjectExtension");
        var audio = SourceContract.ExtractBetween(mainWindow,
            "private async Task AddMoreAudioDiscsAsync", "private void ApplyDiscProjectSetup");

        foreach (var extension in new[] { mp3, local, audio })
            SourceContract.AssertInOrder(extension,
                "var returnState = ProjectReviewState.Phase;",
                "_projectReviewState = ProjectReviewState.WithManualMergeReview(false);");

        var localRollback = SourceContract.ExtractBetween(mainWindow,
            "private void RollbackLocalProjectExtension", "private async Task AddMoreAudioDiscsAsync");
        Assert.Contains("ProjectReviewState.WithManualMergeReview(returnState == ProjectPipelineState.ReviewBeforeMerge)", localRollback);
        Assert.Contains("ProjectReviewState.WithManualMergeReview(returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)", mp3);
        Assert.Contains("ProjectReviewState.WithManualMergeReview(returnState is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed)", audio);
    }

    [Fact]
    public void Manual_merge_permission_is_created_only_by_the_explicit_export_command()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var command = SourceContract.ExtractMember(mainWindow, "private async void Export_Click");
        Assert.Contains("var manualMergeRequested =", command);
        Assert.Contains("_manualMergeReviewHasPreparedTracks && !_manualMergeReviewNeedsReconversion", command);
        Assert.Contains("RunLoadedMp3DiscProjectExportAsync(manualMergeRequested)", command);
        Assert.Contains("ProjectManifestTypes.AudioCdProject, manualMergeRequested", command);
        Assert.Contains("RunCurrentExportPlanAsync(manualMergeRequested: manualMergeRequested)", command);
        var export = SourceContract.ExtractMember(mainWindow, "private async Task ExportToAacWithWorkFolderAsync");
        Assert.DoesNotContain("ManualMergeRequested: ProjectReviewState", export);
        Assert.DoesNotContain("continuingFromManual", mainWindow);
        var continuation = SourceContract.ExtractMember(mainWindow, "private async Task ContinuePausedPipelineAsync");
        SourceContract.AssertInOrder(continuation, "var resumeMerge = ProjectReviewState.Phase == ProjectPipelineState.Merging",
            "if (resumeMerge)", "manualMergeRequested: true", "return;", "PausedPipelineContinuationService.CreatePlan(");
    }

    [Fact]
    public void Production_merge_calls_cannot_bypass_the_shared_export_workflow()
    {
        var root = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "BookStitch.csproj"]))!;
        var callers = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Where(path => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path),
                @"\.\s*MergeConvertedTracksAsync\s*\("))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();
        Assert.Equal(["Services/ExportWorkflowService.cs"], callers);
    }

    private static XDocument LoadXaml(params string[] relativePath) =>
        RepositorySource.LoadXaml(relativePath);

    private static string LoadText(params string[] relativePath) =>
        RepositorySource.Read(relativePath);

    private static XElement SingleElement(
        XDocument document,
        string elementName,
        string attributeName,
        string attributeValue) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == elementName &&
            Attr(element, attributeName) == attributeValue);

    private static XElement SingleElementContaining(
        XDocument document,
        string elementName,
        string attributeName,
        string expectedPart) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == elementName &&
            Attr(element, attributeName).Contains(expectedPart, StringComparison.Ordinal));

    private static string Attr(XElement element, string attributeName)
    {
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        return attributeName.StartsWith("x:", StringComparison.Ordinal)
            ? element.Attribute(xamlNamespace + attributeName[2..])?.Value ?? string.Empty
            : element.Attribute(attributeName)?.Value ?? string.Empty;
    }

}
