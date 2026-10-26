using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using TagLib;
using Xunit;
using IoFile = System.IO.File;
using TagLibFile = TagLib.File;

namespace BookStitch.Tests.Services;

public sealed class FinalTagServiceTests
{
    [Theory]
    [InlineData(".m4a")]
    [InlineData(".m4b")]
    [InlineData(".mp4")]
    [Trait("Category", "NeroChplPreservationGuard")]
    public void WriteFinalTags_PreservesNeroChaptersAcrossRepeatedMetadataUpdates(string extension)
    {
        using var folder = new TemporaryFolder();
        var source = FindRepositoryFile("BookStitch.Tests", "TestData", "Mp4NeroChapterFixture.m4b");
        var target = Path.Combine(folder.Path, "chapters" + extension);
        IoFile.Copy(source, target);
        var expected = ReadChapterListAtom(target);
        Assert.Contains("Start ÄÖÜ", System.Text.Encoding.UTF8.GetString(expected));
        Assert.Contains("Ende 東京", System.Text.Encoding.UTF8.GetString(expected));

        foreach (var title in new[] { "First metadata update", "Second update ÄÖÜ" })
        {
            new FinalTagService().WriteFinalTags(target, new FinalAudioTagData { Title = title });
            Assert.Equal(expected, ReadChapterListAtom(target));
            using var tagged = TagLibFile.Create(target);
            Assert.Equal(title, tagged.Tag.Title);
        }
    }

    private static byte[] ReadChapterListAtom(string path)
    {
        var bytes = IoFile.ReadAllBytes(path);
        var type = System.Text.Encoding.ASCII.GetBytes("chpl");
        for (var index = 4; index <= bytes.Length - type.Length; index++)
        {
            if (!bytes.AsSpan(index, type.Length).SequenceEqual(type))
                continue;
            var length = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index - 4, 4)));
            return bytes.AsSpan(index - 4, length).ToArray();
        }
        throw new InvalidDataException("Fixture chapter list is missing.");
    }

    private readonly FinalTagService _service = new();

    [Fact]
    public void WriteFinalTags_WritesCoreMetadataAndJpegCover()
    {
        using var folder = new TemporaryFolder();
        var audioPath = CopyAudioFixture(folder, "tagged.wav");
        var coverPath = WriteTinyJpeg(folder, "cover.jpg");

        _service.WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "  Final Title  ",
                Album = "  Final Album  ",
                Author = "  Final Author  ",
                Narrator = "  Final Narrator  ",
                Composers = ["  Composer A  ", "Composer B"],
                Genre = "  Hörbuch  ",
                CoverPath = coverPath
            });

        using var tagged = TagLibFile.Create(audioPath);
        Assert.Equal("Final Title", tagged.Tag.Title);
        Assert.Equal("Final Album", tagged.Tag.Album);
        Assert.Equal(["Final Author"], tagged.Tag.Performers);
        Assert.Equal(["Final Author"], tagged.Tag.AlbumArtists);
        Assert.Equal(["Composer A", "Composer B"], tagged.Tag.Composers);
        Assert.Equal(["Hörbuch"], tagged.Tag.Genres);
        Assert.Equal("Sprecher: Final Narrator", tagged.Tag.Comment);

        var picture = Assert.Single(tagged.Tag.Pictures);
        Assert.Equal(PictureType.FrontCover, picture.Type);
        Assert.Equal("image/jpeg", picture.MimeType);
        Assert.Equal("Cover", picture.Description);
    }

    [Fact]
    public void WriteFinalTags_WhenComposersOutputIsDisabled_ClearsComposerTagOnly()
    {
        using var folder = new TemporaryFolder();
        var audioPath = CopyAudioFixture(folder, "composer-disabled.wav");

        _service.WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "Book",
                Narrators = ["Narrator"],
                Composers = ["Composer A", "Composer B"],
                DisabledFields = new HashSet<ProjectMetadataField> { ProjectMetadataField.Composers }
            });

        using var tagged = TagLibFile.Create(audioPath);
        Assert.Empty(tagged.Tag.Composers);
        Assert.Equal("Sprecher: Narrator", tagged.Tag.Comment);
    }

    [Fact]
    public void WriteFinalTags_WhenExistingPicturesArePresent_ReplacesThemWithSingleFrontCover()
    {
        using var folder = new TemporaryFolder();
        var audioPath = CopyAudioFixture(folder, "pictures.wav");
        var coverPath = WriteTinyJpeg(folder, "cover.jpg");
        using (var tagged = TagLibFile.Create(audioPath))
        {
            tagged.Tag.Pictures =
            [
                new Picture(coverPath) { Type = PictureType.BackCover, MimeType = "image/jpeg" },
                new Picture(coverPath) { Type = PictureType.Other, MimeType = "image/jpeg" }
            ];
            tagged.Save();
        }

        _service.WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "Final",
                CoverPath = coverPath
            });

        using var result = TagLibFile.Create(audioPath);
        var picture = Assert.Single(result.Tag.Pictures);
        Assert.Equal(PictureType.FrontCover, picture.Type);
        Assert.Equal("image/jpeg", picture.MimeType);
        Assert.Equal("Cover", picture.Description);
    }

    [Fact]
    public void WriteFinalTags_UsesTitleAsAlbumFallbackAndIgnoresMissingOptionalValues()
    {
        using var folder = new TemporaryFolder();
        var audioPath = CopyAudioFixture(folder, "minimal.wav");

        _service.WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "Minimal Title",
                CoverPath = Path.Combine(folder.Path, "missing.jpg")
            });

        using var tagged = TagLibFile.Create(audioPath);
        Assert.Equal("Minimal Title", tagged.Tag.Title);
        Assert.Equal("Minimal Title", tagged.Tag.Album);
        Assert.Empty(tagged.Tag.Performers);
        Assert.Empty(tagged.Tag.AlbumArtists);
        Assert.Empty(tagged.Tag.Composers);
        Assert.True(string.IsNullOrEmpty(tagged.Tag.Comment));
        Assert.Empty(tagged.Tag.Pictures);
    }

    [Fact]
    public void WriteFinalTags_IgnoresExistingNonJpegCoverPath()
    {
        using var folder = new TemporaryFolder();
        var audioPath = CopyAudioFixture(folder, "no-cover.wav");
        var pngPath = folder.CreateFile("cover.png");

        _service.WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "No Cover",
                CoverPath = pngPath
            });

        using var tagged = TagLibFile.Create(audioPath);
        Assert.Equal("No Cover", tagged.Tag.Title);
        Assert.Empty(tagged.Tag.Pictures);
    }

    [Fact]
    public void WriteFinalTags_WhenAudioPathIsMissing_ThrowsFileNotFound()
    {
        using var folder = new TemporaryFolder();
        var missingPath = Path.Combine(folder.Path, "missing.m4b");

        var exception = Assert.Throws<FileNotFoundException>(() =>
            _service.WriteFinalTags(missingPath, new FinalAudioTagData { Title = "Missing" }));

        Assert.Equal(missingPath, exception.FileName);
    }

    [Fact]
    public void WriteFinalTags_WhenAudioPathIsBlank_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.WriteFinalTags("  ", new FinalAudioTagData()));
    }

    [Fact]
    public void WriteFinalTags_WhenTargetFileIsUnsupported_PropagatesTagLibFailureAndKeepsFile()
    {
        using var folder = new TemporaryFolder();
        var invalidPath = Path.Combine(folder.Path, "invalid.m4b");
        IoFile.WriteAllText(invalidPath, "not an audio container");
        var originalLength = new FileInfo(invalidPath).Length;

        var exception = Record.Exception(() =>
            _service.WriteFinalTags(invalidPath, new FinalAudioTagData { Title = "Invalid" }));

        Assert.NotNull(exception);
        Assert.False(exception is ArgumentException);
        Assert.True(IoFile.Exists(invalidPath));
        Assert.Equal(originalLength, new FileInfo(invalidPath).Length);
    }

    private static string CopyAudioFixture(TemporaryFolder folder, string fileName)
    {
        var source = FindRepositoryFile("BookStitch", "Assets", "Sounds", "Gentle", "information.wav");
        var target = Path.Combine(folder.Path, fileName);
        IoFile.Copy(source, target);
        return target;
    }

    private static string WriteTinyJpeg(TemporaryFolder folder, string fileName)
    {
        var path = Path.Combine(folder.Path, fileName);
        var bytes = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAH/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAEFAqf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/ASP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/ASP/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAY/Al//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/IV//2gAMAwEAAgADAAAAEP/EFBQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8QH//EFBQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8QH//EFBABAQAAAAAAAAAAAAAAAAAAARD/2gAIAQEAAT8QH//Z");
        IoFile.WriteAllBytes(path, bytes);
        return path;
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var directory = new DirectoryInfo(root);

            while (directory is { Exists: true })
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
                if (IoFile.Exists(candidate))
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
