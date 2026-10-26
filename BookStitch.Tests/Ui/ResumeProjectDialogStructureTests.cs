using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ResumeProjectDialogStructureTests
{
    [Fact]
    public void Resume_configuration_closes_before_either_continuation_dispatch_and_keeps_the_metadata_gate()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var continuation = SourceContract.ExtractMember(source, "private async Task ContinuePausedPipelineAsync()");
        SourceContract.AssertInOrder(continuation, "_resumeDiscPreview.MarkProcessingStarted();", "CreateContinuationStart()",
            "ApplyPipelineTransition(continuationStart)", "await RunPreparedAudioDiscRipAsync()");
        var setup = SourceContract.ExtractMember(source, "private async Task ShowCurrentProjectSetupDialogAsync()");
        SourceContract.AssertInOrder(setup, "_resumeDiscPreview.MarkProcessingStarted();", "NotifyExportUiStateChanged()",
            "CreateContinuationStart()", "await ContinuePausedMp3DiscProjectAsync()");
        Assert.Contains("_isMetadataEditingAvailable && ExportUiState.CanChangeBookMetadata", source);
    }

    [Fact]
    public void Projects_button_opens_the_dialog_without_a_synchronous_duplicate_scan()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var opening = SourceContract.ExtractMember(source, "private async void OpenResumeProjects_Click");
        Assert.DoesNotContain("RefreshResumeProjects(", opening, StringComparison.Ordinal);
        Assert.Contains("ModalDialogService.ShowDialog(dialog, this)", opening, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", opening, StringComparison.Ordinal);
        var refresh = SingleElement(ResumeDialog(), "Button", "x:Name", "RefreshProjectsButton");
        Assert.Equal("{Binding CanRefreshProjects}", Attribute(refresh, "IsEnabled"));
    }

    [Fact]
    public void Resumed_disc_selection_precedes_continuation_state_transition_and_uses_shared_nickname_formatter()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var setup = SourceContract.ExtractMember(source, "private async Task ShowCurrentProjectSetupDialogAsync()");
        SourceContract.AssertInOrder(setup, "if (!await EnsureResumeDiscPreviewAsync())", "return;",
            "CreateProjectSetupDialog(", "CreateContinuationStart()");
        var continuation = SourceContract.ExtractMember(source, "private async Task ContinuePausedPipelineAsync()");
        SourceContract.AssertInOrder(continuation, "if (_resumeDiscSourceSelectionRequired)",
            "await ShowCurrentProjectSetupDialogAsync();", "return;", "CreateContinuationStart()");
        var nextAudio = SourceContract.ExtractMember(source, "private async Task<AudioDiscPollingResult?> TrySelectNextAudioDiscFromDriveRoundAsync(");
        Assert.DoesNotContain("Take(1)", nextAudio);
        Assert.Contains("startWithSelectedDrive", nextAudio);
        var label = SourceContract.ExtractMember(source, "private string CreateDiscDriveDisplayName(");
        Assert.Contains("DiscDriveSelectionPresentationService.FormatStatusDriveLabel", label);
    }

    [Fact]
    public void Source_selection_only_succeeds_after_validated_preview_and_does_not_write_audio_drive_to_manifest()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var preview = SourceContract.ExtractMember(source, "private async Task<bool> EnsureResumeDiscPreviewAsync()");
        SourceContract.AssertInOrder(preview, "if (!_resumeDiscSourceSelectionRequired)", "return true;",
            "ShowDiscSourceSelectionDialog()", "if (string.IsNullOrWhiteSpace(selected))", "return false;",
            "WaitForNextAudioDiscAsync(", "if (prepared is null) return false;",
            "CreateTrackPreview(audioManifest, prepared)", "AnalyzeDiscForImport(",
            "IsSupportedDataDisc", "AppendDiscPreviewTracks(", "_resumeDiscSourceSelectionRequired = false;");
        Assert.DoesNotContain("SourceDriveRoot =", preview);
        Assert.DoesNotContain("CreateContinuationStart", preview);
        Assert.DoesNotContain("SaveCurrentAudioDiscProjectSnapshot", preview);
        var acceptance = SourceContract.ExtractMember(source, "private AudioDiscProjectManifestDisc AcceptOrPreviewAudioDisc(");
        SourceContract.AssertInOrder(acceptance, "if (previewOnly)", "BuildDiscEntry(", "return AddAcceptedAudioDiscFromPollingResult(");
    }

    [Fact]
    public void Actual_disc_acceptance_checks_preview_identity_and_retains_edits_before_commit()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var audio = SourceContract.ExtractMember(source, "private AudioDiscProjectManifestDisc AddAcceptedAudioDiscFromPollingResult(");
        SourceContract.AssertInOrder(audio, "MatchesAudio(", "DiscardChangedResumeDiscPreview(",
            "PersistTrackListState();", "AudioDiscProjectService.AddDisc(", "preparedDisc: _resumeDiscPreview.AudioDisc",
            "DiscResumePreviewState.AppendAudioTracks(");
        var mp3 = SourceContract.ExtractMember(source, "private async Task<bool> ImportMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(mp3, "if (waitOutcome == DiscWaitDialogOutcome.ImportCompleted)",
            "DiscardChangedResumeDiscPreview(discNumber);", "CompleteDiscImportEarly(projectManifest)");
        SourceContract.AssertInOrder(mp3, "onReadyToCopy: precheck =>", "MatchesMp3(",
            "DiscardChangedResumeDiscPreview(", "PersistTrackListState();", "ApplyPlannedSourceIds(");
        var wait = SourceContract.ExtractMember(source, "private async Task<AudioDiscProjectManifestDisc?> WaitForNextAudioDiscAsync(");
        SourceContract.AssertInOrder(wait, "if (accepted == DiscWaitDialogOutcome.ImportCompleted)",
            "if (previewOnly) return null;", "DiscardChangedResumeDiscPreview(discNumber);", "CompleteDiscImportEarly(manifest)");
        var rip = SourceContract.ExtractMember(source, "private async Task RunPreparedAudioDiscRipAsync(");
        Assert.Contains("selectedSource: discNumber == firstRequiredDiscNumber ? selectedStartDrive : null", rip);
        foreach (var signature in new[] { "private void ResetLoadedResumeProjectState()",
            "private void ResetMetadataForNewDiscProject()", "private void ResetProjectSourceModeStateForNewSource()" })
            Assert.Contains("_resumeDiscPreview.Clear();", SourceContract.ExtractMember(source, signature));
        Assert.Contains("_resumeDiscPreview.Clear();", SourceContract.ExtractMember(source, "private void CompleteWindowCloseCleanup()"));
    }

    [Fact]
    public void Project_details_scroll_beneath_an_independent_shared_fade_header()
    {
        var document = ResumeDialog();
        Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attribute(document.Root!, "Style"));
        Assert.Equal("700", Attribute(document.Root!, "MinWidth"));
        Assert.Equal("500", Attribute(document.Root!, "MinHeight"));
        var windowHeader = SingleElement(document, "LargeResizableWindowHeader", "CloseRequested", "Close_Click");
        Assert.Contains(windowHeader.Descendants(), element => Attribute(element, "Style") == "{StaticResource LargeResizableWindowHeaderTitleTextStyle}");
        Assert.DoesNotContain(document.Root!.Elements(), element => IsElement(element, "Border") && Attribute(element, "CornerRadius") == "8");

        var panel = SingleElement(document, "Grid", "x:Name", "ProjectInfoPanel");
        var scrollViewer = SingleElement(panel, "ScrollViewer", "x:Name", "ProjectInfoScrollViewer");
        var header = SingleElement(panel, "FadeHeader", "x:Name", "ProjectInfoHeaderOverlay");

        Assert.Same(panel, scrollViewer.Parent);
        Assert.Same(panel, header.Parent);
        Assert.Equal("Auto", Attribute(scrollViewer, "VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", Attribute(scrollViewer, "HorizontalScrollBarVisibility"));
        Assert.Equal("False", Attribute(scrollViewer, "CanContentScroll"));
        Assert.Equal("True", Attribute(scrollViewer, "SmoothMouseWheelScrollBehavior.IsEnabled"));
        Assert.Equal("{StaticResource AppearanceFadeHeaderStyle}", Attribute(header, "Style"));
        Assert.Equal("1", Attribute(header, "Panel.ZIndex"));

        var scrollBarStyle = scrollViewer.Descendants().Single(element =>
            IsElement(element, "Style") && Attribute(element, "TargetType") == "{x:Type ScrollBar}");
        Assert.Equal("{StaticResource AppearanceRoundedScrollBarStyle}", Attribute(scrollBarStyle, "BasedOn"));
    }

    [Fact]
    public void Resume_project_selector_and_header_use_the_central_project_type_templates()
    {
        var app = RepositorySource.LoadXaml(["BookStitch", "App.xaml"]);
        Assert.Contains(app.Descendants(), element =>
            IsElement(element, "ResourceDictionary") &&
            Attribute(element, "Source") == "/Themes/ProjectTypeVisuals.xaml");

        var dialog = ResumeDialog();
        var selector = SingleElement(dialog, "ComboBox", "x:Name", "ProjectsComboBox");
        Assert.Equal("{StaticResource ProjectTypeListItemTemplate}", Attribute(selector, "ItemTemplate"));

        var header = SingleElement(dialog, "FadeHeader", "x:Name", "ProjectInfoHeaderOverlay");
        var icon = header.Descendants().Single(element => IsElement(element, "ContentControl"));
        Assert.Equal("{Binding SelectedProject}", Attribute(icon, "Content"));
        Assert.Equal("{StaticResource ProjectTypeIconTemplate}", Attribute(icon, "ContentTemplate"));
    }

    [Theory]
    [InlineData("FolderProject", "ProjectTypeFolderIconTemplate", "ProjectTypeFolderGlyphTemplate")]
    [InlineData("Mp3DiscProject", "ProjectTypeMp3SummaryIconTemplate", "ProjectTypeMp3DiscGlyphTemplate")]
    [InlineData("Mp3Disc", "ProjectTypeMp3SummaryIconTemplate", "ProjectTypeMp3DiscGlyphTemplate")]
    [InlineData("AudioCdProject", "ProjectTypeAudioSummaryIconTemplate", "ProjectTypeAudioDiscGlyphTemplate")]
    public void Project_type_templates_map_supported_manifest_types(
        string projectType,
        string expectedIconTemplate,
        string expectedGlyphTemplate)
    {
        var visuals = RepositorySource.LoadXaml(["BookStitch", "Themes", "ProjectTypeVisuals.xaml"]);

        Assert.Equal(
            $"{{StaticResource {expectedIconTemplate}}}",
            TriggerTemplate(visuals, "ProjectTypeIconTemplate", projectType));
        Assert.Equal(
            $"{{StaticResource {expectedGlyphTemplate}}}",
            TriggerTemplate(visuals, "ProjectTypeGlyphTemplate", projectType));
    }

    private static string TriggerTemplate(XDocument document, string templateKey, string projectType)
    {
        var template = SingleElement(document, "DataTemplate", "x:Key", templateKey);
        var trigger = template.Descendants().Single(element =>
            IsElement(element, "DataTrigger") && Attribute(element, "Value") == projectType);
        var setter = trigger.Elements().Single(element =>
            IsElement(element, "Setter") && Attribute(element, "Property") == "ContentTemplate");
        return Attribute(setter, "Value");
    }

    private static XDocument ResumeDialog() =>
        RepositorySource.LoadXaml(["BookStitch", "Dialog", "ResumeProjectDialog.xaml"]);

    private static XElement SingleElement(
        XContainer container,
        string elementName,
        string attributeName,
        string attributeValue) =>
        container.Descendants().Single(element =>
            IsElement(element, elementName) && Attribute(element, attributeName) == attributeValue);

    private static bool IsElement(XElement element, string name) => element.Name.LocalName == name;

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name.Split(':')[^1])?.Value ?? "";
}
