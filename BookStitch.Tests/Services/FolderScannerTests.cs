using System.IO;
using Xunit;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;

namespace BookStitch.Tests.Services;

public sealed class FolderScannerTests
{
    [Fact]
    public void Scan_Orders_Files_By_Disc_Track_And_Natural_File_Name()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(@"CD 10\001 Final.mp3");
        folder.CreateFile(@"CD 2\010 Ten.mp3");
        folder.CreateFile(@"CD 2\002 Two.mp3");
        folder.CreateFile(@"CD 1\009 Nine.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(
            [
                "009 Nine.mp3",
                "002 Two.mp3",
                "010 Ten.mp3",
                "001 Final.mp3"
            ],
            tracks.Select(track => track.FileName).ToArray());

        Assert.Equal([1, 2, 3, 4], tracks.Select(track => track.Index).ToArray());
    }

    [Theory]
    [InlineData("Track 001 Intro.mp3", 1, "Intro")]
    [InlineData("Track01 Intro.mp3", 1, "Intro")]
    [InlineData("Kapitel 002 Anfang.mp3", 2, "Anfang")]
    [InlineData("Chapter 003 Middle.mp3", 3, "Middle")]
    [InlineData("Titel 004 Ende.mp3", 4, "Ende")]
    [InlineData("005 Outro.mp3", 5, "Outro")]
    public void Scan_Recognizes_Common_Track_Number_Patterns(string fileName, int expectedTrackNumber, string expectedChapterTitle)
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(fileName);

        var track = Assert.Single(new FolderScanner().Scan(folder.Path));

        Assert.Equal(expectedTrackNumber, track.TrackNumber);
        Assert.Equal(expectedChapterTitle, track.ChapterTitle);
    }

    [Fact]
    public void Scan_Does_Not_Warn_For_Distinct_Number_Suffixes()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("003A Teil A.mp3");
        folder.CreateFile("003B Teil B.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Warns_For_Real_Duplicate_Track_Numbers()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("003 Teil Eins.mp3");
        folder.CreateFile("003 Teil Zwei.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.Contains("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Warns_For_Missing_Track_Numbers_When_Sequence_Is_Almost_Complete()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Eins.mp3");
        folder.CreateFile("002 Zwei.mp3");
        folder.CreateFile("004 Vier.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Contains(tracks, track => track.Warning.Contains("Tracknummer 003 fehlt möglicherweise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Warns_For_Duplicate_Chapter_Titles()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Gleicher Titel.mp3");
        folder.CreateFile("002 Gleicher Titel.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.Contains("Doppelter Kapitelname", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Includes_Currently_Supported_Input_Formats()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Eins.mp3");
        folder.CreateFile("002 Zwei.m4a");
        folder.CreateFile("003 Drei.m4b");
        folder.CreateFile("004 Vier.aac");
        folder.CreateFile("005 Fuenf.wav");
        folder.CreateFile("006 Sechs.flac");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(6, tracks.Count);
        Assert.Equal(["MP3", "M4A", "M4B", "AAC", "WAV", "FLAC"], tracks.Select(track => track.Extension).ToArray());
    }
    [Fact]
    public void Scan_Can_Prefer_File_Name_Track_Numbers_For_Imported_Disc_Projects()
    {
        using var folder = new TemporaryFolder();
        CreateMp3WithId3v1Track(folder, "001 Eins.mp3", 1);
        CreateMp3WithId3v1Track(folder, "002 Zwei.mp3", 1);
        CreateMp3WithId3v1Track(folder, "003 Drei.mp3", 1);

        var fileNamePreferredTracks = new FolderScanner().Scan(folder.Path, TrackNumberPreference.FileName);

        Assert.Equal([1, 2, 3], fileNamePreferredTracks.Select(track => track.TrackNumber).ToArray());
        Assert.All(fileNamePreferredTracks, track =>
            Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Applies_File_Filter_Before_Duplicate_Warnings()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(@"CD 01\001 Original.mp3");
        folder.CreateFile(@"converted\AAC Stereo 128 kbps\001 Original.m4a");

        var tracks = new FolderScanner().Scan(
            folder.Path,
            TrackNumberPreference.FileName,
            path => !path.Contains($"{Path.DirectorySeparatorChar}converted{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var track = Assert.Single(tracks);
        Assert.Equal("001 Original.mp3", track.FileName);
        Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Scan_Uses_File_Name_Track_Numbers_When_Tags_Do_Not_Provide_Track_Numbers()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("111_Eckhart_Omama.mp3");
        folder.CreateFile("020_Eckhart_Omama.mp3");
        folder.CreateFile("003_Eckhart_Omama.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(["003_Eckhart_Omama.mp3", "020_Eckhart_Omama.mp3", "111_Eckhart_Omama.mp3"],
            tracks.Select(track => track.FileName).ToArray());
        Assert.Equal([3, 20, 111], tracks.Select(track => track.TrackNumber).ToArray());
    }

    private static void CreateMp3WithId3v1Track(TemporaryFolder folder, string fileName, byte trackNumber)
    {
        var path = folder.CreateFile(fileName);
        var tag = new byte[128];
        tag[0] = (byte)'T';
        tag[1] = (byte)'A';
        tag[2] = (byte)'G';
        tag[125] = 0;
        tag[126] = trackNumber;
        File.WriteAllBytes(path, tag);
    }

}
