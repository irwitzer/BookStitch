using System.IO;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Naming;
using CoverImageDrop.Core.Output;
using CoverImageDrop.Pipeline.Persistence;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePersistenceServiceTests
{
    [TestMethod]
    public async Task SaveSourceAsync_GenerateUniqueName_UsesTwoAndThreeSuffixesAtomically()
    {
        var folder = CreateTestFolder();
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, "Cover.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(folder, "Cover (2).jpg"), [2]);

            var service = new CoverImagePersistenceService();
            var payload = new byte[] { 10, 20, 30, 40 };
            var result = await service.SaveSourceAsync(
                DropImageData.FromBytes(payload, "source.jpg"),
                folder,
                "jpg",
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = "Cover"
                });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("Cover (3).jpg", result.FileName);
            Assert.IsTrue(result.WasRenamedForCollision);
            CollectionAssert.AreEqual(
                payload,
                await File.ReadAllBytesAsync(result.FilePath!));
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Data is not null &&
                issue.Data.TryGetValue("Reason", out var reason) &&
                Equals(reason, "CollisionResolved")));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveSourceAsync_LongGeneratedNameAndCollisionStayWithinWindowsComponentLimit()
    {
        var folder = CreateTestFolder();
        try
        {
            var service = new CoverImagePersistenceService();
            var longName = new string('x', 400);
            var payload = new byte[] { 1, 2, 3, 4 };

            var first = await service.SaveSourceAsync(
                DropImageData.FromBytes(payload, "source.jpg"),
                folder,
                "jpg",
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = longName
                });

            var second = await service.SaveSourceAsync(
                DropImageData.FromBytes(payload, "source.jpg"),
                folder,
                "jpg",
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = longName
                });

            Assert.IsTrue(first.IsSuccess);
            Assert.IsTrue(second.IsSuccess);
            Assert.AreEqual(
                CoverImageFileNameGenerator.MaxFileNameCharacters,
                first.FileName!.Length);
            Assert.IsLessThanOrEqualTo(
                CoverImageFileNameGenerator.MaxFileNameCharacters,
                second.FileName!.Length);
            Assert.EndsWith(" (2).jpg", second.FileName);
            Assert.IsTrue(second.WasRenamedForCollision);
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveSourceToPathAsync_OverlongExplicitFileNameFailsBeforeWritingTarget()
    {
        var folder = CreateTestFolder();
        try
        {
            var overlongName =
                new string('x', CoverImageFileNameGenerator.MaxFileNameCharacters + 1);
            var targetPath = Path.Combine(folder, overlongName);
            var service = new CoverImagePersistenceService();

            var result = await service.SaveSourceToPathAsync(
                DropImageData.FromBytes([1, 2, 3], "source.bin"),
                targetPath);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Code == CoverImageIssueCode.PersistenceFailed));
            Assert.IsFalse(File.Exists(targetPath));
            Assert.IsEmpty(Directory.GetFiles(folder, ".coverimagedrop-*.tmp"));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveSourceAsync_Overwrite_ReplacesExistingFile()
    {
        var folder = CreateTestFolder();
        try
        {
            var path = Path.Combine(folder, "Cover.jpg");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);

            var service = new CoverImagePersistenceService();
            var payload = new byte[] { 9, 8, 7, 6 };
            var result = await service.SaveSourceAsync(
                DropImageData.FromBytes(payload, "source.jpg"),
                folder,
                "jpg",
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = "Cover"
                },
                CoverImageCollisionPolicy.Overwrite);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(path, result.FilePath);
            Assert.IsFalse(result.WasRenamedForCollision);
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveSourceAsync_FailIfExists_PreservesExistingFile()
    {
        var folder = CreateTestFolder();
        try
        {
            var path = Path.Combine(folder, "Cover.jpg");
            var existing = new byte[] { 1, 2, 3 };
            await File.WriteAllBytesAsync(path, existing);

            var service = new CoverImagePersistenceService();
            var result = await service.SaveSourceAsync(
                DropImageData.FromBytes([9, 8, 7], "source.jpg"),
                folder,
                "jpg",
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = "Cover"
                },
                CoverImageCollisionPolicy.FailIfExists);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Code == CoverImageIssueCode.PersistenceFailed));
            CollectionAssert.AreEqual(existing, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveSourceToPathAsync_LocalFile_CopiesPayloadByteIdentically()
    {
        var folder = CreateTestFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "source.bin");
            var targetPath = Path.Combine(folder, "copy.bin");
            var payload = Enumerable.Range(0, 200_000)
                .Select(index => (byte)(index % 251))
                .ToArray();
            await File.WriteAllBytesAsync(sourcePath, payload);

            var service = new CoverImagePersistenceService();
            var result = await service.SaveSourceToPathAsync(
                DropImageData.FromFile(sourcePath),
                targetPath);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(payload.LongLength, result.BytesWritten);
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SaveOutputAsync_UsesActualOutputExtension()
    {
        var folder = CreateTestFolder();
        try
        {
            var output = new CoverImageOutputResult(
                [4, 5, 6],
                1000,
                1000,
                CoverOutputFormat.Png,
                "image/png",
                ".png",
                Array.Empty<CoverImageIssue>());
            var service = new CoverImagePersistenceService();

            var result = await service.SaveOutputAsync(
                output,
                folder,
                new CoverImageNamingOptions
                {
                    Strategy = CoverImageNamingStrategy.HostProvidedName,
                    HostProvidedName = "Cover.jpg"
                });

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("Cover.png", result.FileName);
            CollectionAssert.AreEqual(output.Bytes, await File.ReadAllBytesAsync(result.FilePath!));
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task Cancellation_PropagatesAndLeavesNoTemporaryFile()
    {
        var folder = CreateTestFolder();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var service = new CoverImagePersistenceService();

            try
            {
                await service.SaveSourceAsync(
                    DropImageData.FromBytes([1, 2, 3], "source.jpg"),
                    folder,
                    "jpg",
                    cancellationToken: cancellation.Token);
                Assert.Fail("Expected cancellation to propagate.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.AreEqual(
                0,
                Directory.GetFiles(folder, ".coverimagedrop-*.tmp").Length);
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    [TestMethod]
    public async Task SourceReadFailure_ReturnsPersistenceIssueAndCleansTemporaryFile()
    {
        var folder = CreateTestFolder();
        try
        {
            var missingSource = Path.Combine(folder, "missing.jpg");
            var targetPath = Path.Combine(folder, "result.jpg");
            var service = new CoverImagePersistenceService();

            var result = await service.SaveSourceToPathAsync(
                DropImageData.FromFile(missingSource),
                targetPath);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Code == CoverImageIssueCode.PersistenceFailed));
            Assert.IsFalse(File.Exists(targetPath));
            Assert.AreEqual(
                0,
                Directory.GetFiles(folder, ".coverimagedrop-*.tmp").Length);
        }
        finally
        {
            DeleteTestFolder(folder);
        }
    }

    private static string CreateTestFolder()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "CoverImageDrop.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteTestFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}