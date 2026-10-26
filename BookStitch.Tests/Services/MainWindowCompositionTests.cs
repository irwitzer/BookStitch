using BookStitch.Services;
using System.Reflection;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainWindowCompositionTests
{
    [Theory]
    [InlineData(CompositionKind.Foundation)]
    [InlineData(CompositionKind.Media)]
    [InlineData(CompositionKind.Tracks)]
    [InlineData(CompositionKind.Discs)]
    [InlineData(CompositionKind.Status)]
    [InlineData(CompositionKind.Pipeline)]
    [InlineData(CompositionKind.Services)]
    public void Create_InitializesEveryRequiredComponent(CompositionKind kind)
    {
        var composition = CreateComposition(kind);
        var properties = composition.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var missingComponents = properties
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.GetValue(composition) is null)
            .Select(property => $"{composition.GetType().Name}.{property.Name}")
            .ToArray();

        Assert.NotEmpty(properties);
        Assert.True(
            missingComponents.Length == 0,
            $"Required composition components were not initialized: {string.Join(", ", missingComponents)}");
    }

    [Fact]
    public void LiveConversionQueueFactory_CreatesFreshQueuePerPipelineRun()
    {
        var composition = MainWindowPipelineComposition.Create();

        var firstQueue = composition.LiveConversionQueueFactory();
        var secondQueue = composition.LiveConversionQueueFactory();

        Assert.NotNull(firstQueue);
        Assert.NotNull(secondQueue);
        Assert.NotSame(firstQueue, secondQueue);
    }

    [Fact]
    public void ServiceComposition_Create_RejectsNullDelegates()
    {
        Assert.Throws<ArgumentNullException>(
            () => MainWindowServiceComposition.Create(CreateEnvironmentContext(), null!, () => false));
        Assert.Throws<ArgumentNullException>(
            () => MainWindowServiceComposition.Create(CreateEnvironmentContext(), () => string.Empty, null!));
    }

    private static object CreateComposition(CompositionKind kind) => kind switch
    {
        CompositionKind.Foundation => MainWindowFoundationComposition.Create(),
        CompositionKind.Media => MainWindowMediaComposition.Create(),
        CompositionKind.Tracks => MainWindowTrackComposition.Create(),
        CompositionKind.Discs => MainWindowDiscComposition.Create(),
        CompositionKind.Status => MainWindowStatusComposition.Create(),
        CompositionKind.Pipeline => MainWindowPipelineComposition.Create(),
        CompositionKind.Services => MainWindowServiceComposition.Create(
            CreateEnvironmentContext(),
            () => string.Empty,
            () => false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static BookStitchEnvironmentContext CreateEnvironmentContext() =>
        new(BookStitchEnvironment.Production, null, Path.GetTempPath(), Path.GetTempPath());

    public enum CompositionKind
    {
        Foundation,
        Media,
        Tracks,
        Discs,
        Status,
        Pipeline,
        Services
    }
}
