using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class CanonicalMetadataSessionWiringTests
{
    private static readonly string MainWindowSource = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Bulk_reset_uses_one_refresh_notification_without_persistence_or_index_work()
    {
        var subscriber = SourceContract.ExtractMember(MainWindowSource, "private void Tracks_CollectionChanged(");
        Assert.Equal(1, subscriber.Split("_trackStateUpdateQueueService.RequestRefresh();").Length - 1);
        Assert.DoesNotContain("NewItems", subscriber);
        Assert.DoesNotContain("PersistTrackListState", subscriber);
        Assert.DoesNotContain("UpdateIndexes", subscriber);
        var reorder = SourceContract.ExtractMember(MainWindowSource, "private void ReplaceTrackList(");
        SourceContract.AssertInOrder(reorder, "Tracks.Reorder(newOrder)", "UpdateIndexes();", "PersistTrackListState();");
        Assert.DoesNotContain("RequestRefresh", reorder);
        Assert.DoesNotContain("Tracks.ReplaceAll", reorder);
        Assert.Contains("if (_isReorderingTracks) return;", subscriber);
    }

    [Fact]
    public void Resume_opens_or_bootstraps_the_session_before_source_aggregation()
    {
        var resume = SourceContract.ExtractMember(
            MainWindowSource,
            "private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)");

        SourceContract.AssertInOrder(
            resume,
            "_projectMetadataSession.Discard();",
            "_projectMetadata.Clear(\"\");",
            "_projectMetadataSession.OpenExisting(plan.ProjectFolder);",
            "_projectMetadata.ApplyCanonicalMetadata(canonicalMetadata);",
            "ApplySourceMetadataAggregation(");
        SourceContract.AssertInOrder(
            resume,
            "_projectMetadataSession.BeginNew(fallbackMetadata);",
            "_projectMetadataSession.AttachNewProject(plan.ProjectFolder);");
    }

    [Fact]
    public void Resume_replaces_workspace_state_inside_the_transition_barrier()
    {
        var resume = SourceContract.ExtractMember(
            MainWindowSource,
            "private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)");

        SourceContract.AssertInOrder(
            resume,
            "BeginWorkspaceTransition();",
            "_activeAudioDiscManifest = loadResult.AudioDiscManifest;",
            "_projectMetadata.Clear(\"\");",
            "Tracks.ReplaceAll(loadResult.Tracks)",
            "finally",
            "EndWorkspaceTransition();",
            "MeasureResumePhase(\"resume.update-indexes.initial\", UpdateIndexes)");
    }

    [Theory]
    [InlineData("private void RefreshActiveAudioDiscPreviewMetadata()", "if (_isWorkspaceTransitioning)")]
    [InlineData("private void RefreshActiveMp3DiscPreviewMetadata(string? previousTitle)", "if (_isWorkspaceTransitioning)")]
    [InlineData("private void PersistTrackListState()", "if (_isWorkspaceTransitioning)")]
    [InlineData("private void UpdateIndexes()", "if (_isWorkspaceTransitioning)")]
    [InlineData("private void QueueChapterMetadataRefreshIfNeeded()", "if (_isWorkspaceTransitioning || _isLoadingSettings")]
    [InlineData("private void BeginWorkspaceTransition()", "_chapterMetadataRefreshTimer.Stop();")]
    public void Workspace_transition_suppresses_reactive_persistence_and_preview_work(
        string memberSignature,
        string expectedGuard)
    {
        var member = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        Assert.Contains(expectedGuard, member, StringComparison.Ordinal);
    }

    [Fact]
    public void New_source_begins_a_live_session_before_locked_preferences_are_applied()
    {
        var reset = SourceContract.ExtractMember(MainWindowSource, "private void ResetMetadataForNewDiscProject()");

        SourceContract.AssertInOrder(
            reset,
            "_projectMetadataSession.Discard();",
            "_projectMetadata.Clear(\"\");",
            "_projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);",
            "_projectMetadataSession.BeginNew(canonicalMetadata);",
            "ApplyLockedGenrePreferencesToMetadataState();");
    }

    [Fact]
    public void New_project_attachment_requires_the_live_metadata_instance()
    {
        var attach = SourceContract.ExtractMember(
            MainWindowSource,
            "private void AttachCanonicalMetadataSessionToNewProject(string projectFolder)");

        SourceContract.AssertInOrder(
            attach,
            "_projectMetadata.EnsureCanonicalMetadata(MetadataOrigin.None);",
            "_projectMetadataSession.BeginNew(metadata);",
            "ReferenceEquals(_projectMetadataSession.Current, metadata)",
            "_projectMetadataSession.AttachNewProject(projectFolder);");
    }

    [Fact]
    public void Audio_disc_manifest_is_saved_before_metadata_attachment_can_create_a_stub()
    {
        var preparation = SourceContract.ExtractBetween(
            MainWindowSource,
            "var projectFolder = _services.Foundation.AudioDiscProjectService.CreateProjectFolder(",
            "if (projectPrepared && !startProject)");

        SourceContract.AssertInOrder(
            preparation,
            "_currentProjectWorkFolder = projectFolder;",
            "AudioDiscProjectService.CreateInitialManifest(",
            "AudioDiscProjectService.Save(manifest);",
            "AttachCanonicalMetadataSessionToNewProject(projectFolder);");
    }

    [Fact]
    public void Source_aggregation_mutates_and_flushes_the_attached_session_instance()
    {
        var aggregation = SourceContract.ExtractMember(MainWindowSource, "private void ApplySourceMetadataAggregation(");

        SourceContract.AssertInOrder(
            aggregation,
            "RequireCanonicalMetadataSessionForProject(projectFolder);",
            "ProjectMetadataAggregationPersistenceService.Apply(",
            "ReferenceEquals(result.CanonicalMetadata, canonicalMetadata)",
            "_projectMetadata.ApplyCanonicalMetadata(canonicalMetadata);",
            "_projectMetadataSession.Flush();");
    }

    [Fact]
    public void Active_project_checkpoint_flushes_metadata_before_manifest_snapshots()
    {
        var checkpoint = SourceContract.ExtractMember(MainWindowSource, "private void TrySaveActiveProjectSnapshot()");

        SourceContract.AssertInOrder(
            checkpoint,
            "!TryFlushCanonicalMetadataBeforeExtensionSnapshot(",
            "TrySaveCurrentMp3DiscProjectSnapshot(",
            "TrySaveCurrentAudioDiscProjectSnapshot(");
    }

    [Theory]
    [InlineData("private async void AddProjectSources_Click(object sender, RoutedEventArgs e)", "manifest.ProjectFolder")]
    [InlineData("private async Task AddMoreLocalSourcesAsync()", "projectFolder")]
    [InlineData("private async Task AddMoreAudioDiscsAsync()", "manifest.ProjectFolder")]
    public void Project_extension_flushes_metadata_before_capturing_rollback_state(
        string memberSignature,
        string projectFolderExpression)
    {
        var extension = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        SourceContract.AssertInOrder(
            extension,
            $"TryFlushCanonicalMetadataBeforeExtensionSnapshot({projectFolderExpression})",
            $"CaptureAdditionalSourcesBaseline({projectFolderExpression})");
    }

    [Theory]
    [InlineData("private async void AddProjectSources_Click(object sender, RoutedEventArgs e)")]
    [InlineData("private void RollbackLocalProjectExtension(")]
    [InlineData("private async Task AddMoreAudioDiscsAsync()")]
    [InlineData("private async Task<bool> ImportMp3DiscProjectAsync(")]
    [InlineData("private async Task RunPreparedAudioDiscRipAsync(")]
    public void Extension_rollback_reloads_the_canonical_session(string memberSignature)
    {
        var rollback = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        SourceContract.AssertInOrder(
            rollback,
            "ProjectExtensionRollbackService.Rollback(",
            "ReloadCanonicalMetadataSessionAfterRollback(");
    }

    [Fact]
    public void Rollback_reload_rebinds_the_restored_metadata_to_ui_state()
    {
        var reload = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ReloadCanonicalMetadataSessionAfterRollback(string projectFolder)");

        SourceContract.AssertInOrder(
            reload,
            "RequireCanonicalMetadataSessionForProject(projectFolder);",
            "_projectMetadataSession.ReloadFromStore();",
            "_projectMetadata.ApplyCanonicalMetadata(metadata);");
    }

    [Fact]
    public void Final_output_flushes_the_session_before_projecting_tags()
    {
        var projection = SourceContract.ExtractMember(
            MainWindowSource,
            "private FinalAudioTagData CreateCurrentFinalAudioTagData(");

        SourceContract.AssertInOrder(
            projection,
            "RequireCanonicalMetadataSessionForProject(_currentProjectWorkFolder);",
            "_projectMetadataSession.Flush();",
            "ProjectMetadataOutputProjectionService().Create(");
    }

    [Fact]
    public void Metadata_dialog_save_requires_session_identity_and_flushes_the_session()
    {
        var dialog = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ShowMetadataDialog(Window owner, bool allowDuringProjectSetup = false)");
        var save = SourceContract.ExtractMember(
            MainWindowSource,
            "private void SaveMetadataDialogCanonicalMetadata(ProjectMetadata metadata)");

        Assert.Contains("saveMetadata: SaveMetadataDialogCanonicalMetadata", dialog, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            save,
            "ReferenceEquals(_projectMetadataSession.Current, metadata)",
            "_projectMetadataSession.Flush();");
    }
    [Fact]
    public void Folder_project_preparation_reuses_an_already_attached_canonical_session_for_the_same_project()
    {
        var preparation = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> PrepareCurrentLocalProjectSourcesAsync()");
        var ensure = SourceContract.ExtractMember(MainWindowSource, "private void EnsureCanonicalMetadataSessionAttachedToProject(string projectFolder)");
        var attach = SourceContract.ExtractMember(MainWindowSource, "private void AttachCanonicalMetadataSessionToNewProject(string projectFolder)");

        Assert.Contains("EnsureCanonicalMetadataSessionAttachedToProject(projectFolder);", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachCanonicalMetadataSessionToNewProject(projectFolder);", preparation, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            ensure,
            "if (!_projectMetadataSession.IsAttached)",
            "AttachCanonicalMetadataSessionToNewProject(projectFolder);",
            "RequireCanonicalMetadataSessionForProject(projectFolder);");
        Assert.Contains("if (_projectMetadataSession.IsAttached)", attach, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException", attach, StringComparison.Ordinal);

        var require = SourceContract.ExtractMember(MainWindowSource, "private ProjectMetadata RequireCanonicalMetadataSessionForProject(string projectFolder)");
        Assert.Contains("_projectMetadataSession.ProjectFolder", require, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_projectMetadataSession.Current, _projectMetadata.CanonicalMetadata)", require, StringComparison.Ordinal);
    }

}
