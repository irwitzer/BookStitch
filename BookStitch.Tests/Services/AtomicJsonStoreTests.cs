using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AtomicJsonStoreTests
{
    [Theory]
    [InlineData(unchecked((int)0x80070020), true)]
    [InlineData(unchecked((int)0x80070021), true)]
    [InlineData(unchecked((int)0x80070497), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(unchecked((int)0x80070070), false)]
    [InlineData(unchecked((int)0x80070057), false)]
    public void Retries_only_recognized_lock_errors(int hresult, bool transient) =>
        Assert.Equal(transient, AtomicJsonStore.IsTransientLock(new IOException("failure", hresult)));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Transient_commit_lock_is_retried_with_the_same_complete_temp_file(bool restore, bool existing)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "state.json");
        if (existing) File.WriteAllText(path, "old");
        var fs = new LockingFileSystem { Failures = 2 };
        var store = new AtomicJsonStore(fs);
        if (restore) store.RestoreBytes(path, System.Text.Encoding.UTF8.GetBytes("new"));
        else store.Save(path, "new", null);
        Assert.Equal(3, fs.Attempts);
        Assert.Contains("new", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), 5)]
    [InlineData(unchecked((int)0x80070070), 1)]
    public void Exhausted_or_permanent_commit_failure_preserves_target_and_cleanup_does_not_mask_it(int hresult, int attempts)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "state.json");
        File.WriteAllText(path, "old");
        var fs = new LockingFileSystem { Failures = 10, ErrorCode = hresult, CleanupFails = true };
        var error = Assert.Throws<IOException>(() => new AtomicJsonStore(fs).Save(path, "new", null));
        Assert.Equal(hresult, error.HResult);
        Assert.Equal(attempts, fs.Attempts);
        Assert.Equal("old", File.ReadAllText(path));
    }

    private sealed class LockingFileSystem : IAtomicJsonStoreFileSystem
    {
        public int Failures, Attempts;
        public int ErrorCode = unchecked((int)0x80070020);
        public bool CleanupFails;
        public Stream CreateNewWriteThroughFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        public bool FileExists(string path) => File.Exists(path);
        public void Move(string sourcePath, string destinationPath) => Commit(sourcePath, destinationPath);
        public void Replace(string sourcePath, string destinationPath) => Commit(sourcePath, destinationPath);
        private void Commit(string source, string target)
        {
            Attempts++;
            if (Attempts <= Failures) throw new IOException("commit lock", ErrorCode);
            File.Move(source, target, overwrite: true);
        }
        public void DeleteIfExists(string path)
        {
            if (CleanupFails) throw new IOException("cleanup failure");
            File.Delete(path);
        }
    }

    [Fact]
    public void RestoreBytes_replace_failure_preserves_existing_json_and_cleans_temporary_file()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "state.json");
        File.WriteAllText(path, "{\"value\":1}");
        var original = File.ReadAllBytes(path);
        var store = new AtomicJsonStore(new ThrowingReplaceFileSystem());
        Assert.Throws<IOException>(() => store.RestoreBytes(path, System.Text.Encoding.UTF8.GetBytes("{\"value\":2}")));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public void Save_CreatesNewJsonFileAndRemovesTemporaryFile()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        var store = new AtomicJsonStore();

        store.Save(path, new SampleSettings("Book", 7), new JsonSerializerOptions { WriteIndented = true });

        Assert.True(File.Exists(path));
        Assert.Contains("\"Name\": \"Book\"", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "settings.json.*.tmp"));
    }

    [Fact]
    public void Save_ReplacesExistingJsonFileWithCompleteNewContent()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        File.WriteAllText(path, "{\"Name\":\"Old\",\"Count\":1}");
        var store = new AtomicJsonStore();

        store.Save(path, new SampleSettings("New", 42), new JsonSerializerOptions { WriteIndented = false });

        Assert.Equal("{\"Name\":\"New\",\"Count\":42}", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "project.json.*.tmp"));
    }

    [Fact]
    public void Save_RespectsProvidedSerializerOptions()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "report.json");
        var store = new AtomicJsonStore();

        store.Save(
            path,
            new SampleSettings("Camel", 3),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Equal("{\"name\":\"Camel\",\"count\":3}", File.ReadAllText(path));
    }

    [Fact]
    public void Save_WhenReplaceFails_RemovesOwnTemporaryFileAndKeepsExistingTarget()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "audio-disc-project.json");
        File.WriteAllText(path, "{\"Name\":\"Existing\",\"Count\":1}");
        var fileSystem = new ThrowingReplaceFileSystem();
        var store = new AtomicJsonStore(
            fileSystem,
            () => Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var exception = Assert.Throws<IOException>(() =>
            store.Save(path, new SampleSettings("New", 2), new JsonSerializerOptions()));

        Assert.Equal("replace failed", exception.Message);
        Assert.Equal("{\"Name\":\"Existing\",\"Count\":1}", File.ReadAllText(path));
        Assert.False(File.Exists(fileSystem.CreatedPaths.Single()));
    }

    [Fact]
    public void Save_UsesUniqueHumanReadableTemporaryFileNames()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "track-list-state.json");
        var fileSystem = new RecordingFileSystem();
        var guids = new Queue<Guid>([
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222")
        ]);
        var store = new AtomicJsonStore(fileSystem, guids.Dequeue);

        store.Save(path, new SampleSettings("First", 1), new JsonSerializerOptions());
        store.Save(path, new SampleSettings("Second", 2), new JsonSerializerOptions());

        Assert.Equal(2, fileSystem.CreatedPaths.Count);
        Assert.NotEqual(fileSystem.CreatedPaths[0], fileSystem.CreatedPaths[1]);
        Assert.All(fileSystem.CreatedPaths, temporaryPath =>
        {
            Assert.Contains("track-list-state.json.", Path.GetFileName(temporaryPath), StringComparison.Ordinal);
            Assert.EndsWith(".tmp", temporaryPath, StringComparison.Ordinal);
        });
    }

    private sealed record SampleSettings(string Name, int Count);

    private sealed class RecordingFileSystem : IAtomicJsonStoreFileSystem
    {
        public List<string> CreatedPaths { get; } = [];

        public Stream CreateNewWriteThroughFile(string path)
        {
            CreatedPaths.Add(path);
            return new MemoryStream();
        }

        public bool FileExists(string path) => false;

        public void Move(string sourcePath, string destinationPath)
        {
        }

        public void Replace(string sourcePath, string destinationPath)
        {
        }

        public void DeleteIfExists(string path)
        {
        }
    }

    private sealed class ThrowingReplaceFileSystem : IAtomicJsonStoreFileSystem
    {
        public List<string> CreatedPaths { get; } = [];

        public Stream CreateNewWriteThroughFile(string path)
        {
            CreatedPaths.Add(path);
            return new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough);
        }

        public bool FileExists(string path) => File.Exists(path);

        public void Move(string sourcePath, string destinationPath) =>
            File.Move(sourcePath, destinationPath);

        public void Replace(string sourcePath, string destinationPath) =>
            throw new IOException("replace failed");

        public void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
