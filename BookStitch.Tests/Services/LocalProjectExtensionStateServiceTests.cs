using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalProjectExtensionStateServiceTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(
        Path.GetTempPath(),
        $"BookStitch_LocalExtensionState_{Guid.NewGuid():N}");

    private readonly LocalProjectExtensionStateService _service = new();

    public LocalProjectExtensionStateServiceTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    [Fact]
    public void ResolveInitialState_AllOriginalsComplete_ReturnsConverting()
    {
        var first = CreateFile("first.mp3");
        var second = CreateFile("second.mp3");

        var result = _service.ResolveInitialState([first, second]);

        Assert.Equal(ProjectPipelineState.Converting, result);
    }

    [Fact]
    public void ResolveInitialState_OriginalMissing_ReturnsAcquiringSources()
    {
        var existing = CreateFile("existing.mp3");
        var missing = Path.Combine(_tempFolder, "missing.mp3");

        var result = _service.ResolveInitialState([existing, missing]);

        Assert.Equal(ProjectPipelineState.AcquiringSources, result);
    }

    [Fact]
    public void ResolveInitialState_OnlyEmptyOriginal_ReturnsAcquiringSources()
    {
        var empty = Path.Combine(_tempFolder, "empty.mp3");
        File.WriteAllBytes(empty, []);

        var result = _service.ResolveInitialState([empty]);

        Assert.Equal(ProjectPipelineState.AcquiringSources, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }

    private string CreateFile(string fileName)
    {
        var path = Path.Combine(_tempFolder, fileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
