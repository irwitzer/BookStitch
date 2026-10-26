using System.Text.RegularExpressions;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AP17LegacyBoundaryTests
{
    private static readonly string RepositoryRoot = ResolveRepositoryRoot();

    [Fact]
    public void ObsoleteBookMetadataRescanService_RemainsRemoved()
    {
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot,
            "BookStitch",
            "Services",
            "BookMetadataService.cs")));

        var production = LoadProductionSources();
        Assert.DoesNotContain(
            production,
            source => source.Text.Contains("BookMetadataService", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyBookMetadataScalars_AreMigrationInputOnly()
    {
        var production = LoadProductionSources();

        var exportMetadataScalarPattern = new Regex(
            @"\.Metadata\.(?:Title|Author|Album|Narrator|Genre)\b",
            RegexOptions.CultureInvariant);

        var exportScalarReaders = production
            .Where(source => exportMetadataScalarPattern.IsMatch(source.Text))
            .Select(source => source.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            ["Services/ProjectMetadataMigrationService.cs"],
            exportScalarReaders);

        var directLegacyManifestPattern = new Regex(
            @"\b(?:audioDiscManifest|mp3DiscManifest|exportManifest)\??\.(?:Title|Author|Album|Narrator|Genre)\b",
            RegexOptions.CultureInvariant);

        var directLegacyReaders = production
            .Where(source => directLegacyManifestPattern.IsMatch(source.Text))
            .Select(source => source.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            ["Services/ProjectMetadataMigrationService.cs"],
            directLegacyReaders);
    }

    [Fact]
    public void ProductiveServices_DoNotWriteLegacyBookMetadataMirrors()
    {
        AssertNoLegacyScalarAssignments(
            "Services/ProjectSnapshotService.cs",
            "manifest.Title",
            "manifest.Author",
            "manifest.Album",
            "manifest.Narrator",
            "manifest.Genre",
            "manifest.Metadata.Title",
            "manifest.Metadata.Author",
            "manifest.Metadata.Album",
            "manifest.Metadata.Narrator",
            "manifest.Metadata.Genre");

        AssertNoLegacyScalarAssignments(
            "Services/WorkManifestService.cs",
            "manifest.Metadata.Title",
            "manifest.Metadata.Author",
            "manifest.Metadata.Album",
            "manifest.Metadata.Narrator",
            "manifest.Metadata.Genre");

        AssertNoLegacyScalarAssignments(
            "Services/AudioDiscProjectService.cs",
            "manifest.Title",
            "manifest.Author",
            "manifest.Album",
            "manifest.Narrator",
            "manifest.Genre");
    }

    [Fact]
    public void GeneratedChapterTitles_AreNotPersistedByNormalWorkManifestWriters()
    {
        var workManifest = LoadProductionSource("Services/WorkManifestService.cs");
        var sourceCapture = LoadProductionSource("Services/SourceMetadataCaptureService.cs");
        var resume = LoadProductionSource("Services/ProjectResumePlanService.cs");

        Assert.DoesNotContain("ChapterTitle", workManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("ChapterTitle =", sourceCapture, StringComparison.Ordinal);

        // Old manifests may still provide chapter presentation as a one-way resume fallback.
        Assert.Contains("manifestTrack?.ChapterTitle", resume, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceTrackDiscEvidence_CannotFlowIntoOutputTrackFields()
    {
        var outputProjection = LoadProductionSource("Services/ProjectMetadataOutputProjectionService.cs");
        var finalTags = LoadProductionSource("Services/FinalTagService.cs");
        var sourceCapture = LoadProductionSource("Services/SourceMetadataCaptureService.cs");

        Assert.Contains("ProjectMetadataField.OutputTrackNumber", outputProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataField.OutputTrackTotal", outputProjection, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataField.OutputTrackNumber", finalTags, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataField.OutputTrackTotal", finalTags, StringComparison.Ordinal);

        Assert.DoesNotContain("SourceTrackNumber", outputProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceTrackTotal", outputProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceDiscNumber", outputProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceDiscTotal", outputProjection, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceTrackNumber", finalTags, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceTrackTotal", finalTags, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceDiscNumber", finalTags, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceDiscTotal", finalTags, StringComparison.Ordinal);

        Assert.DoesNotContain("OutputTrackNumber", sourceCapture, StringComparison.Ordinal);
        Assert.DoesNotContain("OutputTrackTotal", sourceCapture, StringComparison.Ordinal);
    }

    [Fact]
    public void RemainingMainWindowScalarProjection_UsesCanonicalSessionNotSecondPersistencePath()
    {
        var mainWindow = LoadProductionSource("MainWindow.xaml.cs");

        Assert.DoesNotContain("BookMetadataService", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_projectMetadata.ApplyCanonicalMetadata(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataSession", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectMetadataStore", mainWindow, StringComparison.Ordinal);
    }

    private static void AssertNoLegacyScalarAssignments(
        string relativePath,
        params string[] memberExpressions)
    {
        var source = LoadProductionSource(relativePath);
        foreach (var memberExpression in memberExpressions)
        {
            var pattern = new Regex(
                Regex.Escape(memberExpression) + @"\s*=(?!=)",
                RegexOptions.CultureInvariant);

            Assert.False(
                pattern.IsMatch(source),
                $"Legacy metadata mirror assignment remains in {relativePath}: {memberExpression}");
        }
    }

    private static IReadOnlyList<ProductionSource> LoadProductionSources()
    {
        var projectRoot = Path.Combine(RepositoryRoot, "BookStitch");
        return Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ContainsBuildFolder(path))
            .Select(path => new ProductionSource(
                Path.GetRelativePath(projectRoot, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToArray();
    }

    private static string LoadProductionSource(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "BookStitch",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static bool ContainsBuildFolder(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.artifacts/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string testFilePath = "")
    {
        return Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved.");
    }

    private sealed record ProductionSource(string RelativePath, string Text);
}