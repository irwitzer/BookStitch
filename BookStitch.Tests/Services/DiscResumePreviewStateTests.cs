using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

// Neutral runtime identity/edit contract; no drive, WPF or device access.
public sealed class DiscResumePreviewStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_mp3_attempt_keeps_its_permission_only_for_the_same_disc_and_signature(bool duplicate)
    {
        var state = new DiscResumePreviewState();
        var disc = new Mp3DiscPendingChapterDisc(3, "signature", []);
        state.Set(disc, duplicate);
        Assert.False(state.CanResumeMp3Import(3, "signature"));
        state.MarkProcessingStarted();
        Assert.True(state.CanResumeMp3Import(3, "SIGNATURE"));
        Assert.Equal(duplicate, state.Mp3DuplicateAccepted);
        Assert.False(state.CanResumeMp3Import(4, "signature"));
        Assert.False(state.CanResumeMp3Import(3, "other-medium"));
        Assert.Same(disc, state.Mp3Disc);
        state.Clear();
        Assert.False(state.Mp3DuplicateAccepted);
        Assert.False(state.CanResumeMp3Import(3, "signature"));
        state.Set(new Mp3DiscPendingChapterDisc(4, "signature", []));
        Assert.False(state.Mp3DuplicateAccepted);
    }

    [Fact]
    public void Starting_preview_retains_import_identity_but_cannot_reopen_configuration_on_a_later_pause()
    {
        var owner = new DiscResumePreviewState();
        owner.MarkProcessingStarted();
        Assert.False(owner.ProcessingStarted);
        var prepared = new Mp3DiscPendingChapterDisc(3, "fixture", []);
        owner.Set(prepared);
        Assert.False(owner.ProcessingStarted);
        owner.MarkProcessingStarted();
        Assert.True(owner.ProcessingStarted);
        Assert.Same(prepared, owner.Mp3Disc);
        owner.Set(new AudioDiscProjectManifestDisc { DiscNumber = 3, DiscIdentity = "new-preview" });
        Assert.False(owner.ProcessingStarted);
        owner.MarkProcessingStarted();
        Assert.True(owner.ProcessingStarted);
        owner.Clear();
        Assert.False(owner.ProcessingStarted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Audio_preview_is_uncommitted_and_same_disc_commit_retains_ids_and_edits(int completed)
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = new AudioDiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2,
            SourceDriveRoot = "previous" };
        if (completed == 1)
        {
            var first = service.AddDisc(manifest, Disc("first"), 1);
            first.Status = AudioDiscStatus.Completed;
        }
        var current = service.CreateTrackPreview(manifest);
        var oldRoot = manifest.SourceDriveRoot;
        var next = completed + 1;
        var prepared = service.BuildDiscEntry(manifest, Disc("next"), next, service.GetNextGlobalTrackIndex(manifest));
        var owner = new DiscResumePreviewState();
        owner.Set(prepared);
        var visible = DiscResumePreviewState.AppendAudioTracks(current, service.CreateTrackPreview(manifest, prepared)).ToList();
        Assert.Equal(completed, manifest.Discs.Count);
        Assert.Equal(oldRoot, manifest.SourceDriveRoot);
        Assert.Equal((completed + 1) * 2, visible.Count);
        Assert.All(visible, track => Assert.NotEqual(Guid.Empty, track.SourceId));
        Assert.Equal(visible.Count, visible.Select(track => track.SourceId).Distinct().Count());
        Assert.Equal(visible.Count, DiscResumePreviewState.AppendAudioTracks(visible, service.CreateTrackPreview(manifest, prepared)).Count);
        foreach (var track in current) Assert.Same(track, visible.Single(item => item.SourceId == track.SourceId));

        visible.Reverse();
        visible[0].IsExcluded = true;
        visible[0].ExcludedChapterTitle = "User excluded chapter";
        visible[1].ChapterTitle = "User chapter";
        var state = new TrackListStateService();
        state.Save(folder.Path, visible);
        var committed = service.AddDisc(manifest, Disc("next"), next, preparedDisc: owner.AudioDisc);
        Assert.Same(prepared, committed);
        var restored = state.Apply(folder.Path, service.CreateTrackPreview(manifest));
        Assert.Equal(visible.Select(track => track.SourceId), restored.Select(track => track.SourceId));
        Assert.True(restored[0].IsExcluded);
        Assert.Equal("User excluded chapter", restored[0].ExcludedChapterTitle);
        Assert.Equal("User chapter", restored[1].ChapterTitle);
        var before = ChapterCandidateProjectionContext.ForAudioCd("project", new AudioDiscProjectManifest(), prepared);
        var after = ChapterCandidateProjectionContext.ForAudioCd("project", manifest);
        foreach (var track in prepared.Tracks)
            Assert.Equal(before.ScopeProvenanceBySourceId[track.SourceId], after.ScopeProvenanceBySourceId[track.SourceId]);
    }

    [Fact]
    public void Audio_changed_medium_cannot_commit_prepared_identity_and_repeated_disc_still_requires_override()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = new AudioDiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2 };
        var first = service.AddDisc(manifest, Disc("first"), 1);
        var prepared = service.BuildDiscEntry(manifest, Disc("first"), 2, 3);
        Assert.Throws<InvalidOperationException>(() => service.AddDisc(manifest, Disc("other"), 2, preparedDisc: prepared));
        Assert.Throws<InvalidOperationException>(() => service.AddDisc(manifest, Disc("first"), 2, preparedDisc: prepared));
        Assert.Single(manifest.Discs);
        service.AddDisc(manifest, Disc("first"), 2, acceptDuplicate: true, preparedDisc: prepared);
        Assert.Empty(first.Tracks.Select(track => track.SourceId).Intersect(prepared.Tracks.Select(track => track.SourceId)));
    }

    [Fact]
    public void Runtime_owner_clears_all_prepared_identity_when_source_changes()
    {
        var owner = new DiscResumePreviewState();
        owner.Set(new Mp3DiscPendingChapterDisc(2, "scan", []));
        Assert.True(owner.MatchesMp3(2, "SCAN"));
        Assert.False(owner.MatchesMp3(2, "changed"));
        owner.Set(new AudioDiscProjectManifestDisc { DiscNumber = 2, DiscIdentity = "toc" });
        Assert.Null(owner.Mp3Disc);
        Assert.True(owner.MatchesAudio(2, "toc"));
        Assert.False(owner.MatchesAudio(2, "changed"));
        owner.Clear();
        Assert.Null(owner.DiscNumber);
        Assert.Null(owner.AudioDisc);
    }

    private static AudioDiscInfo Disc(string identity) => new("selected", "selected",
        Enumerable.Range(1, 2).Select(number => new AudioDiscTrackInfo(number,
            TimeSpan.FromMinutes(number - 1), TimeSpan.FromMinutes(1), $"{identity}-{number}", 150 + (number - 1) * 4500)).ToArray(),
        TimeSpan.FromMinutes(2), identity, new AudioDiscToc(1, 2, 9150, [150, 4650], "toc"));
}
