using System.IO;

namespace CoverImageDrop.Acquisition.Tests;

[TestClass]
public sealed class DropImageDataTests
{
    [TestMethod]
    public async Task FromBytes_ExposesInMemoryPayloadWithoutFileBacking()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var source = DropImageData.FromBytes(expected, "cover.png");

        Assert.AreEqual(DropImagePayloadKind.InMemoryBytes, source.PayloadKind);
        Assert.AreEqual("cover.png", source.SourceName);
        Assert.IsNull(source.LocalFilePath);
        Assert.AreEqual(expected.LongLength, source.Length);
        Assert.IsTrue(source.TryGetInMemoryBytes(out var bytes));
        Assert.AreSame(expected, bytes);

        await using var stream = await source.OpenReadAsync();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        CollectionAssert.AreEqual(expected, copy.ToArray());
    }

    [TestMethod]
    public async Task FromFile_OpensStreamWithoutMaterializingBytes()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_Source_{Guid.NewGuid():N}.bin");
        var expected = new byte[] { 5, 6, 7, 8, 9 };

        try
        {
            await File.WriteAllBytesAsync(filePath, expected);
            var source = DropImageData.FromFile(filePath);

            Assert.AreEqual(DropImagePayloadKind.LocalFile, source.PayloadKind);
            Assert.AreEqual(Path.GetFileName(filePath), source.SourceName);
            Assert.AreEqual(filePath, source.LocalFilePath);
            Assert.AreEqual(expected.LongLength, source.Length);
            Assert.IsFalse(source.TryGetInMemoryBytes(out _));

            await using var stream = await source.OpenReadAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            CollectionAssert.AreEqual(expected, copy.ToArray());
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task CopyToAsync_StreamsFilePayloadToDestination()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_Copy_{Guid.NewGuid():N}.bin");
        var expected = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        try
        {
            await File.WriteAllBytesAsync(filePath, expected);
            var source = DropImageData.FromFile(filePath);
            using var destination = new MemoryStream();

            await source.CopyToAsync(destination);

            CollectionAssert.AreEqual(expected, destination.ToArray());
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [TestMethod]
    public async Task FromTemporaryFile_StreamsPayloadAndDisposeDeletesOwnedBacking()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_Temporary_{Guid.NewGuid():N}.tmp");
        var expected = Enumerable.Range(0, 1024).Select(value => (byte)(value % 251)).ToArray();
        await File.WriteAllBytesAsync(filePath, expected);

        var source = DropImageData.FromTemporaryFile(
            filePath,
            "cdn.example.test",
            "https://cdn.example.test/cover.png",
            "image/png");
        try
        {
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, source.PayloadKind);
            Assert.AreEqual(filePath, source.LocalFilePath);
            Assert.AreEqual(expected.LongLength, source.Length);
            Assert.AreEqual("https://cdn.example.test/cover.png", source.SourceUrl);
            Assert.AreEqual("image/png", source.ContentType);
            Assert.IsTrue(source.OwnsBackingFile);
            Assert.IsFalse(source.TryGetInMemoryBytes(out _));

            await using var stream = await source.OpenReadAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            CollectionAssert.AreEqual(expected, copy.ToArray());
        }
        finally
        {
            source.Dispose();
        }

        Assert.IsTrue(source.IsDisposed);
        Assert.IsFalse(File.Exists(filePath));
    }

    [TestMethod]
    public async Task FromFile_DisposeDoesNotDeleteCallerOwnedFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_CallerOwned_{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(filePath, [1, 2, 3]);
            var source = DropImageData.FromFile(filePath);

            source.Dispose();

            Assert.IsFalse(source.OwnsBackingFile);
            Assert.IsFalse(source.IsDisposed);
            Assert.IsTrue(File.Exists(filePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
