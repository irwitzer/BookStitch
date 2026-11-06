using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Opposite_channel_sources_are_offered_by_real_preflight(int channels)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder(); var source = Path.Combine(folder.Path, "source.m4a");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=sample_rate=44100:seed=42",
            "-t", "3", "-ac", channels.ToString(), "-c:a", "aac", "-b:a", channels == 1 ? "64k" : "128k", source]);
        var user = ExportPreset.Parse(channels == 1 ? "AAC Stereo 128 kbps" : "AAC Mono 64 kbps");
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, Extension = ".m4a" };
        var options = await new AacDecisionPreflightService().AnalyzeAsync([track], folder.Path, user, 44100,
            tools.FfmpegPath, tools.FfprobePath, default);
        Assert.Equal(channels, options.Optimized.Channels); Assert.Equal(1, options.Optimized.ReusedCount);
        Assert.True(AacDecisionSelectionService.Select(options).NeedsDialog);
        Assert.Equal(user.Channels, options.EncodeAll.Channels); Assert.Equal(0, options.EncodeAll.ReusedCount);
    }

    [Fact]
    public async Task Broken_attached_png_preserves_audio_and_chapters_and_allows_copy_or_conversion()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var audio = Path.Combine(folder.Path, "audio.m4a"); var cover = Path.Combine(folder.Path, "cover.png");
        var metadata = Path.Combine(folder.Path, "chapters.txt"); var source = Path.Combine(folder.Path, "source.m4b");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=sample_rate=44100:seed=42",
            "-t", "3", "-ac", "2", "-c:a", "aac", "-b:a", "128k", audio]);
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i", "color=blue:s=32x32", "-frames:v", "1", cover]);
        File.WriteAllText(metadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1500\ntitle=First\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=1500\nEND=3000\ntitle=Second\n");
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-y", "-i", audio, "-i", cover, "-i", metadata,
            "-map", "0:a:0", "-map", "1:v:0", "-c", "copy", "-disposition:v:0", "attached_pic", "-map_chapters", "2", source]);
        var bytes = File.ReadAllBytes(source);
        var png = bytes.AsSpan().IndexOf(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }); Assert.True(png >= 0);
        var idat = png + bytes.AsSpan(png).IndexOf(Encoding.ASCII.GetBytes("IDAT")); Assert.True(idat > png);
        new byte[] { 0x7f, 0xff, 0xff, 0xff }.CopyTo(bytes, idat - 4); File.WriteAllBytes(source, bytes);
        var raw = await new FfprobeRunnerService().RunAsync(tools.FfprobePath, ["-v", "error", "-show_streams", "-of", "json", source],
            TimeSpan.FromSeconds(30), default);
        Assert.Contains("chunk too big", raw.Stderr);
        var probe = await new AudioInfoService().ProbeAsync(source, tools.FfprobePath);
        Assert.True(probe.Success); Assert.False(probe.HasProbeErrors); Assert.Equal(2, probe.Chapters.Count);
        var track = new TrackInfo { SourceId = Guid.NewGuid(), FilePath = source, Extension = ".m4b" };
        var options = await new AacDecisionPreflightService().AnalyzeAsync([track], folder.Path, ExportPreset.Parse("AAC Mono 64 kbps"),
            44100, tools.FfmpegPath, tools.FfprobePath, default);
        Assert.True(AacDecisionSelectionService.Select(options).NeedsDialog);
        Assert.Equal(2, options.Optimized.Channels); Assert.Equal(1, options.Optimized.ReusedCount);
        Assert.Equal(2, track.EmbeddedChapters.Count);
        foreach (var plan in new[] { options.Optimized, options.EncodeAll })
        {
            AacDecisionPlanner.Apply(plan, [track]); var target = Path.Combine(folder.Path, plan.Channels + ".m4a");
            await new AacExportProcessingService().PrepareTrackForExportAsync(track, source, target, plan.Preset,
                tools.FfmpegPath, default, _ => { }, ffprobePath: tools.FfprobePath);
            Assert.Equal(plan.Channels, (await new AudioInfoService().ProbeAsync(target, tools.FfprobePath)).Channels);
            await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", target, "-map", "0:a:0", "-f", "null", "-"]);
        }
        Assert.Equal(bytes, File.ReadAllBytes(source));
    }
}
