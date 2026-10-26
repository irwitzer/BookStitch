using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class SessionProjectLifecycleTests
{
    [Theory]
    [InlineData(false, false, ProjectPipelineState.Preparing, false, false)]
    [InlineData(true, false, ProjectPipelineState.Preparing, false, false)]
    [InlineData(true, true, ProjectPipelineState.Preparing, false, true)]
    [InlineData(false, false, ProjectPipelineState.AcquiringSources, true, true)]
    [InlineData(false, false, ProjectPipelineState.Converting, true, true)]
    [InlineData(false, false, ProjectPipelineState.AcquiringSources, false, false)]
    [InlineData(true, false, ProjectPipelineState.AcquiringSources, true, false)]
    [InlineData(true, true, ProjectPipelineState.ReviewBeforeMerge, true, false)]
    [InlineData(true, true, ProjectPipelineState.Completed, true, false)]
    public void Deferred_completion_requires_started_or_active_import(bool owned, bool started,
        ProjectPipelineState phase, bool active, bool expected)
    {
        using var folder = new TemporaryFolder();
        var owner = new SessionProjectLifecycle();
        if (owned) owner.Register(folder.Path, ProjectManifestTypes.AudioCdProject, started);
        Assert.Equal(expected, owner.CanDeferCompletion(folder.Path, phase, active));
    }

    [Theory]
    [InlineData(ProjectManifestTypes.AudioCdProject, false, true)]
    [InlineData(ProjectManifestTypes.AudioCdProject, true, false)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, true, false)]
    [InlineData(ProjectManifestTypes.FolderProject, true, true)]
    public void Window_close_distinguishes_start_from_session_ownership(string type, bool started, bool delete)
    {
        using var folder = new TemporaryFolder();
        var owner = new SessionProjectLifecycle();
        owner.Register(folder.Path, type, started);
        Assert.Equal(delete ? ProjectCloseIntent.DeleteNewProject : ProjectCloseIntent.Save,
            ApplicationShutdownService.ResolveProjectCloseIntent(owner.DeleteOnWindowClose(folder.Path, true), false, false));
        Assert.False(owner.DeleteOnWindowClose(Path.Combine(folder.Path, "other"), true));
        Assert.False(owner.DeleteOnWindowClose(folder.Path, false) && started);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Both_audio_start_routes_protect_the_project_before_any_track_is_written(bool directStart)
    {
        using var fixture = new Draft();
        fixture.Owner.RegisterAudioPreparation(fixture.Root.Path, fixture.Manifest, directStart);
        if (!directStart) fixture.Owner.AcceptStart(fixture.Folder);
        Assert.False(fixture.Owner.DeleteOnWindowClose(fixture.Folder, true));
        Assert.Throws<IOException>(fixture.Validate);
        Assert.True(Directory.Exists(fixture.Folder));
    }

    [Fact]
    public void Loaded_workspace_never_inherits_discard_authority()
    {
        using var fixture = new Draft();
        fixture.Owner.Clear();
        fixture.Owner.AcceptStart(fixture.Folder);
        Assert.False(fixture.Owner.IsUnstartedAudio(fixture.Folder));
        Assert.Throws<IOException>(fixture.Validate);
    }



    [Theory]
    [InlineData("id")]
    [InlineData("created")]
    [InlineData("path")]
    [InlineData("type")]
    [InlineData("started")]
    [InlineData("stopped")]
    [InlineData("exported")]
    [InlineData("disc")]
    [InlineData("track")]
    [InlineData("media")]
    [InlineData("missing")]
    [InlineData("schema")]
    [InlineData("null-discs")]
    public void Changed_or_missing_draft_cannot_authorize_deletion(string change)
    {
        using var fixture = new Draft();
        switch (change)
        {
            case "schema": fixture.Manifest.SchemaVersion++; break;
            case "null-discs": fixture.Manifest.Discs = null!; break;
            case "id": fixture.Manifest.ProjectId = Guid.NewGuid().ToString("N"); break;
            case "created": fixture.Manifest.CreatedUtc = fixture.Manifest.CreatedUtc.AddSeconds(1); break;
            case "path": fixture.Manifest.ProjectFolder = fixture.Root.Path; break;
            case "type": fixture.Manifest.ProjectType = ProjectManifestTypes.Mp3DiscProject; break;
            case "started": fixture.Manifest.Status = AudioDiscProjectStatus.Ripping; break;
            case "stopped": fixture.Manifest.IsStoppedForResume = true; break;
            case "exported": fixture.Manifest.HasSuccessfulExport = true; break;
            case "disc": fixture.Manifest.Discs[0].Status = AudioDiscStatus.Completed; break;
            case "track": fixture.Manifest.Discs[0].Tracks[0].Status = AudioDiscTrackStatus.Ripped; break;
            case "media": fixture.Root.CreateFile("audio/draft/originals/CD 01/track.flac"); break;
        }
        fixture.Write();
        if (change == "missing") File.Delete(ProjectFolderLayout.GetAudioDiscManifestPath(fixture.Folder));
        Assert.ThrowsAny<IOException>(fixture.Validate);
        Assert.True(Directory.Exists(fixture.Folder));
        Assert.True(fixture.Owner.IsUnstartedAudio(fixture.Folder));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wrong_root_or_project_parent_cannot_authorize_discard(bool wrongRoot)
    {
        using var fixture = new Draft();
        Assert.Throws<IOException>(() => fixture.Owner.ValidateAudioDiscard(
            wrongRoot ? fixture.Parent : fixture.Root.Path,
            wrongRoot ? fixture.Parent : fixture.Root.Path, fixture.Folder));
    }



    [Fact]
    public void Reparse_entry_blocks_discard_and_keeps_external_source()
    {
        using var fixture = new Draft();
        using var outside = new TemporaryFolder();
        var source = outside.CreateFile("original.wav");
        var link = Path.Combine(fixture.Folder, "linked");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, outside.Path }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            Assert.True(process.WaitForExit(5000));
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside.Path);
        try
        {
            Assert.ThrowsAny<IOException>(fixture.Validate);
            Assert.True(File.Exists(source));
            Assert.True(Directory.Exists(fixture.Folder));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class Draft : IDisposable
    {
        public TemporaryFolder Root { get; } = new();
        public string Parent => Path.Combine(Root.Path, "audio");
        public string Folder => Path.Combine(Parent, "draft");
        public SessionProjectLifecycle Owner { get; } = new();
        public AudioDiscProjectManifest Manifest { get; }
        public Draft()
        {
            Manifest = new AudioDiscProjectManifest
            {
                ProjectId = Guid.NewGuid().ToString("N"), ProjectFolder = Folder, CreatedUtc = DateTime.UtcNow,
                ProjectFormatVersion = ProjectFormat.CurrentProjectFormatVersion,
                TotalDiscs = 2, Discs = [new() { DiscNumber = 1, Tracks = [new() { TrackNumber = 1 }] }]
            };
            Write();
            Owner.RegisterAudioPreparation(Root.Path, Manifest, false);
        }
        public void Write()
        {
            Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(Folder));
            File.WriteAllText(ProjectFolderLayout.GetAudioDiscManifestPath(Folder), JsonSerializer.Serialize(Manifest));
        }
        public void Validate() => Owner.ValidateAudioDiscard(Root.Path, Parent, Folder);
        public void Dispose() => Root.Dispose();
    }
}
