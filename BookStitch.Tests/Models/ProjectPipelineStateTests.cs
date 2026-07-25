using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ProjectPipelineStateTests
{
    [Theory]
    [InlineData("Created", false, false, ProjectPipelineState.Preparing)]
    [InlineData("Importing", false, false, ProjectPipelineState.AcquiringSources)]
    [InlineData("Exporting", false, true, ProjectPipelineState.Converting)]
    [InlineData("Ready", false, true, ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData("PausedBeforeMerge", false, true, ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData("Completed", true, true, ProjectPipelineState.Completed)]
    [InlineData("Canceled", false, true, ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData("Failed", true, true, ProjectPipelineState.Completed)]
    public void FromManifestValue_MapsLegacyStates(
        string value,
        bool hasSuccessfulExport,
        bool sourcesComplete,
        ProjectPipelineState expected)
    {
        var state = ProjectPipelineStateNames.FromManifestValue(
            value,
            hasSuccessfulExport,
            sourcesComplete);

        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData(ProjectPipelineState.Preparing)]
    [InlineData(ProjectPipelineState.AcquiringSources)]
    [InlineData(ProjectPipelineState.Converting)]
    [InlineData(ProjectPipelineState.ReviewBeforeMerge)]
    [InlineData(ProjectPipelineState.Merging)]
    [InlineData(ProjectPipelineState.Completed)]
    public void ManifestRoundTrip_PreservesSixStates(ProjectPipelineState state)
    {
        var restored = ProjectPipelineStateNames.FromManifestValue(state.ToManifestValue());

        Assert.Equal(state, restored);
    }
}
