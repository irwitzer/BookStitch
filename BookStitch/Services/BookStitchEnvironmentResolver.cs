using System.IO;

namespace BookStitch.Services;

public sealed class BookStitchEnvironmentResolver
{
    public const string RepositorySentinelFileName = "BookStitch.slnx";

    private readonly ProjectEnvironmentSelectionService? _selectionService;

    public BookStitchEnvironmentResolver(ProjectEnvironmentSelectionService? selectionService = null)
    {
        _selectionService = selectionService;
    }

    public BookStitchEnvironmentContext Resolve(
        string? startDirectory = null,
        string? appDataBaseFolder = null,
        string? musicFolder = null,
        string? userProfileFolder = null)
    {
        var repositoryRoot = FindRepositoryRoot(startDirectory ?? AppContext.BaseDirectory);
        var environment = BookStitchEnvironment.Production;
        if (repositoryRoot is not null)
        {
            var selectionService = _selectionService ?? new ProjectEnvironmentSelectionService(appDataBaseFolder);
            environment = selectionService.Load(repositoryRoot);
        }

        return new BookStitchEnvironmentContext(
            environment,
            repositoryRoot,
            GetAppDataRoot(environment, appDataBaseFolder),
            GetDefaultProjectRoot(environment, musicFolder, userProfileFolder));
    }

    public static string? FindRepositoryRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null;

        DirectoryInfo? directory;
        try
        {
            var fullPath = Path.GetFullPath(startDirectory);
            directory = File.Exists(fullPath)
                ? Directory.GetParent(fullPath)
                : new DirectoryInfo(fullPath);
        }
        catch
        {
            return null;
        }

        string? repositoryRoot = null;
        while (directory is { Exists: true })
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositorySentinelFileName)))
                repositoryRoot = directory.FullName;

            directory = directory.Parent;
        }

        return repositoryRoot;
    }

    public static string GetAppDataRoot(
        BookStitchEnvironment environment,
        string? appDataBaseFolder = null)
    {
        var appDataBase = string.IsNullOrWhiteSpace(appDataBaseFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appDataBaseFolder;

        return Path.Combine(appDataBase, GetEnvironmentDataFolderName(environment));
    }

    public static string GetDefaultProjectRoot(
        BookStitchEnvironment environment,
        string? musicFolder = null,
        string? userProfileFolder = null)
    {
        var resolvedMusicFolder = string.IsNullOrWhiteSpace(musicFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            : musicFolder;
        if (string.IsNullOrWhiteSpace(resolvedMusicFolder))
        {
            var profile = string.IsNullOrWhiteSpace(userProfileFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : userProfileFolder;
            resolvedMusicFolder = Path.Combine(profile, "Music");
        }

        return Path.Combine(resolvedMusicFolder, GetEnvironmentProjectFolderName(environment));
    }

    private static string GetEnvironmentDataFolderName(BookStitchEnvironment environment) =>
        environment switch
        {
            BookStitchEnvironment.Development => "BookStitch-Development",
            BookStitchEnvironment.XTester => "BookStitch-XTester",
            _ => "BookStitch"
        };

    private static string GetEnvironmentProjectFolderName(BookStitchEnvironment environment) =>
        environment switch
        {
            BookStitchEnvironment.Development => "BookStitchProjects-Development",
            BookStitchEnvironment.XTester => "BookStitchProjects-XTester",
            _ => "BookStitchProjects"
        };
}
