using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json.Nodes;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class DiscProjectStopSaveServiceTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Confirmed_end_deferred_commit_preserves_two_discs_and_releases_partial_third(bool audio, bool logoff)
    {
        using var root = new TemporaryFolder();
        using var fixture = DeferredFixture(root, audio, out var identity);
        fixture.CompleteSecondDisc();
        var third = root.CreateFile(Path.GetRelativePath(root.Path, Path.Combine(fixture.Root, "originals/CD 03/third" + (audio ? ".flac" : ".mp3"))));
        File.WriteAllBytes(third, [1, 2, 3]);
        if (audio)
        {
            var path = ProjectFolderLayout.ResolveAudioDiscManifestPath(fixture.Root);
            var manifest = Read<AudioDiscProjectManifest>(path);
            manifest.Discs.Add(new() { DiscNumber = 3, Status = AudioDiscStatus.Ripping, TrackCount = 2,
                Tracks = [new() { SourceId = Guid.NewGuid(), TrackNumber = 1, DiscNumber = 3,
                    RelativePath = Path.GetRelativePath(fixture.Root, third), Status = AudioDiscTrackStatus.Ripped }] });
            Write(path, manifest);
        }
        else
        {
            var path = ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
            var manifest = Read<Mp3DiscProjectManifest>(path);
            manifest.ImportedDiscs.Add(new() { DiscNumber = 3, Status = "Importing", FileCount = 2, CopiedFiles = 1,
                LocalFolder = Path.GetDirectoryName(third)!, SourceFiles = [new() { SourceId = Guid.NewGuid(),
                    RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(fixture.Root), third) }] });
            Write(path, manifest);
        }
        var service = new DeferredProjectCompletionService(root.Path, "Test");
        service.RecordConfirmed(identity, Guid.NewGuid(), logoff);
        Assert.True(new FileInfo(service.MarkerPath).Length < 32768);
        var result = service.ProcessStartup();
        Assert.False(result.NeedsAttention, result.Detail);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.Equal(2, plan.ImportedDiscCount);
        Assert.Equal(3, plan.NextMissingDiscNumber);
        Assert.True(File.Exists(fixture.FirstSource));
        Assert.True(File.Exists(fixture.PartialSource));
        Assert.False(File.Exists(third));
        Assert.False(File.Exists(service.MarkerPath));
        Assert.False(service.ProcessStartup().NeedsAttention);
        Assert.Equal(3, new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!.NextMissingDiscNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Deferred_empty_first_disc_preserves_identity_and_resumes_disc_one(bool audio)
    {
        using var root = new TemporaryFolder();
        using var fixture = DeferredFixture(root, audio, out var identity);
        fixture.RemoveCompleteDisc();
        File.Delete(fixture.FirstSource);
        File.Delete(fixture.PartialSource);
        var service = new DeferredProjectCompletionService(root.Path, "Test");
        service.RecordConfirmed(identity, Guid.NewGuid(), false);
        var result = service.ProcessStartup();
        Assert.False(result.NeedsAttention, result.Detail);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(fixture.Root)!;
        Assert.Equal(1, plan.NextMissingDiscNumber);
        Assert.Empty(plan.Tracks);
        Assert.True(Directory.Exists(fixture.Root));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void Deferred_interruption_retries_only_its_bound_journal_or_acknowledges_completed_commit(bool audio, int point)
    {
        using var root = new TemporaryFolder();
        using var fixture = DeferredFixture(root, audio, out var identity);
        var interrupted = new DeferredProjectCompletionService(root.Path, "Test", saveDisc: (folder, prepared) =>
        {
            if (point == 2)
            {
                _service.Save(folder, prepared);
                throw new IOException("after successful commit, before marker removal");
            }
            _service.Save(folder, journal =>
            {
                prepared(journal);
                if (point == 1)
                {
                    var write = JsonNode.Parse(File.ReadAllText(journal))!["Writes"]!.AsArray()[0]!;
                    new AtomicJsonStore().Save(Path.Combine(folder, write["RelativePath"]!.GetValue<string>()),
                        write["Document"], new() { WriteIndented = true });
                }
                throw new IOException("injected interruption");
            });
        });
        interrupted.RecordConfirmed(identity, Guid.NewGuid(), false);
        Assert.True(interrupted.ProcessStartup().NeedsAttention);
        Assert.True(File.Exists(interrupted.MarkerPath));
        var result = new DeferredProjectCompletionService(root.Path, "Test").ProcessStartup();
        Assert.False(result.NeedsAttention, result.Detail);
        Assert.True(File.Exists(fixture.FirstSource));
        Assert.False(File.Exists(fixture.PartialSource));
        Assert.False(File.Exists(interrupted.MarkerPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Deferred_existing_pending_journal_is_validated_and_recovered(bool audio)
    {
        using var root = new TemporaryFolder();
        using var fixture = DeferredFixture(root, audio, out var identity);
        Assert.Throws<IOException>(() => _service.Save(fixture.Root, _ => throw new IOException("pending")));
        var service = new DeferredProjectCompletionService(root.Path, "Test");
        service.RecordConfirmed(identity, Guid.NewGuid(), true);
        var result = service.ProcessStartup();
        Assert.False(result.NeedsAttention, result.Detail);
        Assert.False(_service.HasPendingCommit(fixture.Root));
        Assert.True(File.Exists(fixture.FirstSource));
        Assert.False(File.Exists(fixture.PartialSource));
    }

    private static Fixture DeferredFixture(TemporaryFolder root, bool audio, out DeferredProjectIdentity identity)
    {
        var layout = WorkFolderStructure.FromRoot(root.Path);
        var fixture = new Fixture(audio, Path.Combine(audio ? layout.AudioDiscProjectsFolder : layout.Mp3DiscProjectsFolder, "active"));
        var path = audio ? ProjectFolderLayout.ResolveAudioDiscManifestPath(fixture.Root) : ProjectFolderLayout.ResolveMp3DiscManifestPath(fixture.Root);
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        var id = Guid.NewGuid().ToString("N");
        document["ProjectId"] = id;
        document["PipelineState"] = ProjectPipelineStateNames.AcquiringSources;
        File.WriteAllText(path, document.ToJsonString());
        identity = new(fixture.Root, audio ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.Mp3DiscProject, id);
        return fixture;
    }
}
