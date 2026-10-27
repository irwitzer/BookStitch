using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelineSessionOptionsTests
{
    [TestMethod]
    public async Task OutputOptionsAfterLoad_RecomputePlanIssuesAndFinalizeFormatWithoutChangingCropSize()
    {
        var bytes = TestImageFactory.CreateSolidPng(1600, 1600, Colors.MediumPurple);
        var service = new CoverImagePipelineService();
        var load = await service.LoadAsync(DropImageData.FromBytes(bytes, "cover.png"));

        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));
        var session = load.Session!;
        var inspection = session.Inspection;
        var originalCropSize = session.CropState.Rect.Width;

        Assert.AreEqual(2000, session.ProcessingPlan.TargetSizePixels);
        Assert.IsTrue(session.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));

        session.OutputOptions = new StandardCoverOutputOptions
        {
            TargetSize = CoverTargetSizeMode.Size1000,
            Format = CoverOutputFormat.Png,
            Transparency = JpegTransparencyMode.Black
        };

        Assert.AreSame(inspection, session.Inspection);
        Assert.AreEqual(1000, session.ProcessingPlan.TargetSizePixels);
        Assert.AreEqual(CoverOutputFormat.Png, session.ProcessingPlan.Format);
        Assert.AreEqual(JpegTransparencyMode.Black, session.ProcessingPlan.Transparency);
        Assert.AreEqual(originalCropSize, session.CropState.Rect.Width);
        Assert.AreEqual(originalCropSize, session.CropState.Rect.Height);
        Assert.AreEqual(1000, session.CropState.TargetSizePixels);
        Assert.IsFalse(session.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));

        var final = await session.FinalizeAsync();

        Assert.IsTrue(final.IsSuccess, FormatIssues(final.Issues));
        Assert.IsNotNull(final.Output);
        Assert.AreEqual(CoverOutputFormat.Png, final.Output.Format);
        Assert.AreEqual(1000, final.Output.Width);
        Assert.AreEqual(1000, final.Output.Height);

        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task FinalizeAsync_PreservesSessionAndOutputIssuesWithoutDuplicates()
    {
        var bytes = TestImageFactory.CreateHalfTransparentPng(1600, 1600, Colors.MediumPurple);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "transparent.png"));

        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));
        await using var session = load.Session!;

        Assert.AreEqual(
            1,
            session.Issues.Count(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));

        var final = await session.FinalizeAsync();

        Assert.IsTrue(final.IsSuccess, FormatIssues(final.Issues));
        Assert.AreEqual(
            1,
            final.Issues.Count(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));
        Assert.AreEqual(
            1,
            final.Issues.Count(issue => issue.Code == CoverImageIssueCode.TransparencyFlattened));
    }

    [TestMethod]
    public async Task CropPositionRemainsFixedSizeAcrossOutputOptionChanges()
    {
        var bytes = TestImageFactory.CreateSolidPng(2200, 1600, Colors.DarkCyan);
        var load = await new CoverImagePipelineService().LoadAsync(
            DropImageData.FromBytes(bytes, "wide.png"));

        Assert.IsTrue(load.IsSuccess, FormatIssues(load.Issues));
        var session = load.Session!;
        session.UpdateCropPosition(450, 0);
        var before = session.CropState.Rect;

        session.OutputOptions = session.OutputOptions with
        {
            TargetSize = CoverTargetSizeMode.Size1000
        };

        Assert.AreEqual(before.X, session.CropState.Rect.X);
        Assert.AreEqual(before.Y, session.CropState.Rect.Y);
        Assert.AreEqual(before.Width, session.CropState.Rect.Width);
        Assert.AreEqual(before.Height, session.CropState.Rect.Height);

        session.OutputOptions = session.OutputOptions with
        {
            TargetSize = CoverTargetSizeMode.Size2000
        };

        Assert.AreEqual(before, session.CropState.Rect);
        Assert.IsTrue(session.Issues.Any(issue => issue.Code == CoverImageIssueCode.UpscalingRequired));

        await session.DisposeAsync();
    }

    private static string FormatIssues(IEnumerable<CoverImageIssue> issues)
    {
        return string.Join(
            Environment.NewLine,
            issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.TechnicalCause}"));
    }
}
