using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceCopyIdentityStoreTests
{
    [Fact]
    public void Same_length_target_corruption_is_rejected_even_when_timestamps_match()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(root.Path, "project");
        var source = Path.Combine(root.Path, "source.mp3");
        var target = Path.Combine(project, "originals", "source.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(source, [1, 2, 3]);
        File.Copy(source, target);
        var store = new SourceCopyIdentityStore();
        var identity = store.RecordCopiedFile(project, source, target);
        var timestamp = File.GetLastWriteTimeUtc(target);
        File.WriteAllBytes(target, [1, 9, 3]);
        File.SetLastWriteTimeUtc(target, timestamp);

        Assert.NotNull(identity.ContentSha256);
        Assert.False(store.CanReuseExistingCopy(project, source, target));
    }

    [Fact]
    public void Legacy_identity_without_a_content_hash_does_not_authorize_reuse()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(root.Path, "project");
        var source = Path.Combine(root.Path, "source.mp3");
        var target = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(project), "source.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(source, [1, 2, 3]);
        File.Copy(source, target);
        var store = new SourceCopyIdentityStore();
        var identity = store.RecordCopiedFile(project, source, target);
        var state = Path.Combine(ProjectFolderLayout.GetSettingsFolder(project), SourceCopyIdentityStore.FileName);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(state))!;
        json["Files"]![0]!.AsObject().Remove("ContentSha256");
        File.WriteAllText(state, json.ToJsonString());

        Assert.False(store.CanReuseExistingCopy(project, source, target));
        var repaired = store.RecordCopiedFile(project, source, target);
        Assert.Equal(identity.SourceId, repaired.SourceId);
        Assert.True(store.CanReuseExistingCopy(project, source, target));
    }

    [Fact]
    public void RecordCopiedFile_PersistsIdentityAndAllowsMatchingReuse()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var sourceFile = Path.Combine(folder.Path, "source", "chapter.mp3");
        var targetFile = Path.Combine(projectFolder, "originals", "chapter.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.WriteAllBytes(sourceFile, [1, 2, 3]);
        File.Copy(sourceFile, targetFile);
        var lastWriteUtc = new DateTime(2026, 8, 9, 10, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourceFile, lastWriteUtc);

        var store = new SourceCopyIdentityStore();
        var identity = store.RecordCopiedFile(projectFolder, sourceFile, targetFile);
        var reloaded = new SourceCopyIdentityStore();

        Assert.Equal(Path.Combine("originals", "chapter.mp3"), identity.TargetRelativePath);
        Assert.NotEqual(Guid.Empty, identity.SourceId);
        Assert.True(reloaded.CanReuseExistingCopy(projectFolder, sourceFile, targetFile));
        Assert.Single(reloaded.Load(projectFolder));
        var json = File.ReadAllText(Path.Combine(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName));
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"SourceId\":", json);
        Assert.Contains("\"Files\":", json);
        Assert.Empty(Directory.EnumerateFiles(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName + ".*.tmp"));
    }

    [Fact]
    public void CanReuseExistingCopy_ReturnsFalseWhenSourceLastWriteTimeChanged()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var sourceFile = Path.Combine(folder.Path, "source", "chapter.mp3");
        var targetFile = Path.Combine(projectFolder, "originals", "chapter.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.WriteAllBytes(sourceFile, [1, 2, 3]);
        File.Copy(sourceFile, targetFile);
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 8, 9, 10, 30, 0, DateTimeKind.Utc));

        var store = new SourceCopyIdentityStore();
        store.RecordCopiedFile(projectFolder, sourceFile, targetFile);
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 8, 9, 10, 31, 0, DateTimeKind.Utc));

        Assert.False(store.CanReuseExistingCopy(projectFolder, sourceFile, targetFile));
    }

    [Fact]
    public void RecordCopiedFile_ReusesSourceIdForUnchangedSourceIdentity()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var sourceFile = Path.Combine(folder.Path, "source", "chapter.mp3");
        var targetFile = Path.Combine(projectFolder, "originals", "chapter.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.WriteAllBytes(sourceFile, [1, 2, 3]);
        File.Copy(sourceFile, targetFile);
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 8, 9, 10, 30, 0, DateTimeKind.Utc));

        var store = new SourceCopyIdentityStore();
        var first = store.RecordCopiedFile(projectFolder, sourceFile, targetFile);
        var second = store.RecordCopiedFile(projectFolder, sourceFile, targetFile);

        Assert.Equal(first.SourceId, second.SourceId);
    }

    [Fact]
    public void Load_LegacyArray_MigratesToSchemaVersionedState()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = Path.Combine(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName);
        File.WriteAllText(
            path,
            """
            [
              {
                "TargetRelativePath": "originals\\chapter.mp3",
                "SourceFileName": "chapter.mp3",
                "SourceSizeBytes": 3,
                "SourceLastWriteUtcTicks": 638903898000000000
              }
            ]
            """);

        var identities = new SourceCopyIdentityStore().Load(projectFolder);

        var identity = Assert.Single(identities);
        Assert.Equal("chapter.mp3", identity.SourceFileName);
        var migratedJson = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", migratedJson);
        Assert.Contains("\"Files\":", migratedJson);
    }

    [Fact]
    public void Load_UnsupportedSchema_RejectsAndLeavesFileUnchanged()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = Path.Combine(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName);
        var json = """
        {
          "schemaVersion": 99,
          "Files": []
        }
        """;
        File.WriteAllText(path, json);

        Assert.Throws<PersistentJsonSchemaException>(() =>
            new SourceCopyIdentityStore().Load(projectFolder));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void Load_InvalidLegacyJson_DoesNotRewrite()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = Path.Combine(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName);
        const string json = "{ invalid";
        File.WriteAllText(path, json);

        Assert.Empty(new SourceCopyIdentityStore().Load(projectFolder));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public async Task RecordCopiedFile_ConcurrentDistinctIdentities_PersistsEveryEntryAndAllowsReuse()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var sourceFolder = Path.Combine(folder.Path, "source");
        var targetFolder = Path.Combine(projectFolder, "originals");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(targetFolder);
        var files = Enumerable.Range(1, 40)
            .Select(index =>
            {
                var sourceFile = Path.Combine(sourceFolder, $"{index:0000}.mp3");
                var targetFile = Path.Combine(targetFolder, $"{index:0000}.mp3");
                File.WriteAllBytes(sourceFile, BitConverter.GetBytes(index).Concat(new byte[] { 1, 2, 3 }).ToArray());
                File.Copy(sourceFile, targetFile);
                File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc).AddSeconds(index));
                return (SourceFile: sourceFile, TargetFile: targetFile);
            })
            .ToArray();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var registrations = files
            .Select(async item =>
            {
                await startGate.Task;
                return new SourceCopyIdentityStore().RecordCopiedFile(
                    projectFolder,
                    item.SourceFile,
                    item.TargetFile);
            })
            .ToArray();

        startGate.SetResult();
        await Task.WhenAll(registrations);

        var reloadedStore = new SourceCopyIdentityStore();
        var identities = reloadedStore.Load(projectFolder);
        Assert.Equal(files.Length, identities.Count);
        Assert.Equal(
            files.Length,
            identities.Select(item => item.TargetRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, item =>
            Assert.True(reloadedStore.CanReuseExistingCopy(projectFolder, item.SourceFile, item.TargetFile)));
        AssertNoTemporaryStoreFiles(projectFolder);
    }

    [Fact]
    public async Task RecordCopiedFile_ConcurrentSameIdentity_ReusesOneStableSourceId()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var sourceFile = Path.Combine(folder.Path, "source", "chapter.mp3");
        var targetFile = Path.Combine(projectFolder, "originals", "chapter.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.WriteAllBytes(sourceFile, [1, 2, 3, 4]);
        File.Copy(sourceFile, targetFile);
        File.SetLastWriteTimeUtc(sourceFile, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var registrations = Enumerable.Range(0, 24)
            .Select(async _ =>
            {
                await startGate.Task;
                return new SourceCopyIdentityStore().RecordCopiedFile(projectFolder, sourceFile, targetFile);
            })
            .ToArray();

        startGate.SetResult();
        var recorded = await Task.WhenAll(registrations);

        var stableSourceId = Assert.Single(recorded.Select(item => item.SourceId).Distinct());
        var reloadedStore = new SourceCopyIdentityStore();
        var persisted = Assert.Single(reloadedStore.Load(projectFolder));
        Assert.Equal(stableSourceId, persisted.SourceId);
        Assert.NotEqual(Guid.Empty, persisted.SourceId);
        Assert.Equal(Path.Combine("originals", "chapter.mp3"), persisted.TargetRelativePath);
        Assert.True(reloadedStore.CanReuseExistingCopy(projectFolder, sourceFile, targetFile));
        AssertNoTemporaryStoreFiles(projectFolder);
    }

    private static void AssertNoTemporaryStoreFiles(string projectFolder)
    {
        Assert.Empty(Directory.EnumerateFiles(
            ProjectFolderLayout.GetSettingsFolder(projectFolder),
            SourceCopyIdentityStore.FileName + ".*.tmp"));
    }

}
