using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SampleRateSettingsServiceTests
{
    [Theory]
    [InlineData("44100", 44100)]
    [InlineData("48000", 48000)]
    [InlineData("22050", 44100)]
    [InlineData("0", 44100)]
    [InlineData("null", 44100)]
    [InlineData("\"invalid\"", 44100)]
    [InlineData("\"48000\"", 48000)]
    [InlineData("{}", 44100)]
    public void Malformed_preference_does_not_discard_other_settings(string value, int expected)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"SelectedExportPreset\":\"AAC Stereo 128 kbps\",\"DefaultSampleRateHz\":" + value + "}")!;
        Assert.Equal(expected, settings.DefaultSampleRateHz);
        Assert.Equal("AAC Stereo 128 kbps", settings.SelectedExportPreset);
        Assert.Equal(expected, JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.DefaultSampleRateHz);
    }

    [Theory]
    [InlineData("Folder")]
    [InlineData("Mp3Disc")]
    [InlineData("AudioCd")]
    public void Primary_manifest_owns_target_and_legacy_projects_retain_44100(string type)
    {
        using var folder = new TemporaryFolder();
        var path = type == "Folder" ? ProjectFolderLayout.GetFolderManifestPath(folder.Path) :
            type == "Mp3Disc" ? ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path) : ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schemaVersion\":1}");
        Assert.Equal(44100, ProjectSampleRateService.ReadTarget(folder.Path));
        File.WriteAllText(path, "{\"schemaVersion\":1,\"TargetSampleRateHz\":48000}");
        Assert.Equal(48000, ProjectSampleRateService.ReadTarget(folder.Path));
        var settings = new AppSettings { DefaultSampleRateHz = 44100 };
        settings.DefaultSampleRateHz = 48000;
        Assert.Equal(48000, ProjectSampleRateService.ReadTarget(folder.Path));
        Assert.Equal(44100, JsonSerializer.Deserialize<AppSettings>("{}")!.DefaultSampleRateHz);
    }
}
