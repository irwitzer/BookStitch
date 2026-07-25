using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportPlanServiceTests
{
    private readonly ExportPlanService _service = new();

    [Fact]
    public void Create_ForFolderProject_BuildsExpectedWorkAndMergePaths()
    {
        var tracks = new[]
        {
            new TrackInfo { FileName = "001.mp3", Duration = "00:01:00" },
            new TrackInfo { FileName = "002.mp3", Duration = "00:02:00" }
        };

        var plan = _service.Create(new ExportPlanRequest(
            tracks,
            @"C:\Hoerbuecher\Stephen King\Der Anschlag",
            @"D:\BookStitch\Work",
            @"E:\Export\Stephen King\Der Anschlag.m4a",
            "AAC Mono 64 kbps",
            8,
            Author: "Stephen King",
            BookTitle: "Der Anschlag"));

        Assert.Equal("AAC Mono 64 kbps", plan.Preset.DisplayName);
        Assert.Equal("aac_mono_64k", plan.PresetFolder);
        Assert.Equal(8, plan.ParallelConversions);
        Assert.Equal(TimeSpan.FromMinutes(3), plan.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(3).Ticks, plan.TotalTicks);
        Assert.Equal(ProjectManifestTypes.FolderProject, plan.ProjectType);
        Assert.EndsWith(System.IO.Path.Combine("converted", "aac_mono_64k"), plan.ConvertedFolder);
        Assert.EndsWith(System.IO.Path.Combine("merge", "concat-list.txt"), plan.ConcatListPath);
        Assert.EndsWith(System.IO.Path.Combine("merge", "chapters.ffmetadata"), plan.ChapterMetadataPath);
        Assert.EndsWith(System.IO.Path.Combine(ProjectFolderLayout.SettingsFolderName, ProjectFolderLayout.WorkManifestFileName), plan.ManifestPath);
        Assert.EndsWith(".part.m4a", plan.FinalPartPath);
        Assert.Equal(@"E:\Export\Stephen King", plan.FinalOutputFolder);
        Assert.Equal("Der Anschlag.m4a", plan.FinalOutputFileName);
    }

    [Fact]
    public void Create_WithProjectWorkFolderOverride_UsesOverrideAndMp3DiscManifestName()
    {
        var projectFolder = @"D:\BookStitch\Work\DiscProjects\Projekt_001";

        var plan = _service.Create(new ExportPlanRequest(
            Array.Empty<TrackInfo>(),
            @"D:\BookStitch\Work\DiscProjects\Projekt_001",
            @"D:\BookStitch\Work",
            @"E:\Export\Hoerbuch.m4b",
            "AAC Stereo 128 kbps",
            99,
            ProjectWorkFolderOverride: projectFolder,
            ProjectType: ProjectManifestTypes.Mp3DiscProject));

        Assert.Equal(projectFolder, plan.ProjectWorkFolder);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, plan.ProjectType);
        Assert.Equal(40, plan.ParallelConversions);
        Assert.Equal("aac_stereo_128k", plan.PresetFolder);
        Assert.Equal(ProjectFolderLayout.GetExportManifestPath(projectFolder), plan.ManifestPath);
        Assert.EndsWith(".part.m4b", plan.FinalPartPath);
    }

    [Fact]
    public void BuildProjectWorkFolderName_WithMissingMetadata_UsesSourceFolderNameAndStableHash()
    {
        var first = _service.BuildProjectWorkFolderName(@"C:\Quelle\Mein Buch", "", "");
        var second = _service.BuildProjectWorkFolderName(@"C:\Quelle\Mein Buch", null, null);

        Assert.Equal(first, second);
        Assert.StartsWith("Mein Buch_", first);
        Assert.Matches(@"_[a-f0-9]{8}$", first);
    }

    [Fact]
    public void BuildProjectWorkFolderName_WithMetadata_CleansInvalidCharacters()
    {
        var result = _service.BuildProjectWorkFolderName(@"C:\Quelle\Buch", "Au:tor", "Ti*tel");

        Assert.StartsWith("Au tor - Ti tel_", result);
    }

    [Fact]
    public void Create_PrefersDurationTicksForTotalDuration()
    {
        var tracks = new[]
        {
            new TrackInfo { FileName = "001.mp3", Duration = "00:01", DurationTicks = TimeSpan.FromMilliseconds(1750).Ticks },
            new TrackInfo { FileName = "002.mp3", Duration = "00:02", DurationTicks = TimeSpan.FromMilliseconds(2250).Ticks }
        };

        var plan = _service.Create(new ExportPlanRequest(
            tracks,
            @"C:\Quelle\Buch",
            @"D:\BookStitch\Work",
            @"E:\Export\Buch.m4a",
            "AAC Mono 64 kbps",
            2));

        Assert.Equal(TimeSpan.FromSeconds(4), plan.TotalDuration);
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, plan.TotalTicks);
    }
}
