using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LoadedProjectStateSnapshotTests
{
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    public void HasLoadedProject_ReflectsAnyLoadedProjectState(
        bool needsDiscImport,
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal,
        bool expected)
    {
        var snapshot = new LoadedProjectStateSnapshot(
            needsDiscImport,
            isMp3Disc,
            isAudioDisc,
            isLocal);

        Assert.Equal(expected, snapshot.HasLoadedProject);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Constructor_RejectsMultipleProjectTypes(
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal)
    {
        Assert.Throws<ArgumentException>(
            () => new LoadedProjectStateSnapshot(
                NeedsDiscImport: false,
                IsMp3Disc: isMp3Disc,
                IsAudioDisc: isAudioDisc,
                IsLocal: isLocal));
    }

    [Fact]
    public void ToPipelineProjectState_PreservesExclusiveLoadedProjectType()
    {
        var snapshot = new LoadedProjectStateSnapshot(
            NeedsDiscImport: true,
            IsMp3Disc: false,
            IsAudioDisc: true,
            IsLocal: false);

        var result = snapshot.ToPipelineProjectState();

        Assert.True(result.NeedsDiscImport);
        Assert.False(result.IsMp3Disc);
        Assert.True(result.IsAudioDisc);
        Assert.False(result.IsLocal);
    }

    [Fact]
    public void None_ReturnsEmptyLoadedProjectState()
    {
        var result = LoadedProjectStateSnapshot.None;

        Assert.False(result.NeedsDiscImport);
        Assert.Equal(LoadedProjectKind.None, result.ProjectKind);
        Assert.False(result.IsMp3Disc);
        Assert.False(result.IsAudioDisc);
        Assert.False(result.IsLocal);
        Assert.False(result.HasLoadedProject);
    }

    [Theory]
    [InlineData(true, false, false, LoadedProjectKind.Mp3Disc)]
    [InlineData(false, true, false, LoadedProjectKind.AudioDisc)]
    [InlineData(false, false, true, LoadedProjectKind.Local)]
    [InlineData(false, false, false, LoadedProjectKind.None)]
    public void Create_MapsFlagsToExclusiveProjectKind(
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal,
        LoadedProjectKind expected)
    {
        var result = LoadedProjectStateSnapshot.Create(
            needsDiscImport: true,
            isMp3Disc,
            isAudioDisc,
            isLocal);

        Assert.True(result.NeedsDiscImport);
        Assert.Equal(expected, result.ProjectKind);
        Assert.Equal(expected == LoadedProjectKind.Mp3Disc, result.IsMp3Disc);
        Assert.Equal(expected == LoadedProjectKind.AudioDisc, result.IsAudioDisc);
        Assert.Equal(expected == LoadedProjectKind.Local, result.IsLocal);
    }

    [Fact]
    public void WithDiscImportRequirement_ChangesOnlyImportRequirement()
    {
        var original = LoadedProjectStateSnapshot.Create(
            needsDiscImport: false,
            isMp3Disc: false,
            isAudioDisc: true,
            isLocal: false);

        var result = original.WithDiscImportRequirement(true);

        Assert.True(result.NeedsDiscImport);
        Assert.Equal(LoadedProjectKind.AudioDisc, result.ProjectKind);
    }

    [Fact]
    public void ActivatingProjectType_ReplacesPreviousProjectType()
    {
        var original = LoadedProjectStateSnapshot.Create(
            needsDiscImport: true,
            isMp3Disc: false,
            isAudioDisc: true,
            isLocal: false);

        var result = original.WithLocalState(true);

        Assert.True(result.NeedsDiscImport);
        Assert.Equal(LoadedProjectKind.Local, result.ProjectKind);
        Assert.False(result.IsMp3Disc);
        Assert.False(result.IsAudioDisc);
        Assert.True(result.IsLocal);
    }

    [Fact]
    public void DeactivatingCurrentProjectType_ClearsProjectType()
    {
        var original = LoadedProjectStateSnapshot.Create(
            needsDiscImport: false,
            isMp3Disc: true,
            isAudioDisc: false,
            isLocal: false);

        var result = original.WithMp3DiscState(false);

        Assert.Equal(LoadedProjectKind.None, result.ProjectKind);
        Assert.False(result.HasLoadedProject);
    }

    [Fact]
    public void DeactivatingDifferentProjectType_PreservesCurrentProjectType()
    {
        var original = LoadedProjectStateSnapshot.Create(
            needsDiscImport: false,
            isMp3Disc: false,
            isAudioDisc: true,
            isLocal: false);

        var result = original.WithLocalState(false);

        Assert.Equal(LoadedProjectKind.AudioDisc, result.ProjectKind);
        Assert.True(result.IsAudioDisc);
    }
}
