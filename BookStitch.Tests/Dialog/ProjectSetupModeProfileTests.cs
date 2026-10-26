using BookStitch.Dialog;
using Xunit;

namespace BookStitch.Tests.Dialog;

public sealed class ProjectSetupModeProfileTests
{
    [Theory]
    [InlineData(ProjectSetupSourceKind.Folder, false, true)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, true, false)]
    [InlineData(ProjectSetupSourceKind.AudioDisc, true, false)]
    public void Source_kind_derives_the_single_setup_presentation_profile(
        ProjectSetupSourceKind sourceKind,
        bool showsDiscCount,
        bool showsProjectFolder)
    {
        var profile = ProjectSetupModeProfile.FromSourceKind(sourceKind);

        Assert.Equal(showsDiscCount, profile.ShowsDiscCount);
        Assert.Equal(showsProjectFolder, profile.ShowsProjectFolder);
        Assert.Equal(showsDiscCount, profile.UsesDiscPresentation);
    }
}
