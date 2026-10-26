using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Diagnostics;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DestructiveProjectFileSystemTests
{
    [Theory]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("capture", false)]
    [InlineData("capture", true)]
    [InlineData("rollback", false)]
    [InlineData("rollback", true)]
    [InlineData("cover", false)]
    [InlineData("cover", true)]
    [InlineData("copy", false)]
    [InlineData("copy", true)]
    [InlineData("restore", false)]
    [InlineData("restore", true)]
    public void Project_operations_reject_linked_tree_or_linked_ancestor_without_touching_external_files(string operation, bool ancestor)
    {
        using var root = new TemporaryFolder();
        using var outside = new TemporaryFolder();
        var project = Directory.CreateDirectory(Path.Combine(root.Path, "project")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(outside.Path, "nested")).FullName;
        var external = Path.Combine(target, "state.json");
        File.WriteAllText(external, "{\"external\":true}");
        File.SetAttributes(external, FileAttributes.ReadOnly);
        var rollback = new ProjectExtensionRollbackService();
        var snapshot = rollback.Capture(project);
        var link = ancestor ? Path.Combine(root.Path, "linked") : Path.Combine(project, "linked");
        CreateLink(link, outside.Path);
        var accessedProject = ancestor ? Path.Combine(link, "nested") : project;
        try
        {
            switch (operation)
            {
                case "delete":
                    Assert.False(new ProjectIndexService().DeleteProject(root.Path, accessedProject).Deleted);
                    break;
                case "capture":
                    Assert.Throws<ProjectPathSafetyException>(() => rollback.Capture(accessedProject));
                    break;
                case "rollback":
                    if (ancestor) snapshot = snapshot with { ProjectFolder = accessedProject };
                    Assert.Throws<ProjectPathSafetyException>(() => rollback.Rollback(snapshot));
                    break;
                case "cover":
                    var coversFolder = ancestor ? Directory.CreateDirectory(Path.Combine(accessedProject, "covers")).FullName : link;
                    var externalCover = Path.Combine(coversFolder, "cover.jpg");
                    File.WriteAllText(externalCover, "external cover");
                    Assert.NotEmpty(new CoverCleanupService().DeleteOldUnreferencedCoverFiles(
                        accessedProject, coversFolder, DateTime.UtcNow.AddDays(1), 0));
                    Assert.Equal("external cover", File.ReadAllText(externalCover));
                    break;
                case "copy":
                    var source = root.CreateFile("source.mp3");
                    var copyTarget = ancestor ? Path.Combine(accessedProject, "state.json") : Path.Combine(link, "nested", "state.json");
                    Assert.Throws<ProjectPathSafetyException>(() => SourceFileCopyService.CopyAtomicallyAsync(source, copyTarget, CancellationToken.None).GetAwaiter().GetResult());
                    break;
                case "restore":
                    var restoreTarget = ancestor ? Path.Combine(accessedProject, "state.json") : Path.Combine(link, "nested", "state.json");
                    Assert.Throws<ProjectPathSafetyException>(() => new AtomicJsonStore().RestoreBytes(restoreTarget, [1, 2, 3]));
                    break;
            }
            Assert.Equal("{\"external\":true}", File.ReadAllText(external));
            Assert.True((File.GetAttributes(external) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            Directory.Delete(link); // Remove only the link itself.
            File.SetAttributes(external, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Rollback_does_not_restore_snapshot_through_a_replaced_settings_directory()
    {
        using var project = new TemporaryFolder();
        using var outside = new TemporaryFolder();
        var settings = Directory.CreateDirectory(Path.Combine(project.Path, "project-settings")).FullName;
        File.WriteAllText(Path.Combine(settings, "project.json"), "{\"before\":true}");
        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(project.Path);
        File.Delete(Path.Combine(settings, "project.json"));
        Directory.Delete(settings);
        var external = outside.CreateFile("project.json");
        File.WriteAllText(external, "{\"external\":true}");
        CreateLink(settings, outside.Path);
        try
        {
            Assert.Throws<ProjectPathSafetyException>(() => service.Rollback(snapshot));
            Assert.Equal("{\"external\":true}", File.ReadAllText(external));
        }
        finally { Directory.Delete(settings); }
    }

    private static void CreateLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(10000));
        Assert.Equal(0, process.ExitCode);
    }
}
