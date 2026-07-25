using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackStateUpdateQueueServiceTests
{
    [Fact]
    public void ApplyUpdates_RefreshesSourceAndConvertedSizesForSelectedPreset()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[2 * 1024 * 1024]);
            File.WriteAllBytes(convertedPath, new byte[512 * 1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            var changed = TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.True(changed);
            Assert.Equal(2d, track.SizeMb);
            Assert.True(track.SourceSizeAvailable);
            Assert.Equal("2.00", track.DisplaySizeMb);
            Assert.Equal(0.5d, track.ConvertedSizeMb);
            Assert.True(track.ConvertedSizeAvailable);
            Assert.Equal("0.5 MB", track.DisplayConvertedSizeMb);
            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal("Wiederverwenden", track.DisplayProcessingAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_IgnoresConvertedFileFromDifferentPreset()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Stereo 128 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.Equal(0d, track.ConvertedSizeMb);
            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("–", track.DisplayConvertedSizeMb);
            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal("Bereit", track.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_UsesPartFileSizeWhileConversionIsRunning()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[256 * 1024]);

            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.Equal(0.25d, track.ConvertedSizeMb);
            Assert.True(track.ConvertedSizeAvailable);
            Assert.Equal("0.3 MB", track.DisplayConvertedSizeMb);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_LeavesMissingSizesBlank()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.flac"),
                ProcessingAction = "FLAC rippen",
                Warning = "Noch nicht gerippt"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.False(track.SourceSizeAvailable);
            Assert.Equal("", track.DisplaySizeMb);
            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("–", track.DisplayConvertedSizeMb);
            Assert.Equal("Noch nicht gerippt", track.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ShowsAndWarnsAboutExistingZeroByteFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, Array.Empty<byte>());
            File.WriteAllBytes(convertedPath, Array.Empty<byte>());
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converted");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "FLAC rippen"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.True(track.SourceSizeAvailable);
            Assert.Equal("0.00", track.DisplaySizeMb);
            Assert.True(track.ConvertedSizeAvailable);
            Assert.Equal("0.0 MB", track.DisplayConvertedSizeMb);
            Assert.Contains("Quelldatei ist leer", track.Warning);
            Assert.Contains("AAC-Datei ist leer", track.Warning);
            Assert.False(track.HasReusableConvertedFile);
            Assert.Equal("FLAC rippen", track.ProcessingAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void ApplyUpdates_RefreshesCompletedAudioDiscSourceMetadata()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            File.WriteAllBytes(sourcePath, new byte[1024 * 1024]);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                DiscNumber = 1,
                Duration = "00:10",
                ProcessingAction = "FLAC rippen"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));

            Assert.Equal(".flac", track.Extension);
            Assert.Equal("FLAC", track.Codec);
            Assert.Equal(2, track.Channels);
            Assert.Equal("Stereo", track.ChannelLayout);
            Assert.Equal(839, track.BitrateKbps);
            Assert.Equal("Konvertieren", track.ProcessingAction);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ClearsRemovedPartFileAndStalePreparedState()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[128 * 1024]);
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren",
                PreparedConvertedPath = convertedPath,
                PreparedConvertedPreset = "AAC Mono 64 kbps"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", true));
            Assert.True(track.ConvertedSizeAvailable);

            File.Delete(convertedPath + ".part");
            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("–", track.DisplayConvertedSizeMb);
            Assert.Equal("", track.PreparedConvertedPath);
            Assert.Equal("", track.PreparedConvertedPreset);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void ApplyUpdates_IgnoresLingeringPartFileWhenWorkflowIsIdle()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "track.flac");
            var convertedPath = Path.Combine(root, "track.m4a");
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath + ".part", new byte[256 * 1024]);
            WriteManifest(root, sourcePath, convertedPath, "AAC Mono 64 kbps", "Converting");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren",
                ConvertedSizeAvailable = true,
                ConvertedSizeMb = 0.25d
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.False(track.ConvertedSizeAvailable);
            Assert.Equal("–", track.DisplayConvertedSizeMb);
            Assert.False(track.HasReusableConvertedFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_ClearsMeasuredMetadataWhenLocalSourceIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.mp3"),
                ProcessingAction = "Konvertieren",
                BitrateKbps = 192,
                Channels = 2,
                ChannelLayout = "Stereo",
                Codec = "MP3"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.Null(track.BitrateKbps);
            Assert.Null(track.Channels);
            Assert.Equal("", track.ChannelLayout);
            Assert.Equal("MP3", track.Codec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_PreservesExpectedAudioDiscChannelMetadataBeforeRip()
    {
        var root = CreateTempDirectory();
        try
        {
            var track = new TrackInfo
            {
                FilePath = Path.Combine(root, "missing.flac"),
                DiscNumber = 1,
                ProcessingAction = "FLAC rippen",
                BitrateKbps = 1411,
                Channels = 2,
                ChannelLayout = "Stereo",
                Codec = "FLAC"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Mono 64 kbps", false));

            Assert.Null(track.BitrateKbps);
            Assert.Equal(2, track.Channels);
            Assert.Equal("Stereo", track.ChannelLayout);
            Assert.Equal("FLAC", track.Codec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_UsesExportManifestForMp3DiscProject()
    {
        var root = CreateTempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            var convertedPath = Path.Combine(root, "converted", "aac", "track.m4a");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[2048]);

            File.WriteAllText(
                Path.Combine(root, "project.json"),
                JsonSerializer.Serialize(new Mp3DiscProjectManifest
                {
                    ProjectFolder = root,
                    TotalDiscs = 1
                }));

            WriteManifest(root, sourcePath, convertedPath, "AAC Stereo 160 kbps", "Converted", "export-project.json");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, "AAC Stereo 160 kbps", false));

            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal(convertedPath, track.PreparedConvertedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_RestoresCompletedLiveConversionFromDeterministicPathWithoutManifestEntry()
    {
        var root = CreateTempDirectory();
        try
        {
            const string presetName = "AAC Stereo 160 kbps";
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = "track.mp3",
                ProcessingAction = "Konvertieren"
            };

            var preset = ExportPreset.Parse(presetName);
            var convertedFolder = Path.Combine(root, "converted", preset.GetFolderName());
            Directory.CreateDirectory(convertedFolder);
            var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
            File.WriteAllBytes(convertedPath, new byte[2048]);

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, presetName, false));

            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal("Wiederverwenden", track.DisplayProcessingAction);
            Assert.Equal(convertedPath, track.PreparedConvertedPath);
            Assert.Equal(presetName, track.PreparedConvertedPreset);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyUpdates_KeepsCompletedFinalFileWhenManifestStatusWasNotFlushedBeforeAbort()
    {
        var root = CreateTempDirectory();
        try
        {
            const string presetName = "AAC Stereo 160 kbps";
            var sourcePath = Path.Combine(root, "CD 01", "track.mp3");
            var convertedPath = Path.Combine(root, "converted", "track.m4a");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
            File.WriteAllBytes(sourcePath, new byte[1024]);
            File.WriteAllBytes(convertedPath, new byte[2048]);
            WriteManifest(root, sourcePath, convertedPath, presetName, "Converting", "export-project.json");

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = "track.mp3",
                ProcessingAction = "Konvertieren"
            };

            TrackStateUpdateQueueService.ApplyUpdates(new TrackStateUpdateContext(
                new[] { track }, root, presetName, false));

            Assert.True(track.HasReusableConvertedFile);
            Assert.Equal("Konvertiert", track.Status);
            Assert.Equal(convertedPath, track.PreparedConvertedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteManifest(
        string root,
        string sourcePath,
        string convertedPath,
        string preset,
        string status,
        string fileName = "project.json")
    {
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    SourcePath = sourcePath,
                    Preset = preset,
                    Status = status,
                    ConvertedPath = convertedPath
                }
            ]
        };

        File.WriteAllText(
            Path.Combine(root, fileName),
            JsonSerializer.Serialize(manifest));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
