using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OutputFileConflictServiceTests
{
    [Fact]
    public void Long_conflict_alternative_respects_final_path_limits()
    {
        var folder = CreateTempFolder();
        try
        {
            var name = new string('a', WindowsOutputPathService.MaximumPathLength - folder.Length - 5);
            var path = Path.Combine(folder, name + ".m4b");
            Assert.True(WindowsOutputPathService.IsValid(path));
            var result = OutputFileConflictService.CreateRenamedOutputPath(path, ExportPreset.Parse("AAC Stereo 128 kbps"));
            Assert.True(WindowsOutputPathService.IsValid(result));
            Assert.StartsWith("NEU 128 ", Path.GetFileName(result));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Renamed_output_skips_existing_directories_as_well_as_files()
    {
        var folder = CreateTempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "NEU 128 Book.m4b"));
            Directory.CreateDirectory(Path.Combine(folder, "NEU 128 (2) Book.m4b"));
            var result = OutputFileConflictService.CreateRenamedOutputPath(Path.Combine(folder, "Book.m4b"),
                ExportPreset.Parse("AAC Stereo 128 kbps"));
            Assert.Equal(Path.Combine(folder, "NEU 128 (3) Book.m4b"), result);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void CreateRenamedOutputPath_Stereo_AddsBitratePrefix()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "Autor - Titel.m4b");

            var result = OutputFileConflictService.CreateRenamedOutputPath(
                path,
                ExportPreset.Parse("AAC Stereo 128 kbps"));

            Assert.Equal(Path.Combine(folder, "NEU 128 Autor - Titel.m4b"), result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CreateRenamedOutputPath_Mono_AddsBitrateAndMonoPrefix()
    {
        var folder = CreateTempFolder();
        try
        {
            var path = Path.Combine(folder, "Titel.m4a");

            var result = OutputFileConflictService.CreateRenamedOutputPath(
                path,
                ExportPreset.Parse("AAC Mono 96 kbps"));

            Assert.Equal(Path.Combine(folder, "NEU 96 Mono Titel.m4a"), result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CreateRenamedOutputPath_WhenSuggestedNameExists_AddsCounter()
    {
        var folder = CreateTempFolder();
        try
        {
            var existing = Path.Combine(folder, "NEU 64 Mono Titel.m4b");
            File.WriteAllText(existing, "existing");

            var result = OutputFileConflictService.CreateRenamedOutputPath(
                Path.Combine(folder, "Titel.m4b"),
                ExportPreset.Parse("AAC Mono 64 kbps"));

            Assert.Equal(Path.Combine(folder, "NEU 64 Mono (2) Titel.m4b"), result);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
