using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class FileSystemMetadataReader
{
    public FileSystemMetadata Read(string rootFolder, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        var relativePath = Path.GetRelativePath(rootFolder, fullPath);

        return new FileSystemMetadata(
            fullPath,
            Path.GetFileName(fullPath),
            Path.GetExtension(fullPath),
            relativePath,
            Path.GetDirectoryName(relativePath) ?? "",
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }
}
