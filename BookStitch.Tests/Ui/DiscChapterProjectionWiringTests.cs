using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class DiscChapterProjectionWiringTests
{
    [Fact]
    public void Editable_resume_preview_projects_the_validated_runtime_owner_only_between_producers()
    {
        Assert.Contains("_resumeDiscPreview.DiscNumber is not null && !_resumeDiscSourceSelectionRequired &&",
            MainWindowSource, StringComparison.Ordinal);
        Assert.Contains("!IsDiscImporting && !IsExporting", MainWindowSource, StringComparison.Ordinal);
    }

    private static readonly string MainWindowSource =
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Mp3_refresh_restores_saved_track_state_after_manifest_identity_before_indexing()
    {
        var refresh = SourceContract.ExtractMember(
            MainWindowSource, "private async Task RefreshMp3DiscTrackListAfterDiscImportAsync(");
        SourceContract.AssertInOrder(refresh,
            "prepareScannedTracksBeforeIndexUpdate: tracks =>",
            "Mp3DiscTrackReconciliationService.ApplyManifestDiscOrder(",
            "TrackListStateService.Apply(",
            "tracks.Clear();",
            "tracks.AddRange(ordered);");
    }

    [Fact]
    public void Waiting_mp3_disc_preview_does_not_publish_unaccepted_chapter_identity()
    {
        var import = ImportMp3Discs();
        var waitingPreview = SourceContract.ExtractBetween(
            import,
            "var waitOutcome = await WaitForNextDiscAsync(",
            "StartMp3DiscPerformanceOperation(sourceFolder);");

        Assert.Contains("AppendDiscPreviewTracks(", waitingPreview, StringComparison.Ordinal);
        Assert.Contains("refreshChapterProjection: false", waitingPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("_mp3DiscChapterSourceCatalog.Stage(", waitingPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepted_mp3_disc_stages_identity_and_refreshes_preview_before_copying()
    {
        var callback = SourceContract.ExtractBetween(
            ImportMp3Discs(),
            "onReadyToCopy: precheck =>",
            "preparedDisc: _resumeDiscPreview.Mp3Disc,");

        SourceContract.AssertInOrder(
            callback,
            "_mp3DiscChapterSourceCatalog.Stage(",
            "ApplyPlannedSourceIds(",
            "CreatePreviewTracksFromKnownFiles(",
            "ApplyPlannedSourceIds(",
            "AppendDiscPreviewTracks(",
            "refreshChapterProjection: false",
            "RefreshExistingMp3DiscTrackStateBeforeImport(",
            "UpdateIndexes();",
            "StartActiveMp3PerformancePhase(\"file.copy\")");

        Assert.Contains(
            "Tracks,",
            SourceContract.ExtractBetween(
                callback,
                "_mp3DiscChapterSourceCatalog.Stage(",
                "CreatePreviewTracksFromKnownFiles("),
            StringComparison.Ordinal);
        Assert.Contains(
            "previewTracks,",
            SourceContract.ExtractBetween(
                callback,
                "CreatePreviewTracksFromKnownFiles(",
                "AppendDiscPreviewTracks("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Chapter_auto_request_includes_current_pending_disc_sources()
    {
        var request = SourceContract.ExtractMember(
            MainWindowSource,
            "private ChapterAutoEngineRequest CreateCurrentChapterAutoEngineRequest(");

        Assert.Contains("PendingMp3Discs: BuildCurrentMp3DiscChapterSources()", request, StringComparison.Ordinal);
        Assert.Contains("PendingAudioDiscIdentity:", request, StringComparison.Ordinal);
    }

    [Fact]
    public void Initial_disc_previews_establish_transient_context_before_projecting_tracks()
    {
        var mp3Start = SourceContract.ExtractMember(
            MainWindowSource,
            "private async Task StartMp3DiscProjectAsync(string sourceFolder)");
        SourceContract.AssertInOrder(
            mp3Start,
            "_transientMp3DiscChapterPreview = new Mp3DiscPendingChapterDisc(",
            "NormalizeProjectDiscNumber(",
            "ApplyPlannedSourceIds(",
            "AppendDiscPreviewTracks(");

        var audioStart = SourceContract.ExtractMember(
            MainWindowSource,
            "private async Task<bool> TryShowAudioDiscContentsAsync(string sourceFolder)");
        SourceContract.AssertInOrder(
            audioStart,
            "_pendingAudioDisc = result.Disc;",
            "UpdateIndexes();");
    }

    [Fact]
    public void Transient_disc_previews_override_the_folder_project_type()
    {
        var resolve = SourceContract.ExtractMember(
            MainWindowSource,
            "private string ResolveCurrentChapterProjectType()");

        SourceContract.AssertInOrder(
            resolve,
            "_activeAudioDiscManifest is not null || _pendingAudioDisc is not null || LoadedProjectState.IsAudioDisc",
            "return ProjectManifestTypes.AudioCdProject;",
            "_activeMp3DiscManifest is not null || _transientMp3DiscChapterPreview is not null || LoadedProjectState.IsMp3Disc",
            "return ProjectManifestTypes.Mp3DiscProject;",
            "return ProjectManifestTypes.FolderProject;");
    }

    [Fact]
    public void New_disc_session_clears_stale_folder_identity()
    {
        var reset = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ResetMetadataForNewDiscProject()");

        Assert.Contains("_currentFolderPath = \"\";", reset, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanned_tracks_are_prepared_before_entering_the_live_collection()
    {
        var loadFolder = SourceContract.ExtractMember(MainWindowSource, "private async Task LoadFolderAsync(");

        SourceContract.AssertInOrder(
            loadFolder,
            "prepareScannedTracksBeforeIndexUpdate?.Invoke(tracks);",
            "Tracks.ReplaceAll(tracks);",
            "UpdateIndexes();");
    }

    [Theory]
    [InlineData(
        "private async Task RefreshMp3DiscTrackListAfterDiscImportAsync(",
        "Mp3DiscTrackReconciliationService.ApplyManifestDiscOrder(")]
    [InlineData(
        "private async Task RunPreparedAudioDiscRipAsync(",
        "AudioDiscProjectService.ApplyManifestOrderAndMetadataToRippedTracks(")]
    public void Disc_refreshes_supply_manifest_reconciliation_before_indexing(
        string workflowMember,
        string reconciliationCall)
    {
        var workflow = SourceContract.ExtractMember(MainWindowSource, workflowMember);
        SourceContract.AssertInOrder(
            workflow,
            "prepareScannedTracksBeforeIndexUpdate: tracks =>",
            reconciliationCall);
    }

    private static string ImportMp3Discs() =>
        SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
}
