using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterProjectionReadTests
{
    [Fact]
    public void Mp3_projection_does_not_reconcile_sources_and_reordering_keeps_chapter_timeline()
    {
        using var folder = new TemporaryFolder();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = folder.Path, TotalDiscs = 1,
            ImportedDiscs = [new() { DiscNumber = 1, FileCount = 2, CopiedFiles = 2,
                Status = Mp3DiscImportStatus.Completed,
                SourceFiles = [new() { SourceId = ids[0], RelativePath = "CD 01/001.mp3", SourceSizeBytes = 10 },
                               new() { SourceId = ids[1], RelativePath = "CD 01/002.mp3", SourceSizeBytes = 10 }] }]
        };
        var service = new Mp3DiscProjectService();
        service.Save(manifest);
        var path = ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path);
        var bytes = File.ReadAllBytes(path);
        var projection = service.ReadForChapterProjection(folder.Path)!;
        Assert.Equal(Mp3DiscImportStatus.Completed, projection.ImportedDiscs[0].Status);
        // Normal load must still reject the same missing sources.
        Assert.NotEqual(Mp3DiscImportStatus.Completed, service.TryLoad(folder.Path)!.ImportedDiscs[0].Status);
        var rows = ids.Select((id, i) => new TrackInfo { SourceId = id,
            FilePath = Path.Combine(folder.Path, "originals", $"CD 01/00{i+1}.mp3"),
            FileName = $"00{i+1}.mp3", DurationTicks = TimeSpan.FromSeconds(30 + i).Ticks }).ToList();
        var request = new ChapterAutoEngineRequest(rows, ProjectManifestTypes.Mp3DiscProject, folder.Path,
            folder.Path, new ChapterCanonicalMetadataSnapshot(1, "Book", MetadataOrigin.User, null, MetadataOrigin.None),
            new ChapterSettings(), LegacyUseLeadingZeros: false);
        var engine = new ChapterAutoEngineService();
        var fallback = engine.Apply(request);
        var supplied = engine.Apply(request with { Mp3DiscManifest = projection });
        Assert.Equal(supplied.RenderedTimeline.Select(c => (c.Title, c.StartTicks)), fallback.RenderedTimeline.Select(c => (c.Title, c.StartTicks)));
        rows.Reverse();
        var reordered = engine.Apply(request);
        Assert.Same(rows[0], reordered.RenderedTimeline[0].SourceTrack);
        Assert.Equal(0, reordered.RenderedTimeline[0].StartTicks);
        Assert.Equal(rows[0].DurationTicks, reordered.RenderedTimeline[1].StartTicks);
        Assert.All(reordered.RenderedTimeline, c => Assert.Contains("Book", c.Title));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Audio_projection_preserves_persisted_context_but_normal_load_validates_sources()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = new AudioDiscProjectManifest { ProjectFolder = folder.Path,
            Discs = [new() { DiscNumber = 1, DiscIdentity = "test", Tracks = [new() {
                SourceId = Guid.NewGuid(), GlobalIndex = 1, DiscNumber = 1, TrackNumber = 1,
                RelativePath = "originals/CD 01/001.flac", Status = AudioDiscTrackStatus.Ripped,
                OutputSizeBytes = 10 }] }] };
        service.Save(manifest);
        var path = ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(AudioDiscTrackStatus.Ripped, service.ReadForChapterProjection(folder.Path)!.Discs[0].Tracks[0].Status);
        Assert.Equal(AudioDiscTrackStatus.Pending, service.TryLoad(folder.Path)!.Discs[0].Tracks[0].Status);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
