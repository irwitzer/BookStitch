using System.IO;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MetadataChapterDependencyArchitectureTests
{
    private static readonly string[][] MetadataPipelineFiles =
    [
        ["BookStitch", "Services", "ProjectMetadataInitializationService.cs"],
        ["BookStitch", "Services", "ProjectTitleSourceEvidenceService.cs"],
        ["BookStitch", "Services", "SourceMetadataAlternativeService.cs"],
        ["BookStitch", "Services", "MetadataNormalizationService.cs"]
    ];

    private static readonly string[] ForbiddenChapterDependencies =
    [
        "ChapterCandidateNormalizer",
        "ChapterCandidateSource",
        "ChapterCandidateProjectionService",
        "ChapterTechnicalNumberingAnalyzer",
        "ChapterResolveService",
        "ChapterAutoEngineService",
        "ChapterScopeBuilder",
        "ChapterAnalysis"
    ];

    [Fact]
    public void MetadataNormalizationEvidenceAndAggregation_DoNotDependOnChapterSpecificLogic()
    {
        foreach (var relativePath in MetadataPipelineFiles)
        {
            var text = LoadText(relativePath);

            foreach (var forbiddenDependency in ForbiddenChapterDependencies)
            {
                Assert.DoesNotContain(
                    forbiddenDependency,
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ProjectMetadataInitialization_DelegatesFilenameTitleEvidenceToMetadataService()
    {
        var initialization = LoadText("BookStitch", "Services", "ProjectMetadataInitializationService.cs");
        var titleEvidence = LoadText("BookStitch", "Services", "ProjectTitleSourceEvidenceService.cs");

        Assert.Contains("_projectTitleEvidence.EvaluateFilenameTitle(names)", initialization, StringComparison.Ordinal);
        Assert.Contains("BuildFilenameTitleCandidateGroups", titleEvidence, StringComparison.Ordinal);
        Assert.Contains("Path.GetFileNameWithoutExtension", titleEvidence, StringComparison.Ordinal);
    }

    private static string LoadText(params string[] relativePathParts)
    {
        var path = FindRepositoryFile(relativePathParts);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var directory = new DirectoryInfo(root);

            while (directory is { Exists: true })
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePathParts));
    }

    private static IEnumerable<string> RepositorySearchRoots(
        [System.Runtime.CompilerServices.CallerFilePath] string testFilePath = "")
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        var sourceRoot = Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(sourceRoot))
            yield return sourceRoot;
    }
}
