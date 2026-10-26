using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeferredProjectCompletionServiceTests
{
    [Fact]
    public void Atomic_marker_replace_failure_preserves_pending_order_and_never_deletes_work()
    {
        using var fixture = new LocalProject();
        fixture.Record();
        var bytes = File.ReadAllBytes(fixture.Service.MarkerPath);
        var failing = new DeferredProjectCompletionService(fixture.Root.Path, "Test", new AtomicJsonStore(new FailingReplace()));
        Assert.True(failing.ProcessStartup().NeedsAttention);
        Assert.Equal(bytes, File.ReadAllBytes(failing.MarkerPath));
        Assert.True(Directory.Exists(fixture.Folder));
        Assert.False(fixture.Service.ProcessStartup().NeedsAttention);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Missing_verified_identity_never_creates_marker(string id)
    {
        using var fixture = new LocalProject();
        Assert.Throws<InvalidDataException>(() => fixture.Service.RecordConfirmed(fixture.Identity with { ProjectId = id }, Guid.NewGuid(), true));
        Assert.False(File.Exists(fixture.Service.MarkerPath));
        Assert.True(Directory.Exists(fixture.Folder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_work_is_deleted_once_and_original_source_survives(bool partialCopy)
    {
        using var fixture = new LocalProject();
        if (partialCopy) File.WriteAllText(fixture.Media + ".copying", "partial");
        fixture.Record();
        var result = fixture.Service.ProcessStartup();
        Assert.False(result.NeedsAttention, result.Detail);
        Assert.False(Directory.Exists(fixture.Folder));
        Assert.Equal("original", File.ReadAllText(fixture.Original));
        Assert.False(File.Exists(fixture.Service.MarkerPath));
        Assert.False(fixture.Service.ProcessStartup().NeedsAttention);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("path")]
    [InlineData("type")]
    [InlineData("completed")]
    [InlineData("review")]
    [InlineData("revision")]
    [InlineData("foreign")]
    [InlineData("foreign-metadata")]
    [InlineData("environment")]
    [InlineData("root")]
    [InlineData("escape")]
    [InlineData("corrupt")]
    [InlineData("oversize")]
    [InlineData("confirmation")]
    public void Unsafe_or_changed_order_keeps_project_and_marker(string change)
    {
        using var fixture = new LocalProject();
        fixture.Record();
        var primary = ProjectFolderLayout.GetFolderManifestPath(fixture.Folder);
        if (change is "id" or "path" or "type" or "completed" or "review")
        {
            var document = JsonNode.Parse(File.ReadAllText(primary))!;
            if (change == "id") document["ProjectId"] = Guid.NewGuid().ToString("N");
            if (change == "path") document["ProjectFolder"] = fixture.Source.Path;
            if (change == "type") document["ProjectType"] = ProjectManifestTypes.AudioCdProject;
            if (change == "completed") document["PipelineState"] = ProjectPipelineStateNames.Completed;
            if (change == "review") document["PipelineState"] = ProjectPipelineStateNames.ReviewBeforeMerge;
            File.WriteAllText(primary, document.ToJsonString());
        }
        else if (change == "revision") File.AppendAllText(primary, " ");
        else if (change == "foreign") File.WriteAllText(Path.Combine(fixture.Folder, "unowned.txt"), "foreign");
        else if (change == "foreign-metadata")
        {
            var folder = ProjectFolderLayout.GetSourceMetadataSourcesFolder(fixture.Folder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, Guid.NewGuid() + ".json"), "{}");
        }
        else if (change == "corrupt") File.WriteAllText(fixture.Service.MarkerPath, "{");
        else if (change == "oversize") File.WriteAllText(fixture.Service.MarkerPath, new string(' ', 32769));
        else
        {
            var order = JsonNode.Parse(File.ReadAllText(fixture.Service.MarkerPath))!;
            if (change == "environment") order["Environment"] = "Other";
            if (change == "root") order["WorkingRoot"] = fixture.Source.Path;
            if (change == "escape") order["ProjectFolder"] = fixture.Source.Path;
            if (change == "confirmation") order["ConfirmedSessionEnd"] = "Query";
            File.WriteAllText(fixture.Service.MarkerPath, order.ToJsonString());
        }
        Assert.True(fixture.Service.ProcessStartup().NeedsAttention);
        Assert.True(Directory.Exists(fixture.Folder));
        Assert.True(File.Exists(fixture.Service.MarkerPath));
        Assert.Equal("original", File.ReadAllText(fixture.Original));
    }

    [Fact]
    public void Missing_target_is_already_done_and_duplicate_cannot_overwrite_order()
    {
        using var fixture = new LocalProject();
        fixture.Record();
        var bytes = File.ReadAllBytes(fixture.Service.MarkerPath);
        Assert.Throws<IOException>(fixture.Record);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.Service.MarkerPath));
        Directory.Delete(fixture.Folder, true);
        Assert.False(fixture.Service.ProcessStartup().NeedsAttention);
        Assert.False(File.Exists(fixture.Service.MarkerPath));
    }

    [Fact]
    public void Delete_failure_keeps_order_and_retries_only_on_next_start()
    {
        using var fixture = new LocalProject();
        var calls = 0;
        var failing = new DeferredProjectCompletionService(fixture.Root.Path, "Test", delete: (_, _) =>
        {
            calls++;
            return new(false, "injected failure");
        });
        fixture.Record();
        Assert.True(failing.ProcessStartup().NeedsAttention);
        Assert.Equal(1, calls);
        Assert.True(File.Exists(failing.MarkerPath));
        Assert.True(Directory.Exists(fixture.Folder));
        Assert.False(fixture.Service.ProcessStartup().NeedsAttention);
    }

    [Fact]
    public void Reparse_target_prevents_cleanup_of_its_destination()
    {
        using var fixture = new LocalProject();
        fixture.Record();
        var link = Path.Combine(fixture.Folder, "redirect");
        // Directory junctions require no developer-mode/symlink privilege.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        { Arguments = $"/c mklink /J \"{link}\" \"{fixture.Source.Path}\"", UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        process!.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.True(fixture.Service.ProcessStartup().NeedsAttention);
            Assert.Equal("original", File.ReadAllText(fixture.Original));
            Assert.True(File.Exists(fixture.Service.MarkerPath));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class FailingReplace : IAtomicJsonStoreFileSystem
    {
        public Stream CreateNewWriteThroughFile(string path) => new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough);
        public bool FileExists(string path) => File.Exists(path);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination) => throw new IOException("injected atomic replace failure");
        public void DeleteIfExists(string path) { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class LocalProject : IDisposable
    {
        public TemporaryFolder Root { get; } = new();
        public TemporaryFolder Source { get; } = new();
        public string Folder { get; }
        public string Media { get; }
        public string Original { get; }
        public DeferredProjectIdentity Identity { get; }
        public DeferredProjectCompletionService Service { get; }
        public LocalProject()
        {
            Folder = Path.Combine(WorkFolderStructure.FromRoot(Root.Path).LocalProjectsFolder, "active");
            new FolderProjectManifestService().Create(Folder, Source.Path);
            var primary = ProjectFolderLayout.GetFolderManifestPath(Folder);
            var main = JsonNode.Parse(File.ReadAllText(primary))!;
            main["PipelineState"] = ProjectPipelineStateNames.AcquiringSources;
            File.WriteAllText(primary, main.ToJsonString());
            var id = main["ProjectId"]!.GetValue<string>();
            Original = Path.Combine(Source.Path, "track.mp3");
            File.WriteAllText(Original, "original");
            Media = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(Folder), "track.mp3");
            File.WriteAllText(Media, "copy");
            var manifest = new ExportWorkManifest { ProjectId = id, ProjectWorkFolder = Folder,
                SourceFolder = Source.Path, State = new() { Status = ProjectPipelineStateNames.AcquiringSources },
                Tracks = [new() { SourcePath = Media, SourceId = Guid.NewGuid() }] };
            File.WriteAllText(ProjectFolderLayout.GetExportManifestPath(Folder), JsonSerializer.Serialize(manifest));
            Identity = new(Folder, ProjectManifestTypes.FolderProject, id);
            Service = new(Root.Path, "Test");
        }
        public void Record() => Service.RecordConfirmed(Identity, Guid.NewGuid(), true);
        public void Dispose() { Root.Dispose(); Source.Dispose(); }
    }
}
