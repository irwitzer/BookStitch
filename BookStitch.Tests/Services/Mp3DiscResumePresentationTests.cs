using System.IO;
using System.Text.Json;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscResumePresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_presentation_is_not_guessed_from_a_mismatched_identity_or_path(bool sameIdentity)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(folder.Path), "213.mp3");
        File.WriteAllText(path, "source");
        var id = Guid.NewGuid();
        new SourceMetadataSnapshotStore().Save(folder.Path, new SourceMetadataSnapshot
        {
            SourceId = id,
            TechnicalInfo = new SourceTechnicalInfo
            {
                SourcePath = sameIdentity ? Path.Combine(ProjectFolderLayout.GetOriginalsFolder(folder.Path), "other.mp3") : path,
                Duration = TimeSpan.FromMinutes(3), BitrateKbps = 128, Channels = 2
            }
        });
        var row = new TrackInfo { FilePath = path, SourceId = sameIdentity ? id : Guid.NewGuid() };
        Assert.Equal(0, new Mp3DiscTrackReconciliationService().RestoreImportedSourcePresentation([row], folder.Path));
        Assert.Null(row.DurationTicks);
        Assert.Null(row.BitrateKbps);
        Assert.False(row.HasReusableConvertedFile);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void Reused_copies_restore_duration_and_valid_preparation_without_callbacks_or_state_writes(
        int repeatedPauses, bool validPreparation)
    {
        using var folder = new TemporaryFolder();
        var project = Path.Combine(folder.Path, "project");
        ProjectFolderLayout.EnsureProjectFolders(project);
        var source = Path.Combine(folder.Path, "drive", "213_Oxen_CD02.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "unchanged source");
        var imported = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project, 2), Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        File.Copy(source, imported);
        var converted = Path.Combine(project, "converted", "prepared.m4a");
        File.WriteAllBytes(converted, new byte[4096]);
        var id = Guid.NewGuid();
        new SourceMetadataSnapshotStore().Save(project, new SourceMetadataSnapshot
        {
            SourceId = id,
            CanonicalFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 123, SourceDiscNumber = 2 },
            TechnicalInfo = new SourceTechnicalInfo
            {
                SourcePath = imported, Duration = TimeSpan.FromSeconds(222), BitrateKbps = 160, Channels = 2, Codec = "MP3"
            }
        });
        File.WriteAllText(ProjectFolderLayout.GetMp3DiscManifestPath(project), JsonSerializer.Serialize(
            new Mp3DiscProjectManifest { ProjectId = "test", TargetSampleRateHz = 48000, TotalDiscs = 2 }));
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(project);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new ExportWorkManifest
        {
            ProjectWorkFolder = project, ProjectType = ProjectManifestTypes.Mp3DiscProject, SelectedPreset = "AAC Stereo 128 kbps",
            Tracks = [new ExportWorkManifestTrack
            {
                SourceId = id, SourcePath = imported, ConvertedPath = converted, Preset = "AAC Stereo 128 kbps",
                SourceSizeBytes = new FileInfo(imported).Length, SourceLastWriteUtcTicks = File.GetLastWriteTimeUtc(imported).Ticks,
                ConvertedSizeBytes = new FileInfo(converted).Length, ConvertedLastWriteUtcTicks = File.GetLastWriteTimeUtc(converted).Ticks,
                PreparationVersion = 1, PreparationId = validPreparation ? Guid.NewGuid() : Guid.Empty,
                TargetSampleRateHz = 48000, Status = "Converted"
            }]
        }));
        var manifestBefore = File.ReadAllBytes(manifestPath);
        var originalBefore = File.ReadAllBytes(imported);
        var row = new TrackInfo
        {
            SourceId = id, DiscNumber = 2, FilePath = source, FileName = Path.GetFileName(source),
            Index = 213, AutomaticOrderPosition = 213, TrackNumber = 213, ChapterTitle = "User chapter", IsExcluded = true
        };
        var service = new Mp3DiscTrackReconciliationService();
        var rows = new List<TrackInfo> { row };
        // Cold resume starts with a drive preview; session pauses retain this same row.
        for (var cycle = 0; cycle <= repeatedPauses; cycle++)
        {
            service.AppendMissingPreviewTracks(rows,
                service.CreatePreviewTracksFromKnownFiles(Path.GetDirectoryName(source)!, [source]),
                Path.GetDirectoryName(source)!, 2, false);
            Assert.Same(row, Assert.Single(rows));
            service.ReconcileImportedTrackPaths([row], Path.GetDirectoryName(source)!, project, 2);
            service.RestoreImportedSourcePresentation([row], project);
            new TrackPreparedStateRefreshService().RefreshForCurrentPreset([row], project, "AAC Stereo 128 kbps");
            Assert.Equal(imported, row.FilePath);
            Assert.Equal(TimeSpan.FromSeconds(222).Ticks, row.DurationTicks);
            Assert.Equal("03:42", row.Duration);
            Assert.Equal(123, row.SourceTrackNumber);
            Assert.Equal(213, row.TrackNumber);
            Assert.Equal(213, row.Index);
            Assert.Equal("User chapter", row.ChapterTitle);
            Assert.True(row.IsExcluded);
            Assert.Equal(48000, row.TargetSampleRateHz);
            Assert.Equal(validPreparation, row.HasReusableConvertedFile);
            Assert.Equal(validPreparation ? 128 : (int?)null, row.DisplayPreparedBitrateKbps);
            Assert.Equal(validPreparation ? "Stereo" : "", row.DisplayPreparedChannelLayout);
            Assert.Null(row.AudioValidationPassed); // Metadata does not certify media validation.
        }
        Assert.Equal(manifestBefore, File.ReadAllBytes(manifestPath));
        Assert.Equal(originalBefore, File.ReadAllBytes(imported));
    }
}
