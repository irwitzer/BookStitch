using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class EmbeddedCoverServiceTests
{
    [Fact]
    public void Tiny_embedded_cover_is_skipped_and_next_eligible_cover_is_used()
    {
        using var folder = new TemporaryFolder();
        var tiny = folder.CreateFile("tiny.mp3");
        var valid = folder.CreateFile("valid.mp3");
        var reader = new RecordingCoverReader();
        reader.Covers[tiny] = new EmbeddedCoverData(CoverTestImages.Create(199, 300, jpeg: true), "image/jpeg");
        reader.Covers[valid] = new EmbeddedCoverData(ValidJpegBytes(), "image/jpeg");
        var result = new EmbeddedCoverService(reader).ExtractFirstValidCover([tiny, valid], folder.Path);
        Assert.NotNull(result);
        Assert.Equal(reader.Covers[valid]!.Bytes, File.ReadAllBytes(result!));
        Assert.Equal(new[] { tiny, valid }, reader.ReadFiles);
    }

    [Fact]
    public void Oversized_embedded_payload_is_skipped_and_next_valid_cover_is_used()
    {
        using var folder = new TemporaryFolder();
        var oversized = folder.CreateFile("oversized.mp3");
        var valid = folder.CreateFile("valid.mp3");
        var reader = new RecordingCoverReader();
        reader.Covers[oversized] = new EmbeddedCoverData(new byte[CoverInputSafety.MaxEncodedBytes + 1], "image/jpeg");
        reader.Covers[valid] = new EmbeddedCoverData(ValidJpegBytes(), "image/jpeg");
        var result = new EmbeddedCoverService(reader).ExtractFirstValidCover([oversized, valid], folder.Path);
        Assert.NotNull(result);
        Assert.Equal(reader.Covers[valid]!.Bytes, File.ReadAllBytes(result!));
    }

    [Fact]
    public void User_cover_removal_records_identical_content_and_manual_selection_restores_it()
    {
        using var folder = new TemporaryFolder();
        var path = folder.CreateFile("cover.jpg");
        File.WriteAllBytes(path, ValidJpegBytes());
        var metadata = new BookStitch.Models.ProjectMetadata();
        EmbeddedCoverService.RecordUserRemoval(metadata, "");
        Assert.Empty(metadata.GetSuppressedValues(BookStitch.Models.ProjectMetadataField.Cover));
        EmbeddedCoverService.RecordUserRemoval(metadata, path);
        Assert.Single(metadata.GetSuppressedValues(BookStitch.Models.ProjectMetadataField.Cover));
        EmbeddedCoverService.RecordUserSelection(metadata, path);
        Assert.Empty(metadata.GetSuppressedValues(BookStitch.Models.ProjectMetadataField.Cover));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtractFirstValidCover_SuppressedIdenticalBytesAreSkippedButDifferentCoverIsAllowed(bool hasEvidence)
    {
        using var folder = new TemporaryFolder();
        var deleted = folder.CreateFile("deleted.mp3");
        var next = folder.CreateFile("next.mp3");
        var bytes = ValidJpegBytes();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var reader = new RecordingCoverReader();
        reader.Covers[deleted] = new EmbeddedCoverData(bytes, "image/jpeg");
        // Appending a byte changes the existing byte fingerprint without changing the decoded image.
        reader.Covers[next] = new EmbeddedCoverData(bytes.Concat(new byte[] { 0 }).ToArray(), "image/jpeg");

        var result = new EmbeddedCoverService(reader).ExtractFirstValidCover(
            [new(deleted, hasEvidence, hasEvidence ? fingerprint : ""), new(next)], folder.Path, [fingerprint]);

        Assert.NotNull(result);
        Assert.Equal(reader.Covers[next]!.Bytes, File.ReadAllBytes(result!));
        Assert.Equal(hasEvidence ? new[] { next } : new[] { deleted, next }, reader.ReadFiles);
    }

    [Fact]
    public void ExtractFirstValidCover_WhenEvidenceFingerprintIsDuplicate_SkipsDuplicateCandidate()
    {
        using var folder = new TemporaryFolder();
        var first = folder.CreateFile("first.mp3");
        var duplicate = folder.CreateFile("duplicate.mp3");
        var valid = folder.CreateFile("valid.mp3");
        var reader = new RecordingCoverReader
        {
            Covers =
            {
                [first] = new EmbeddedCoverData([1, 2, 3], "image/jpeg"),
                [duplicate] = new EmbeddedCoverData(ValidJpegBytes(), "image/jpeg"),
                [valid] = new EmbeddedCoverData(ValidJpegBytes(), "image/jpeg")
            }
        };
        var service = new EmbeddedCoverService(reader);

        var result = service.ExtractFirstValidCover(
            [
                new EmbeddedCoverExtractionCandidate(first, HasCoverEvidence: true, Fingerprint: "same"),
                new EmbeddedCoverExtractionCandidate(duplicate, HasCoverEvidence: true, Fingerprint: "same"),
                new EmbeddedCoverExtractionCandidate(valid, HasCoverEvidence: true, Fingerprint: "other")
            ],
            folder.Path);

        Assert.NotNull(result);
        Assert.DoesNotContain(duplicate, reader.ReadFiles);
        Assert.Equal([first, valid], reader.ReadFiles);
    }

    [Fact]
    public void ExtractFirstValidCover_WhenEvidenceCandidatesAreInvalid_StopsAfterFiveDistinctFailures()
    {
        using var folder = new TemporaryFolder();
        var files = Enumerable.Range(1, 7)
            .Select(index => folder.CreateFile($"track{index}.mp3"))
            .ToArray();
        var reader = new RecordingCoverReader();
        reader.Covers[files[6]] = new EmbeddedCoverData(ValidJpegBytes(), "image/jpeg");
        var service = new EmbeddedCoverService(reader);

        var result = service.ExtractFirstValidCover(
            files.Select((file, index) => new EmbeddedCoverExtractionCandidate(
                file,
                HasCoverEvidence: true,
                Fingerprint: "invalid-" + index)),
            folder.Path);

        Assert.Null(result);
        Assert.Equal(files.Take(5), reader.ReadFiles);
    }

    [Fact]
    public void ExtractFirstValidCover_WhenNoSnapshotEvidenceExists_DoesNotApplyInvalidCandidateLimit()
    {
        using var folder = new TemporaryFolder();
        var files = Enumerable.Range(1, 7)
            .Select(index => folder.CreateFile($"track{index}.mp3"))
            .ToArray();
        var reader = new RecordingCoverReader();
        reader.Covers[files[6]] = new EmbeddedCoverData(ValidJpegBytes(), "image/png");
        var service = new EmbeddedCoverService(reader);

        var result = service.ExtractFirstValidCover(files, folder.Path);

        Assert.NotNull(result);
        Assert.EndsWith(".png", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(files, reader.ReadFiles);
    }

    private static byte[] ValidJpegBytes()
    {
        return CoverTestImages.Create(600, 600, jpeg: true);
    }

    private sealed class RecordingCoverReader : TagMetadataReader
    {
        public Dictionary<string, EmbeddedCoverData?> Covers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ReadFiles { get; } = [];

        public override EmbeddedCoverData? ReadFirstEmbeddedCover(string filePath)
        {
            ReadFiles.Add(filePath);
            return Covers.TryGetValue(filePath, out var cover)
                ? cover
                : null;
        }
    }
}
