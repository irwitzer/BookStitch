using System.Text.Json;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectEnvironmentSelectionServiceTests
{
    [Fact]
    public void FilePath_UsesEnvironmentIndependentBootstrapLocation()
    {
        using var appData = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);

        Assert.Equal(
            Path.Combine(appData.Path, "BookStitch", "developer-tools", "project-environment.json"),
            service.FilePath);
    }

    [Fact]
    public void MissingFile_ReturnsProduction()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();

        var service = new ProjectEnvironmentSelectionService(appData.Path);

        Assert.Equal(BookStitchEnvironment.Production, service.Load(repository.Path));
    }

    [Fact]
    public void SaveAndLoad_PersistsSelectionAtomically()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);

        service.Save(repository.Path, BookStitchEnvironment.Development);

        Assert.True(File.Exists(service.FilePath));
        Assert.Equal(BookStitchEnvironment.Development, service.Load(repository.Path));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(service.FilePath)!, "*.tmp"));
    }

    [Fact]
    public void MultipleRepositories_RemainIndependent()
    {
        using var appData = new TemporaryFolder();
        using var first = new TemporaryFolder();
        using var second = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);

        service.Save(first.Path, BookStitchEnvironment.Development);
        service.Save(second.Path, BookStitchEnvironment.XTester);

        Assert.Equal(BookStitchEnvironment.Development, service.Load(first.Path));
        Assert.Equal(BookStitchEnvironment.XTester, service.Load(second.Path));
    }

    [Fact]
    public void SavingSameNormalizedRepository_ReplacesEntryInsteadOfDuplicatingIt()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);

        service.Save(repository.Path + Path.DirectorySeparatorChar, BookStitchEnvironment.Development);
        service.Save(repository.Path, BookStitchEnvironment.XTester);

        using var json = JsonDocument.Parse(File.ReadAllText(service.FilePath));
        Assert.Single(json.RootElement.GetProperty("Repositories").EnumerateArray());
        Assert.Equal(BookStitchEnvironment.XTester, service.Load(repository.Path));
    }

    [Fact]
    public void CorruptJson_ReturnsProduction()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(service.FilePath)!);
        File.WriteAllText(service.FilePath, "{ broken");

        Assert.Equal(BookStitchEnvironment.Production, service.Load(repository.Path));
    }

    [Fact]
    public void UnknownEnvironment_ReturnsProduction()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(service.FilePath)!);
        File.WriteAllText(
            service.FilePath,
            $$"""{"Repositories":[{"RepositoryRoot":{{JsonSerializer.Serialize(repository.Path)}},"Environment":"Broken"}]}""");

        Assert.Equal(BookStitchEnvironment.Production, service.Load(repository.Path));
    }

    [Fact]
    public void Save_DoesNotTouchEnvironmentSpecificSettingsLocationFiles()
    {
        using var appData = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        var service = new ProjectEnvironmentSelectionService(appData.Path);
        var productionLocation = Path.Combine(appData.Path, "BookStitch", "settings-location.json");
        var developmentLocation = Path.Combine(appData.Path, "BookStitch-Development", "settings-location.json");
        Directory.CreateDirectory(Path.GetDirectoryName(productionLocation)!);
        Directory.CreateDirectory(Path.GetDirectoryName(developmentLocation)!);
        File.WriteAllText(productionLocation, "production");
        File.WriteAllText(developmentLocation, "development");

        service.Save(repository.Path, BookStitchEnvironment.XTester);

        Assert.Equal("production", File.ReadAllText(productionLocation));
        Assert.Equal("development", File.ReadAllText(developmentLocation));
    }
}
