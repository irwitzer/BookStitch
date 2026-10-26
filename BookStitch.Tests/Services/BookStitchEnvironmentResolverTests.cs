using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class BookStitchEnvironmentResolverTests
{
    [Fact]
    public void Resolve_RepositoryWithoutPersistedSelection_ReturnsProduction()
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        var resolver = new BookStitchEnvironmentResolver(new ProjectEnvironmentSelectionService(appData.Path));

        var context = resolver.Resolve(folder.Path, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.Production, context.Environment);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production)]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Resolve_RepositoryUsesPersistedSelection(BookStitchEnvironment environment)
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        var selection = new ProjectEnvironmentSelectionService(appData.Path);
        selection.Save(folder.Path, environment);
        var resolver = new BookStitchEnvironmentResolver(selection);

        var context = resolver.Resolve(folder.Path, appData.Path, "Music", "User");

        Assert.Equal(environment, context.Environment);
    }

    [Fact]
    public void Resolve_MarkerFilesHaveNoInfluence()
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        File.WriteAllText(Path.Combine(folder.Path, "_ENVIRONMENT_Development"), string.Empty);
        File.WriteAllText(Path.Combine(folder.Path, "_ENVIRONMENT_Broken"), string.Empty);
        var resolver = new BookStitchEnvironmentResolver(new ProjectEnvironmentSelectionService(appData.Path));

        var context = resolver.Resolve(folder.Path, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.Production, context.Environment);
    }

    [Fact]
    public void Resolve_WithoutRepositoryAlwaysReturnsProductionDespitePersistedOtherRepository()
    {
        using var appData = new TemporaryFolder();
        using var repository = CreateRepository();
        using var installedFolder = new TemporaryFolder();
        var selection = new ProjectEnvironmentSelectionService(appData.Path);
        selection.Save(repository.Path, BookStitchEnvironment.XTester);
        var resolver = new BookStitchEnvironmentResolver(selection);

        var context = resolver.Resolve(installedFolder.Path, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.Production, context.Environment);
        Assert.Null(context.RepositoryRoot);
    }

    [Fact]
    public void Resolve_CorruptSelectionFallsBackToProductionWithoutStartupFailure()
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        var selection = new ProjectEnvironmentSelectionService(appData.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(selection.FilePath)!);
        File.WriteAllText(selection.FilePath, "{ broken");
        var resolver = new BookStitchEnvironmentResolver(selection);

        var context = resolver.Resolve(folder.Path, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.Production, context.Environment);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, "BookStitch")]
    [InlineData(BookStitchEnvironment.Development, "BookStitch-Development")]
    [InlineData(BookStitchEnvironment.XTester, "BookStitch-XTester")]
    public void GetAppDataRoot_UsesEnvironmentSpecificFolder(
        BookStitchEnvironment environment,
        string expectedFolder)
    {
        var result = BookStitchEnvironmentResolver.GetAppDataRoot(environment, @"C:\Users\Test\AppData\Roaming");

        Assert.Equal(Path.Combine(@"C:\Users\Test\AppData\Roaming", expectedFolder), result);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, "BookStitchProjects")]
    [InlineData(BookStitchEnvironment.Development, "BookStitchProjects-Development")]
    [InlineData(BookStitchEnvironment.XTester, "BookStitchProjects-XTester")]
    public void GetDefaultProjectRoot_UsesEnvironmentSpecificFolder(
        BookStitchEnvironment environment,
        string expectedFolder)
    {
        var result = BookStitchEnvironmentResolver.GetDefaultProjectRoot(environment, @"C:\Users\Test\Music");

        Assert.Equal(Path.Combine(@"C:\Users\Test\Music", expectedFolder), result);
    }

    [Fact]
    public void Resolve_FindsPersistedEnvironmentWhenStartedFromDeepBinPath()
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        var selection = new ProjectEnvironmentSelectionService(appData.Path);
        selection.Save(folder.Path, BookStitchEnvironment.Development);
        var binPath = Path.Combine(folder.Path, "BookStitch", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(binPath);
        var resolver = new BookStitchEnvironmentResolver(selection);

        var context = resolver.Resolve(binPath, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.Development, context.Environment);
        Assert.Equal(folder.Path, context.RepositoryRoot);
    }

    [Fact]
    public void Resolve_UsesOutermostRepositoryRootWhenNestedSolutionSentinelExists()
    {
        using var appData = new TemporaryFolder();
        using var folder = CreateRepository();
        var projectPath = Path.Combine(folder.Path, "BookStitch");
        var binPath = Path.Combine(projectPath, "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(binPath);
        File.WriteAllText(Path.Combine(projectPath, BookStitchEnvironmentResolver.RepositorySentinelFileName), string.Empty);
        var selection = new ProjectEnvironmentSelectionService(appData.Path);
        selection.Save(folder.Path, BookStitchEnvironment.XTester);
        var resolver = new BookStitchEnvironmentResolver(selection);

        var context = resolver.Resolve(binPath, appData.Path, "Music", "User");

        Assert.Equal(BookStitchEnvironment.XTester, context.Environment);
        Assert.Equal(folder.Path, context.RepositoryRoot);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, "BookStitch", "BookStitch", "Standard")]
    [InlineData(BookStitchEnvironment.Development, "BookStitch [Development]", "BookDevel.", "Development")]
    [InlineData(BookStitchEnvironment.XTester, "BookStitch [Tester]", "BookTester", "Tester")]
    public void PresentationProperties_UseVisibleEnvironmentContract(
        BookStitchEnvironment environment,
        string expectedTitle,
        string expectedBrand,
        string expectedDisplayName)
    {
        var context = new BookStitchEnvironmentContext(environment, null, "AppData", "Projects");

        Assert.Equal(expectedTitle, context.WindowTitle);
        Assert.Equal(expectedBrand, context.BrandText);
        Assert.Equal(expectedDisplayName, context.DisplayName);
    }

    private static TemporaryFolder CreateRepository()
    {
        var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, BookStitchEnvironmentResolver.RepositorySentinelFileName), string.Empty);
        return folder;
    }
}
