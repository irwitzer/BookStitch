using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class WorkflowStatusFormatterTests
{
    private readonly WorkflowStatusFormatter _formatter = new();

    [Fact]
    public void Format_AudioDiscRippingAndLiveConversion_CombinesCurrentDiscAndProjectProgress()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.AcquiringSources,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 8,
                TotalCurrentSource: 16,
                CompletedProject: 16,
                TotalProject: 26,
                CurrentDisc: 2,
                TotalDiscs: 4,
                Percent: 80,
                WorkingFormat: "WAV"),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 7,
                Total: 26,
                Percent: 27,
                ActiveTrackNumbers: [1, 3, 4, 5, 7],
                BitrateKbps: 128,
                IsLive: true)
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal(
            "Audio-CD 2 von 4 wird gerippt • 08 / 16 WAV | Live-Konvertierung 7 / 26 AAC 128 kbps",
            result.TeletextText);
        Assert.Equal(
            "80 % | 16 / 26 gerippt | Konvertierung: 01, 03, 04, 05, 07",
            result.ProgressText);
        Assert.Equal(80, result.ProgressPercent);
    }

    [Fact]
    public void Format_FolderCopyCompletedAndLiveConversion_PreservesCompletedSourceContext()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                CompletedCurrentSource: 42,
                TotalCurrentSource: 42,
                CompletedProject: 42,
                TotalProject: 42,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 31,
                Total: 42,
                Percent: 74,
                ActiveTrackNumbers: [32, 33],
                BitrateKbps: 128,
                IsLive: true)
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal(
            "Kopieren abgeschlossen 42 / 42 | Live-Konvertierung 31 / 42 AAC 128 kbps",
            result.TeletextText);
        Assert.Equal(
            "74 % | 31 / 42 konvertiert | Aktive Jobs: 32, 33",
            result.ProgressText);
    }

    [Fact]
    public void Format_PausedConversion_KeepsContextAndAllowsCurrentCounts()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            IsPaused = true,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 8,
                TotalCurrentSource: 16,
                CompletedProject: 16,
                TotalProject: 26,
                CurrentDisc: 2,
                TotalDiscs: 4,
                Percent: 80,
                WorkingFormat: "WAV"),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 6,
                Total: 26,
                Percent: 23,
                ActiveTrackNumbers: [1, 3],
                BitrateKbps: 128,
                IsLive: true)
        };

        var result = _formatter.Format(snapshot);

        Assert.StartsWith("Pause • Audio-CD 2 von 4", result.TeletextText);
        Assert.Equal(
            "80 % | pausiert | 16 / 26 gerippt | Konvertierung: 01, 03",
            result.ProgressText);
    }

    [Fact]
    public void Format_Error_UsesOnlyConfirmedCountsAndNamesFailedTrackSeparately()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 11,
                TotalCurrentSource: 16,
                CompletedProject: 27,
                TotalProject: 49,
                CurrentDisc: 3,
                TotalDiscs: 4,
                Percent: 42,
                WorkingFormat: "WAV"),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 19,
                Total: 49,
                Percent: 39,
                BitrateKbps: 128),
            Error = new WorkflowErrorStatus(
                "Track 28 konnte nicht gerippt werden",
                FailedTrackOrFileNumber: 28)
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal("Fehler | Track 28 konnte nicht gerippt werden", result.TeletextText);
        Assert.Equal(
            "42 % | Fehler | Audio-CD 3 von 4 | 27 / 49 gerippt | Fehler bei Track 28 | 19 / 49 konvertiert",
            result.ProgressText);
    }



    [Fact]
    public void Format_AudioDiscRipError_KeepsDiscRipAndConversionContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.AcquiringSources,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 11,
                TotalCurrentSource: 16,
                CompletedProject: 27,
                TotalProject: 49,
                CurrentDisc: 3,
                TotalDiscs: 4,
                Percent: 42,
                WorkingFormat: "WAV"),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 19,
                Total: 49,
                Percent: 39,
                BitrateKbps: 128,
                IsLive: true),
            Error = new WorkflowErrorStatus(
                "Track 28 konnte nicht gerippt werden",
                FailedTrackOrFileNumber: 28)
        });

        Assert.Equal("Fehler | Track 28 konnte nicht gerippt werden", result.TeletextText);
        Assert.Equal(
            "42 % | Fehler | Audio-CD 3 von 4 | 27 / 49 gerippt | Fehler bei Track 28 | 19 / 49 konvertiert",
            result.ProgressText);
        Assert.Equal(42, result.ProgressPercent);
    }

    [Fact]
    public void Format_LocalCopyError_KeepsLastConfirmedCopyProgress()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.AcquiringSources,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                CompletedCurrentSource: 17,
                TotalCurrentSource: 42,
                CompletedProject: 17,
                TotalProject: 42,
                Percent: 40),
            Error = new WorkflowErrorStatus(
                "Datei 18 konnte nicht kopiert werden",
                FailedTrackOrFileNumber: 18)
        });

        Assert.Equal("Fehler | Datei 18 konnte nicht kopiert werden", result.TeletextText);
        Assert.Equal(
            "40 % | Fehler | 17 / 42 kopiert | Fehler bei Datei 18",
            result.ProgressText);
        Assert.Equal(40, result.ProgressPercent);
    }

    [Fact]
    public void Format_Mp3DiscCopyError_KeepsDiscAndConversionContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.AcquiringSources,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                CompletedCurrentSource: 73,
                TotalCurrentSource: 90,
                CompletedProject: 123,
                TotalProject: 180,
                CurrentDisc: 2,
                TotalDiscs: 3,
                Percent: 68),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 91,
                Total: 180,
                Percent: 51,
                BitrateKbps: 96,
                IsLive: true),
            Error = new WorkflowErrorStatus(
                "CD 2 konnte nicht vollständig kopiert werden",
                FailedTrackOrFileNumber: 124)
        });

        Assert.Equal("Fehler | CD 2 konnte nicht vollständig kopiert werden", result.TeletextText);
        Assert.Equal(
            "68 % | Fehler | MP3-CD 2 von 3 | 123 / 180 kopiert | Fehler bei Datei 124 | 91 / 180 konvertiert",
            result.ProgressText);
        Assert.Equal(68, result.ProgressPercent);
    }

    [Fact]
    public void Format_LocalConversionError_KeepsLastConfirmedConversionProgress()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.Converting,
            ConversionProgress = new ConversionActivityProgress(
                Completed: 18,
                Total: 42,
                Percent: 43,
                BitrateKbps: 128,
                IsLive: true),
            Error = new WorkflowErrorStatus(
                "Datei 19 konnte nicht konvertiert werden",
                FailedTrackOrFileNumber: 19)
        });

        Assert.Equal("Fehler | Datei 19 konnte nicht konvertiert werden", result.TeletextText);
        Assert.Equal(
            "43 % | Fehler | Fehler bei Datei 19 | 18 / 42 konvertiert",
            result.ProgressText);
        Assert.Equal(43, result.ProgressPercent);
    }

    [Fact]
    public void Format_Mp3DiscConversionError_KeepsCompletedImportAndConversionContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.Converting,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                CompletedCurrentSource: 60,
                TotalCurrentSource: 60,
                CompletedProject: 180,
                TotalProject: 180,
                CurrentDisc: 3,
                TotalDiscs: 3,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 91,
                Total: 180,
                Percent: 51,
                BitrateKbps: 96,
                IsLive: true),
            Error = new WorkflowErrorStatus(
                "Datei 92 konnte nicht konvertiert werden",
                FailedTrackOrFileNumber: 92)
        });

        Assert.Equal("Fehler | Datei 92 konnte nicht konvertiert werden", result.TeletextText);
        Assert.Equal(
            "51 % | Fehler | MP3-CD 3 von 3 | 180 / 180 kopiert | Fehler bei Datei 92 | 91 / 180 konvertiert",
            result.ProgressText);
        Assert.Equal(51, result.ProgressPercent);
    }


    [Fact]
    public void Format_AudioDiscConversionError_KeepsCompletedRipAndConversionContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.Converting,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 26,
                TotalCurrentSource: 26,
                CompletedProject: 26,
                TotalProject: 26,
                CurrentDisc: 4,
                TotalDiscs: 4,
                Percent: 100,
                WorkingFormat: "WAV",
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 7,
                Total: 26,
                Percent: 27,
                BitrateKbps: 128,
                IsLive: false),
            Error = new WorkflowErrorStatus(
                "Track 8 konnte nicht konvertiert werden",
                FailedTrackOrFileNumber: 8)
        });

        Assert.Equal("Fehler | Track 8 konnte nicht konvertiert werden", result.TeletextText);
        Assert.Equal(
            "27 % | Fehler | Audio-CD 4 von 4 | 26 / 26 gerippt | Fehler bei Track 8 | 7 / 26 konvertiert",
            result.ProgressText);
        Assert.Equal(27, result.ProgressPercent);
    }

    [Fact]
    public void Format_LocalMergeError_KeepsLastMergeProgress()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.Merging,
            MergeProgress = new MergeProgress(11, 42, 26),
            Error = new WorkflowErrorStatus("Zusammenfügen fehlgeschlagen")
        });

        Assert.Equal("Fehler | Zusammenfügen fehlgeschlagen", result.TeletextText);
        Assert.Equal("26 % | Fehler | Datei 11 von 42", result.ProgressText);
        Assert.Equal(26, result.ProgressPercent);
    }

    [Fact]
    public void Format_AudioDiscMergeError_KeepsSourceAndMergeContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.Merging,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 26,
                TotalCurrentSource: 26,
                CompletedProject: 26,
                TotalProject: 26,
                CurrentDisc: 4,
                TotalDiscs: 4,
                Percent: 100,
                WorkingFormat: "WAV",
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            MergeProgress = new MergeProgress(13, 26, 50),
            Error = new WorkflowErrorStatus("Zusammenfügen der Audio-CD-Dateien fehlgeschlagen")
        });

        Assert.Equal("Fehler | Zusammenfügen der Audio-CD-Dateien fehlgeschlagen", result.TeletextText);
        Assert.Equal(
            "50 % | Fehler | Audio-CD 4 von 4 | 26 / 26 gerippt | Datei 13 von 26",
            result.ProgressText);
        Assert.Equal(50, result.ProgressPercent);
    }

    [Fact]
    public void Format_AudioDiscExportReconversion_KeepsCompletedRipContext()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.Converting,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 26,
                TotalCurrentSource: 26,
                CompletedProject: 26,
                TotalProject: 26,
                CurrentDisc: 4,
                TotalDiscs: 4,
                Percent: 100,
                WorkingFormat: "WAV",
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                Completed: 7,
                Total: 26,
                Percent: 27,
                ActiveTrackNumbers: [8, 9],
                BitrateKbps: 128,
                IsLive: false)
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal(
            "Audio-CD 4 von 4 fertig | Neu konvertieren 7 / 26 AAC 128 kbps",
            result.TeletextText);
        Assert.Equal(
            "27 % | 7 / 26 konvertiert | Aktive Jobs: 08, 09",
            result.ProgressText);
    }

    [Theory]
    [InlineData(689L * 1024 * 1024, "689 MB")]
    [InlineData(1288490189L, "1,2 GB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void FormatFileSize_UsesVersionOneDisplayRules(long bytes, string expected)
    {
        Assert.Equal(expected, WorkflowStatusFormatter.FormatFileSize(bytes));
    }

    [Fact]
    public void Format_SuccessfulAudioDiscExport_ShowsSourceChaptersSizeAndCompletion()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            IsSuccessfulExport = true,
            TotalSourceItems = 4,
            TotalChapters = 269,
            OutputFileSizeBytes = 1288490189L
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal(
            "4 Audio-CDs / 269 Kapitel 1,2 GB | Hörbuch erfolgreich erstellt.",
            result.TeletextText);
        Assert.Equal("100 % | fertig", result.ProgressText);
    }

    [Fact]
    public void Format_LoadedCompletedProject_OmitsAmbiguousOutputSize()
    {
        var snapshot = new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.Completed,
            IsLoadedProject = true,
            TotalSourceItems = 4,
            TotalChapters = 296,
            OutputFileSizeBytes = 1288490189L
        };

        var result = _formatter.Format(snapshot);

        Assert.Equal("4 MP3-CDs / 296 Kapitel | Bereit", result.TeletextText);
        Assert.Equal("100 % | geladenes Projekt", result.ProgressText);
        Assert.DoesNotContain("GB", result.TeletextText);
    }

    [Fact]
    public void Format_MergeAndMetadata_UseDistinctMessages()
    {
        var merge = _formatter.Format(new WorkflowStatusSnapshot
        {
            MergeProgress = new MergeProgress(11, 26, 42)
        });
        var metadata = _formatter.Format(new WorkflowStatusSnapshot
        {
            MergeProgress = new MergeProgress(26, 26, 100, IsWritingMetadata: true)
        });

        Assert.Equal("Zusammenfügen ...", merge.TeletextText);
        Assert.Equal("42 % | Datei 11 von 26", merge.ProgressText);
        Assert.Equal(
            "Zusammenfügen abgeschlossen, Metadaten werden geschrieben ...",
            metadata.TeletextText);
        Assert.Equal("100 % | Metadaten werden geschrieben ...", metadata.ProgressText);
    }

    [Fact]
    public void Format_LocalPresetChangePending_ShowsSourceChapterContextAndReadyProgress()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.Completed,
            IsPresetChangePending = true,
            TotalSourceItems = 65,
            TotalChapters = 65,
            ConversionProgress = new ConversionActivityProgress(
                0,
                65,
                0,
                Array.Empty<int>(),
                96,
                false,
                IsLive: false)
        });

        Assert.Equal(
            "65 Dateien / 65 Kapitel | Neu konvertieren • 0 / 65 AAC 96 kbps",
            result.TeletextText);
        Assert.Equal("0 % | bereit", result.ProgressText);
        Assert.Equal(0, result.ProgressPercent);
    }

    [Fact]
    public void Format_Rollback_UsesDedicatedHighPriorityState()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            Rollback = new WorkflowRollbackStatus(WorkflowRollbackPhase.Running),
            Error = null
        });

        Assert.Equal(
            "Erweiterung abgebrochen | Änderungen werden zurückgesetzt ...",
            result.TeletextText);
        Assert.Equal("Rollback läuft ...", result.ProgressText);
        Assert.True(result.IsProgressIndeterminate);
    }
    [Fact]
    public void Format_Mp3Ready_UsesDiscContextAndConversionColor()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            IsReadyToMerge = true,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                182,
                182,
                182,
                182,
                CurrentDisc: 2,
                TotalDiscs: 2,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            ConversionProgress = new ConversionActivityProgress(
                182,
                182,
                100,
                Array.Empty<int>(),
                64,
                true,
                IsLive: true)
        });

        Assert.Equal(
            "MP3-CD 2 von 2 fertig | 182 / 182 AAC 64 kbps Mono | Bereit zum Zusammenfügen",
            result.TeletextText);
        Assert.Equal("100 %", result.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Conversion, result.ProgressVisualKind);
    }

    [Fact]
    public void Format_MergeAborted_PreservesMergePercent()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.ReviewBeforeMerge,
            IsMergeAborted = true,
            MergeProgress = new MergeProgress(28, 182, 42)
        });

        Assert.Equal(
            "Zusammenfügen abgebrochen | Bereit zum Zusammenfügen",
            result.TeletextText);
        Assert.Equal("42 % | abgebrochen", result.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Merge, result.ProgressVisualKind);
    }

    [Fact]
    public void Format_Mp3SuccessfulExport_UsesDiscCountAndOutputSize()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.Completed,
            IsSuccessfulExport = true,
            TotalChapters = 182,
            OutputFileSizeBytes = 293L * 1024 * 1024,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                182,
                182,
                182,
                182,
                CurrentDisc: 2,
                TotalDiscs: 2,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true)
        });

        Assert.Equal(
            "2 MP3-CDs / 182 Kapitel 293 MB | Hörbuch erfolgreich erstellt.",
            result.TeletextText);
        Assert.Equal("100 % | fertig", result.ProgressText);
        Assert.Equal(WorkflowProgressVisualKind.Merge, result.ProgressVisualKind);
    }

    [Fact]
    public void Format_LocalMetadataError_KeepsCompletedMergeContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Folder,
            ProjectState = ProjectPipelineState.Merging,
            MergeProgress = new MergeProgress(42, 42, 100, IsWritingMetadata: true),
            Error = new WorkflowErrorStatus("Metadaten konnten nicht geschrieben werden")
        });

        Assert.Equal("Fehler | Metadaten konnten nicht geschrieben werden", result.TeletextText);
        Assert.Equal("100 % | Fehler | Datei 42 von 42", result.ProgressText);
        Assert.Equal(100, result.ProgressPercent);
    }

    [Fact]
    public void Format_Mp3DiscMetadataError_KeepsCompletedCopyAndMergeContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.Mp3Disc,
            ProjectState = ProjectPipelineState.Merging,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Copying,
                CompletedCurrentSource: 92,
                TotalCurrentSource: 92,
                CompletedProject: 182,
                TotalProject: 182,
                CurrentDisc: 2,
                TotalDiscs: 2,
                Percent: 100,
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            MergeProgress = new MergeProgress(182, 182, 100, IsWritingMetadata: true),
            Error = new WorkflowErrorStatus("Metadaten konnten nicht geschrieben werden")
        });

        Assert.Equal("Fehler | Metadaten konnten nicht geschrieben werden", result.TeletextText);
        Assert.Equal(
            "100 % | Fehler | MP3-CD 2 von 2 | 182 / 182 kopiert | Datei 182 von 182",
            result.ProgressText);
        Assert.Equal(100, result.ProgressPercent);
    }


    [Fact]
    public void Format_AudioDiscMetadataError_KeepsCompletedRipAndMergeContext()
    {
        var result = _formatter.Format(new WorkflowStatusSnapshot
        {
            ProjectKind = WorkflowProjectKind.AudioDisc,
            ProjectState = ProjectPipelineState.Merging,
            SourceProgress = new SourceAcquisitionProgress(
                SourceAcquisitionKind.Ripping,
                CompletedCurrentSource: 26,
                TotalCurrentSource: 26,
                CompletedProject: 26,
                TotalProject: 26,
                CurrentDisc: 4,
                TotalDiscs: 4,
                Percent: 100,
                WorkingFormat: "WAV",
                CurrentSourceFinished: true,
                AllSourcesFinished: true),
            MergeProgress = new MergeProgress(26, 26, 100, IsWritingMetadata: true),
            Error = new WorkflowErrorStatus("Metadaten konnten nicht geschrieben werden")
        });

        Assert.Equal("Fehler | Metadaten konnten nicht geschrieben werden", result.TeletextText);
        Assert.Equal(
            "100 % | Fehler | Audio-CD 4 von 4 | 26 / 26 gerippt | Datei 26 von 26",
            result.ProgressText);
        Assert.Equal(100, result.ProgressPercent);
    }

}
