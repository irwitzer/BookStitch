using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class Mp3DiscPreparationServiceTests
{
    [Fact]
    public void BuildMissingPresetPreparationItems_ReturnsOnlyTracksThatNeedPreparation()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        var convertedFolder = System.IO.Path.Combine(projectFolder, "converted", "aac_stereo_128k");
        System.IO.Directory.CreateDirectory(convertedFolder);

        var firstSource = CreateNonEmptyFile(projectFolder, "CD 01", "Track 01.mp3");
        var secondSource = CreateNonEmptyFile(projectFolder, "CD 01", "Track 02.mp3");
        var missingSource = System.IO.Path.Combine(projectFolder, "CD 01", "Track 03.mp3");

        var tracks = new[]
        {
            CreateTrack("CD 01", "Track 01.mp3", 1),
            CreateTrack("CD 01", "Track 02.mp3", 2),
            CreateTrack("CD 01", "Track 03.mp3", 3)
        };

        var reusedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, firstSource, tracks[0]);
        CreateNonEmptyFileAbsolute(reusedConvertedPath);

        var service = new Mp3DiscPreparationService();

        var result = service.BuildMissingPresetPreparationItems(
            tracks,
            projectFolder,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (sourcePath, convertedPath) =>
                sourcePath == firstSource &&
                convertedPath == reusedConvertedPath);

        var item = Assert.Single(result);
        Assert.Equal(1, item.Index);
        Assert.Same(tracks[1], item.Track);
        Assert.Equal(secondSource, item.SourcePath);
        Assert.Equal(ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, secondSource, tracks[1]), item.ConvertedPath);
        Assert.DoesNotContain(result, candidate => TrackPathService.PathEquals(candidate.SourcePath, missingSource));
    }

    [Fact]
    public void BuildMissingPresetPreparationItems_UsesStableConvertedPathForPresetFolder()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        var convertedFolder = System.IO.Path.Combine(projectFolder, "converted", "aac_mono_96k");
        var sourcePath = CreateNonEmptyFile(projectFolder, "CD 02", "005 - Kapitel.mp3");
        var track = CreateTrack("CD 02", "005 - Kapitel.mp3", 5);
        var service = new Mp3DiscPreparationService();

        var result = service.BuildMissingPresetPreparationItems(
            new[] { track },
            projectFolder,
            ExportPreset.Parse("AAC Mono 96 kbps"),
            convertedFolder,
            (_, _) => false);

        var item = Assert.Single(result);
        Assert.Equal(convertedFolder, System.IO.Path.GetDirectoryName(item.ConvertedPath));
        Assert.EndsWith(".m4a", item.ConvertedPath, StringComparison.OrdinalIgnoreCase);
        var expectedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
        Assert.Equal(expectedPath, item.ConvertedPath);
    }

    [Theory]
    [InlineData("Track 01.mp3", "mp3", "MP3")]
    [InlineData("Track 01.m4a", "m4a", "")]
    [InlineData("Track 01.wav", "wav", "")]
    public void BuildLiveConversionTrack_CreatesTrackFromCopiedFile(string fileName, string expectedExtension, string expectedCodec)
    {
        var service = new Mp3DiscPreparationService();
        var importedFile = System.IO.Path.Combine("D:\\BookStitch", "Project", "CD 03", fileName);
        var copiedFile = new DiscCopiedFile(
            DiscNumber: 3,
            TotalDiscs: 5,
            CopiedFiles: 7,
            TotalFiles: 12,
            SourceFile: System.IO.Path.Combine("E:\\", fileName),
            ImportedFile: importedFile);

        var result = service.BuildLiveConversionTrack(copiedFile);

        Assert.Equal(7, result.Index);
        Assert.Equal(3, result.DiscNumber);
        Assert.Equal(7, result.TrackNumber);
        Assert.Equal(importedFile, result.FilePath);
        Assert.Equal(fileName, result.FileName);
        Assert.Equal(expectedExtension, result.Extension);
        Assert.Equal(expectedCodec, result.Codec);
        Assert.Equal("Konvertieren", result.ProcessingAction);
    }

    private static TrackInfo CreateTrack(string relativeFolder, string fileName, int trackNumber)
    {
        return new TrackInfo
        {
            Index = trackNumber,
            DiscNumber = 1,
            TrackNumber = trackNumber,
            RelativeFolder = relativeFolder,
            FileName = fileName,
            Extension = System.IO.Path.GetExtension(fileName).TrimStart('.'),
            ProcessingAction = "Konvertieren"
        };
    }

    private static string CreateNonEmptyFile(string root, params string[] relativePathParts)
    {
        var parts = new string[relativePathParts.Length + 1];
        parts[0] = root;
        Array.Copy(relativePathParts, 0, parts, 1, relativePathParts.Length);
        var fullPath = System.IO.Path.Combine(parts);
        return CreateNonEmptyFileAbsolute(fullPath);
    }

    private static string CreateNonEmptyFileAbsolute(string fullPath)
    {
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        System.IO.File.WriteAllText(fullPath, "audio");
        return fullPath;
    }
}
