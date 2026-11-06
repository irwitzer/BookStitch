using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepared_columns_use_real_timing_or_cache_and_never_nominal_preset_values(bool legacy)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.m4a");
        var prepared = Path.Combine(folder.Path, "prepared.m4a");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-f", "lavfi", "-i", "anoisesrc=sample_rate=48000:seed=42", "-t", "1", "-ac", "1", "-c:a", "aac", "-b:a", "64k", source]);
        File.Copy(source, prepared);
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, TargetSampleRateHz = 48000,
            HasReusableConvertedFile = true, PreparedConvertedPath = prepared, PreparedConvertedPreset = "AAC Mono 64 kbps" };
        var timing = await new AacExportProcessingService().AnalyzeConvertedTimingAsync(prepared, tools.FfprobePath, null, default);
        Assert.Equal("LC", timing.Audio!.Profile); Assert.Equal(48000, timing.Audio.SampleRateHz); Assert.Equal(1, timing.Audio.Channels);
        Assert.True(timing.Audio.BitrateBitsPerSecond > 0);
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var store = new WorkManifestService();
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var manifest = store.LoadOrCreate(manifestPath, folder.Path, folder.Path, track.PreparedConvertedPreset);
        store.UpdateTrack(manifest, 0, track, source, prepared, ExportPreset.Parse(track.PreparedConvertedPreset));
        manifest.Tracks[0].AacTiming = legacy ? null : timing;
        store.Save(manifestPath, manifest);
        var before = File.ReadAllBytes(manifestPath);
        var cache = new AudioEvidenceCache();
        var result = Assert.Single(await new PreparedAudioColumnService().ReadAsync([track], folder.Path,
            track.PreparedConvertedPreset, legacy ? tools.FfprobePath : "unavailable-tool", cache, default));
        Assert.Equal(timing.Audio, result.Audio);
        Assert.Equal(legacy, cache.HasEntries); // Modern timing needs no extra probe or tool lookup.
        if (legacy)
        {
            cache.Save(folder.Path, "prepared-audio-evidence.json");
            var restored = AudioEvidenceCache.Load(folder.Path, "prepared-audio-evidence.json");
            var identity = AudioEvidenceCache.ProbeSchemaIdentity + AacToolchainIdentityCache.Identity(tools.FfprobePath);
            Assert.True(restored.TryGet(prepared, identity, out var saved));
            Assert.Equal(timing.Audio, AudioStreamDetails.FromProbe(saved!.Probe));
            Assert.False(restored.TryGet(prepared, identity + "changed-toolchain", out _));
            File.AppendAllText(prepared, "changed");
            Assert.False(restored.TryGet(prepared, identity, out _));
            // Restore the fixture artifact before checking the independent prepared-state invalidation below.
            File.Copy(source, prepared, overwrite: true);
            File.SetLastWriteTimeUtc(prepared, new DateTime(result.Modified, DateTimeKind.Utc));
        }
        Assert.Equal(before, File.ReadAllBytes(manifestPath));
        new TrackPreparedStateRefreshService().RefreshForCurrentPreset([track], folder.Path, track.PreparedConvertedPreset);
        track.PreparedAudio = result.Audio; track.PreparedAudioLength = result.Length; track.PreparedAudioModified = result.Modified;
        Assert.Equal("48.000", track.DisplayPreparedSampleRateHz); Assert.Equal("Mono", track.DisplayPreparedChannelLayout);
        Assert.Equal("AAC-LC", track.DisplayPreparedProfile);
        new TrackPreparedStateRefreshService().RefreshForCurrentPreset([track], folder.Path, "AAC Stereo 128 kbps");
        Assert.Empty(track.DisplayPreparedProfile); Assert.Null(track.DisplayPreparedBitrateKbps);
        Assert.Empty(track.DisplayPreparedSampleRateHz); Assert.False(track.HasReusableConvertedFile);
    }
}
