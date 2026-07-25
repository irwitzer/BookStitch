using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class AudioProcessingServiceTests
{
    private static readonly ExportPreset Stereo192 = ExportPreset.Parse("AAC Stereo 192 kbps");
    private static readonly ExportPreset Mono64 = ExportPreset.Parse("AAC Mono 64 kbps");

    [Theory]
    [InlineData("MP3", ".mp3")]
    [InlineData("FLAC", ".flac")]
    [InlineData("PCM_S16LE", ".wav")]
    [InlineData("", ".mp3")]
    [InlineData("", ".wav")]
    [InlineData("", ".flac")]
    public void DetermineProcessingAction_AlwaysConvertInputs_ReturnsConvert(string codec, string extension)
    {
        var track = new TrackInfo
        {
            Codec = codec,
            Extension = extension
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Konvertieren", action);
    }

    [Theory]
    [InlineData(".aac")]
    [InlineData("aac")]
    [InlineData(".AAC")]
    public void DetermineProcessingAction_RawAacFile_ReturnsConvert(string extension)
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = extension,
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Konvertieren", action);
    }

    [Theory]
    [InlineData(".m4a")]
    [InlineData(".m4b")]
    [InlineData("m4a")]
    [InlineData("M4B")]
    public void DetermineProcessingAction_AacInM4ContainerMatchingPreset_ReturnsCopy(string extension)
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = extension,
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Übernehmen", action);
    }

    [Theory]
    [InlineData(128, 2)]
    [InlineData(192, 1)]
    [InlineData(null, 2)]
    [InlineData(192, null)]
    public void DetermineProcessingAction_AacInM4ContainerNotMatchingPreset_ReturnsConvert(int? bitrateKbps, int? channels)
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = ".m4a",
            BitrateKbps = bitrateKbps,
            Channels = channels
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Konvertieren", action);
    }

    [Theory]
    [InlineData(182)]
    [InlineData(192)]
    [InlineData(202)]
    public void DetermineProcessingAction_AacBitrateWithinTolerance_ReturnsCopy(int bitrateKbps)
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = ".m4b",
            BitrateKbps = bitrateKbps,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Übernehmen", action);
    }

    [Theory]
    [InlineData(181)]
    [InlineData(203)]
    public void DetermineProcessingAction_AacBitrateOutsideTolerance_ReturnsConvert(int bitrateKbps)
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = ".m4b",
            BitrateKbps = bitrateKbps,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Konvertieren", action);
    }

    [Fact]
    public void DetermineProcessingAction_AacWithoutM4Container_ReturnsCheck()
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = ".mp4",
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Prüfen", action);
    }

    [Fact]
    public void DetermineProcessingAction_M4aWithUnknownCodec_ReturnsCheck()
    {
        var track = new TrackInfo
        {
            Codec = "",
            Extension = ".m4a"
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Prüfen", action);
    }

    [Fact]
    public void DetermineProcessingAction_UnknownCodecAndExtension_ReturnsCheck()
    {
        var track = new TrackInfo
        {
            Codec = "ALAC",
            Extension = ".m4a",
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Stereo192);

        Assert.Equal("Prüfen", action);
    }

    [Fact]
    public void DetermineProcessingAction_MonoAacMatchingMonoPreset_ReturnsCopy()
    {
        var track = new TrackInfo
        {
            Codec = "AAC",
            Extension = ".m4b",
            BitrateKbps = 64,
            Channels = 1
        };

        var action = AudioProcessingService.DetermineProcessingAction(track, Mono64);

        Assert.Equal("Übernehmen", action);
    }

    [Fact]
    public void DetermineProcessingAction_FailedProbe_ReturnsCheck()
    {
        var probe = new AudioProbeInfo
        {
            Success = false,
            FilePath = @"C:\Audio\track.m4a",
            CodecName = "aac",
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(probe, Stereo192);

        Assert.Equal("Prüfen", action);
    }

    [Fact]
    public void DetermineProcessingAction_ProbeAacM4aMatchingPreset_ReturnsCopy()
    {
        var probe = new AudioProbeInfo
        {
            Success = true,
            FilePath = @"C:\Audio\track.m4a",
            CodecName = "aac",
            BitrateKbps = 192,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(probe, Stereo192);

        Assert.Equal("Übernehmen", action);
    }

    [Fact]
    public void DetermineProcessingAction_ProbeMp3_ReturnsConvert()
    {
        var probe = new AudioProbeInfo
        {
            Success = true,
            FilePath = @"C:\Audio\track.mp3",
            CodecName = "mp3",
            BitrateKbps = 128,
            Channels = 2
        };

        var action = AudioProcessingService.DetermineProcessingAction(probe, Stereo192);

        Assert.Equal("Konvertieren", action);
    }

    [Theory]
    [InlineData(null, "Offen")]
    [InlineData("", "Offen")]
    [InlineData("  ", "Offen")]
    [InlineData("Uebernehmen", "Übernehmen")]
    [InlineData("übernehmen", "Übernehmen")]
    [InlineData("Pruefen", "Prüfen")]
    [InlineData("prüfen", "Prüfen")]
    [InlineData("Konvertieren", "Konvertieren")]
    public void NormalizeProcessingAction_NormalizesKnownValues(string? input, string expected)
    {
        var result = AudioProcessingService.NormalizeProcessingAction(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(1, "Mono")]
    [InlineData(2, "Stereo")]
    [InlineData(6, "6 Kanäle")]
    public void FormatChannelLayout_FormatsChannels(int? channels, string expected)
    {
        var result = AudioProcessingService.FormatChannelLayout(channels);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" aac ", "AAC")]
    [InlineData("mp3", "MP3")]
    public void NormalizeCodecName_NormalizesCodecNames(string? codecName, string expected)
    {
        var result = AudioProcessingService.NormalizeCodecName(codecName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("pcm_s16le", true)]
    [InlineData("PCM_S24LE", true)]
    [InlineData("PCM", true)]
    [InlineData("aac", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPcmCodec_DetectsPcmCodecs(string? codec, bool expected)
    {
        var result = AudioProcessingService.IsPcmCodec(codec);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildProcessingActionSummary_SummarizesActions()
    {
        var tracks = new[]
        {
            new TrackInfo { ProcessingAction = "Übernehmen" },
            new TrackInfo { ProcessingAction = "Konvertieren" },
            new TrackInfo { ProcessingAction = "Konvertieren" },
            new TrackInfo { ProcessingAction = "Prüfen" },
            new TrackInfo { ProcessingAction = "" }
        };

        var summary = AudioProcessingService.BuildProcessingActionSummary(tracks);

        Assert.Equal("1 übernehmen, 2 konvertieren, 1 prüfen, 1 offen", summary);
    }

    [Fact]
    public void BuildAudioDiscPipelineActionSummary_DescribesMixedResumeState()
    {
        var tracks = new[]
        {
            new TrackInfo { ProcessingAction = "Konvertieren", HasReusableConvertedFile = true },
            new TrackInfo { ProcessingAction = "Konvertieren", HasReusableConvertedFile = true },
            new TrackInfo { ProcessingAction = "Konvertieren" },
            new TrackInfo { ProcessingAction = "FLAC rippen" },
            new TrackInfo { ProcessingAction = "FLAC rippen" }
        };

        var summary = AudioProcessingService.BuildAudioDiscPipelineActionSummary(tracks, "AAC Stereo 128 kbps");

        Assert.Equal("2 FLAC rippen, 1 zu AAC Stereo 128 kbps konvertieren, 2 AAC wiederverwenden", summary);
    }

    [Fact]
    public void BuildAudioDiscPipelineActionSummary_AllTracksNeedRipping()
    {
        var tracks = Enumerable.Range(1, 3)
            .Select(_ => new TrackInfo { ProcessingAction = "FLAC rippen" })
            .ToList();

        var summary = AudioProcessingService.BuildAudioDiscPipelineActionSummary(tracks, "AAC Mono 64 kbps");

        Assert.Equal("3 FLAC rippen", summary);
    }

    [Fact]
    public void BuildProcessingActionSummary_EmptyTrackList_ReturnsNoTracks()
    {
        var summary = AudioProcessingService.BuildProcessingActionSummary([]);

        Assert.Equal("noch keine Tracks", summary);
    }
}
