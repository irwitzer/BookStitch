using System.Windows.Media;
using CoverImageDrop.Core.Imaging;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageBitmapOrientationTests
{
    [TestMethod]
    public void Apply_ExifOrientationsOneThroughEight_MapAllCornersAndDimensions()
    {
        const int sourceWidth = 8;
        const int sourceHeight = 6;

        var cases = new[]
        {
            new OrientationCase(1, sourceWidth, sourceHeight, Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow),
            new OrientationCase(2, sourceWidth, sourceHeight, Colors.Lime, Colors.Red, Colors.Yellow, Colors.Blue),
            new OrientationCase(3, sourceWidth, sourceHeight, Colors.Yellow, Colors.Blue, Colors.Lime, Colors.Red),
            new OrientationCase(4, sourceWidth, sourceHeight, Colors.Blue, Colors.Yellow, Colors.Red, Colors.Lime),
            new OrientationCase(5, sourceHeight, sourceWidth, Colors.Red, Colors.Blue, Colors.Lime, Colors.Yellow),
            new OrientationCase(6, sourceHeight, sourceWidth, Colors.Blue, Colors.Red, Colors.Yellow, Colors.Lime),
            new OrientationCase(7, sourceHeight, sourceWidth, Colors.Yellow, Colors.Lime, Colors.Blue, Colors.Red),
            new OrientationCase(8, sourceHeight, sourceWidth, Colors.Lime, Colors.Yellow, Colors.Red, Colors.Blue)
        };

        foreach (var testCase in cases)
        {
            var source = TestImageFactory.CreateQuadrantBitmap(
                sourceWidth,
                sourceHeight);

            var oriented = CoverImageBitmapOrientation.Apply(
                source,
                testCase.Orientation);

            Assert.AreEqual(
                testCase.ExpectedWidth,
                oriented.PixelWidth,
                $"Orientation {testCase.Orientation} width.");
            Assert.AreEqual(
                testCase.ExpectedHeight,
                oriented.PixelHeight,
                $"Orientation {testCase.Orientation} height.");

            AssertCorner(oriented, 0, 0, testCase.TopLeft, testCase.Orientation, "top-left");
            AssertCorner(
                oriented,
                oriented.PixelWidth - 1,
                0,
                testCase.TopRight,
                testCase.Orientation,
                "top-right");
            AssertCorner(
                oriented,
                0,
                oriented.PixelHeight - 1,
                testCase.BottomLeft,
                testCase.Orientation,
                "bottom-left");
            AssertCorner(
                oriented,
                oriented.PixelWidth - 1,
                oriented.PixelHeight - 1,
                testCase.BottomRight,
                testCase.Orientation,
                "bottom-right");
        }
    }

    [TestMethod]
    public void SwapsDimensions_MatchesExifOrientationsOneThroughEight()
    {
        for (ushort orientation = 1; orientation <= 8; orientation++)
        {
            Assert.AreEqual(
                orientation is 5 or 6 or 7 or 8,
                CoverImageBitmapOrientation.SwapsDimensions(orientation),
                $"Unexpected dimension-swap classification for EXIF orientation {orientation}.");
        }
    }

    private static void AssertCorner(
        System.Windows.Media.Imaging.BitmapSource bitmap,
        int x,
        int y,
        Color expected,
        ushort orientation,
        string corner)
    {
        var actual = TestImageFactory.ReadPixel(bitmap, x, y);
        Assert.AreEqual(
            expected,
            actual,
            $"EXIF orientation {orientation} produced {actual} at {corner}; expected {expected}.");
    }

    private sealed record OrientationCase(
        ushort Orientation,
        int ExpectedWidth,
        int ExpectedHeight,
        Color TopLeft,
        Color TopRight,
        Color BottomLeft,
        Color BottomRight);
}
