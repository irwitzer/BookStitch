using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ProjectIndexItemTests
{
    [Theory]
    [InlineData(ProjectManifestTypes.FolderProject, "ORD-PRJ.")]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, "MP3-PRJ.")]
    [InlineData("Mp3Disc", "MP3-PRJ.")]
    [InlineData(ProjectManifestTypes.AudioCdProject, "AUDIO-PRJ.")]
    public void ListDisplayName_UsesCompactProjectTypePrefix(string projectType, string expectedPrefix)
    {
        var project = new ProjectIndexItem
        {
            ProjectType = projectType,
            DisplayName = "Testprojekt",
            CreatedUtc = new DateTime(2026, 7, 18, 12, 30, 0, DateTimeKind.Local)
        };

        Assert.StartsWith(expectedPrefix + " | Testprojekt | ", project.ListDisplayName);
        Assert.DoesNotContain("-Projekt", project.ListDisplayName);
    }
}
