using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ProjectIndexItemTests
{
    [Theory]
    [InlineData(false, ProjectHealthStatus.RebuildRequired, true)]
    [InlineData(false, ProjectHealthStatus.Damaged, true)]
    [InlineData(false, ProjectHealthStatus.NotChecked, false)]
    [InlineData(false, ProjectHealthStatus.Warning, false)]
    [InlineData(true, ProjectHealthStatus.OK, true)]
    public void Browser_visibility_includes_only_selectable_or_recovery_projects(bool selectable, ProjectHealthStatus health, bool visible)
    {
        Assert.Equal(visible, new ProjectIndexItem { IsSelectableProject = selectable, HealthStatus = health }.IsVisibleInProjectBrowser);
    }

    [Theory]
    [InlineData(ProjectManifestTypes.FolderProject)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData("Mp3Disc")]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void ListDisplayName_OmitsProjectTypePrefix(string projectType)
    {
        var project = CreateProject(projectType);

        Assert.DoesNotContain("PRJ.", project.ListDisplayName, StringComparison.Ordinal);
        Assert.Equal("Autor - Titel | 18.07.2026 12:30", project.ListDisplayPrefix);
        Assert.Equal("Exportbereit", project.ListDisplayState);
        Assert.Equal("Autor - Titel | 18.07.2026 12:30 | Exportbereit", project.ListDisplayName);
    }

    [Fact]
    public void ListDisplayName_TruncatesAuthorToExactly18CharactersIncludingEllipsis()
    {
        var project = CreateProject(ProjectManifestTypes.FolderProject);
        project.Author = "Gabriel García Márquez";

        var authorSegment = project.ListDisplayName[..18];

        Assert.Equal(18, authorSegment.Length);
        Assert.Equal("Gabriel García ...", authorSegment);
        Assert.StartsWith("Gabriel García ... - Titel |", project.ListDisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void ListDisplayName_DoesNotPadAuthorShorterThan18Characters()
    {
        var project = CreateProject(ProjectManifestTypes.FolderProject);
        project.Author = "John Green";

        Assert.StartsWith("John Green - Titel |", project.ListDisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("John Green  -", project.ListDisplayName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "In Arbeit")]
    [InlineData(false, true, "Exportbereit")]
    [InlineData(true, true, "Beendet")]
    public void ListDisplayName_UsesCompactThreeStateProjectStatus(
        bool isCompletedProject,
        bool isSelectableProject,
        string expectedState)
    {
        var project = CreateProject(ProjectManifestTypes.FolderProject);
        project.IsCompletedProject = isCompletedProject;
        project.IsSelectableProject = isSelectableProject;

        Assert.EndsWith(" | " + expectedState, project.ListDisplayName, StringComparison.Ordinal);
    }

    private static ProjectIndexItem CreateProject(string projectType)
    {
        return new ProjectIndexItem
        {
            ProjectType = projectType,
            DisplayName = "Autor - Titel",
            Author = "Autor",
            Title = "Titel",
            IsSelectableProject = true,
            CreatedUtc = new DateTime(2026, 7, 18, 12, 30, 0, DateTimeKind.Local)
        };
    }
}
