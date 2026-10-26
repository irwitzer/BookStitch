using System.IO;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class ProjectEnvironmentSelectionService
{
    public const string DeveloperToolsFolderName = "developer-tools";
    public const string FileName = "project-environment.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AtomicJsonStore _jsonStore;
    private readonly StringComparer _repositoryComparer;

    public ProjectEnvironmentSelectionService(
        string? appDataBaseFolder = null,
        AtomicJsonStore? jsonStore = null)
    {
        var appDataBase = string.IsNullOrWhiteSpace(appDataBaseFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appDataBaseFolder;

        FilePath = Path.Combine(appDataBase, "BookStitch", DeveloperToolsFolderName, FileName);
        _jsonStore = jsonStore ?? new AtomicJsonStore();
        _repositoryComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    public string FilePath { get; }

    public BookStitchEnvironment Load(string repositoryRoot)
    {
        string normalizedRepositoryRoot;
        try
        {
            normalizedRepositoryRoot = NormalizeRepositoryRoot(repositoryRoot);
        }
        catch
        {
            return BookStitchEnvironment.Production;
        }

        var document = TryLoadDocument();
        if (document is null)
            return BookStitchEnvironment.Production;

        foreach (var entry in document.Repositories ?? [])
        {
            string normalizedEntryRoot;
            try
            {
                normalizedEntryRoot = NormalizeRepositoryRoot(entry.RepositoryRoot);
            }
            catch
            {
                continue;
            }

            if (!_repositoryComparer.Equals(normalizedRepositoryRoot, normalizedEntryRoot))
                continue;

            return Enum.TryParse<BookStitchEnvironment>(entry.Environment, ignoreCase: false, out var environment) &&
                   IsSupported(environment)
                ? environment
                : BookStitchEnvironment.Production;
        }

        return BookStitchEnvironment.Production;
    }

    public void Save(string repositoryRoot, BookStitchEnvironment environment)
    {
        if (!IsSupported(environment))
            throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported BookStitch environment.");

        var normalizedRepositoryRoot = NormalizeRepositoryRoot(repositoryRoot);
        var document = TryLoadDocument() ?? new ProjectEnvironmentSelectionDocument();
        document.Repositories ??= [];

        var retainedEntries = new List<ProjectEnvironmentSelectionEntry>();
        foreach (var entry in document.Repositories)
        {
            string? normalizedEntryRoot = null;
            try
            {
                normalizedEntryRoot = NormalizeRepositoryRoot(entry.RepositoryRoot);
            }
            catch
            {
                // Preserve unrelated malformed entries instead of silently deleting user data.
            }

            if (normalizedEntryRoot is not null &&
                _repositoryComparer.Equals(normalizedRepositoryRoot, normalizedEntryRoot))
            {
                continue;
            }

            retainedEntries.Add(entry);
        }

        retainedEntries.Add(new ProjectEnvironmentSelectionEntry
        {
            RepositoryRoot = normalizedRepositoryRoot,
            Environment = environment.ToString()
        });
        document.Repositories = retainedEntries;

        _jsonStore.Save(FilePath, document, JsonOptions);
    }

    public static string NormalizeRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var fullPath = Path.GetFullPath(repositoryRoot);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        while (fullPath.Length > root.Length &&
               (fullPath.EndsWith(Path.DirectorySeparatorChar) ||
                fullPath.EndsWith(Path.AltDirectorySeparatorChar)))
        {
            fullPath = fullPath[..^1];
        }

        return fullPath;
    }

    private ProjectEnvironmentSelectionDocument? TryLoadDocument()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ProjectEnvironmentSelectionDocument();

            return JsonSerializer.Deserialize<ProjectEnvironmentSelectionDocument>(
                File.ReadAllText(FilePath, Encoding.UTF8),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSupported(BookStitchEnvironment environment) =>
        environment is BookStitchEnvironment.Production or
            BookStitchEnvironment.Development or
            BookStitchEnvironment.XTester;

    private sealed class ProjectEnvironmentSelectionDocument
    {
        public List<ProjectEnvironmentSelectionEntry>? Repositories { get; set; } = [];
    }

    private sealed class ProjectEnvironmentSelectionEntry
    {
        public string RepositoryRoot { get; set; } = string.Empty;

        public string Environment { get; set; } = string.Empty;
    }
}
