namespace BookStitch.Models;

public sealed record FileSystemMetadata(
    string FullPath,
    string FileName,
    string Extension,
    string RelativePath,
    string RelativeFolder,
    long SizeBytes,
    DateTime LastWriteTimeUtc);
