using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageEncoderTests
{
    [TestMethod]
    public void EncodeJpegAdaptive_KeepsQuality94AtOneMegabyte()
    {
        var qualities = new List<int>();

        var result = CoverImageEncoder.EncodeJpegAdaptive(quality =>
        {
            qualities.Add(quality);
            return new byte[CoverImageEncoder.Quality94MaxBytes];
        });

        Assert.AreEqual(94, result.JpegQuality);
        Assert.AreEqual(1, result.EncodeAttempts);
        CollectionAssert.AreEqual(new[] { 94 }, qualities);
    }

    [TestMethod]
    public void EncodeJpegAdaptive_UsesQuality92WhenQuality94ExceedsOneMegabyte()
    {
        var qualities = new List<int>();

        var result = CoverImageEncoder.EncodeJpegAdaptive(quality =>
        {
            qualities.Add(quality);
            return quality == 94
                ? new byte[CoverImageEncoder.Quality94MaxBytes + 1]
                : new byte[CoverImageEncoder.Quality92MaxBytes];
        });

        Assert.AreEqual(92, result.JpegQuality);
        Assert.AreEqual(2, result.EncodeAttempts);
        CollectionAssert.AreEqual(new[] { 94, 92 }, qualities);
    }

    [TestMethod]
    public void EncodeJpegAdaptive_UsesQuality90WhenQuality92StillExceedsFourMegabytes()
    {
        var qualities = new List<int>();

        var result = CoverImageEncoder.EncodeJpegAdaptive(quality =>
        {
            qualities.Add(quality);
            return quality switch
            {
                94 => new byte[CoverImageEncoder.Quality94MaxBytes + 1],
                92 => new byte[CoverImageEncoder.Quality92MaxBytes + 1],
                _ => new byte[123]
            };
        });

        Assert.AreEqual(90, result.JpegQuality);
        Assert.AreEqual(3, result.EncodeAttempts);
        Assert.AreEqual(123, result.Bytes.Length);
        CollectionAssert.AreEqual(new[] { 94, 92, 90 }, qualities);
    }
}
