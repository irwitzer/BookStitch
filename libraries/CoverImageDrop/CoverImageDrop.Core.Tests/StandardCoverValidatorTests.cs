using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Policy;
using CoverImageDrop.Core.Validation;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class StandardCoverValidatorTests
{
    private readonly StandardCoverValidator _validator = new();

    [TestMethod]
    [DataRow(499, 2000, false)]
    [DataRow(500, 3000, true)]
    [DataRow(480, 2000, false)]
    [DataRow(500, 500, true)]
    public void Validate_AppliesMinimumUsableSquareRule(int width, int height, bool expectedValid)
    {
        var result = _validator.Validate(CreateInspection(width, height));

        Assert.AreEqual(expectedValid, result.IsValid);
        Assert.AreEqual(Math.Min(width, height), result.UsableSquarePixels);
        Assert.AreEqual(!expectedValid, result.Issues.Any(issue => issue.Code == CoverImageIssueCode.ImageTooSmall));
    }

    [TestMethod]
    public void Validate_AppliesEightyMegapixelRuleWithoutPerDimensionLimit()
    {
        var exactlyAtLimit = _validator.Validate(CreateInspection(20_000, 4_000));
        var aboveLimit = _validator.Validate(CreateInspection(20_001, 4_000));

        Assert.IsTrue(exactlyAtLimit.IsValid);
        Assert.IsFalse(aboveLimit.IsValid);
        Assert.IsTrue(aboveLimit.Issues.Any(issue => issue.Code == CoverImageIssueCode.TooManyPixels));
    }

    [TestMethod]
    public void ValidateInputLength_AppliesEightyMegabyteRule()
    {
        var policy = StandardCoverPolicy.Default;

        Assert.IsNull(_validator.ValidateInputLength(policy.MaxInputBytes));
        var issue = _validator.ValidateInputLength(policy.MaxInputBytes + 1);

        Assert.IsNotNull(issue);
        Assert.AreEqual(CoverImageIssueCode.InputTooLarge, issue.Code);
        Assert.AreEqual(CoverImageIssueSeverity.Error, issue.Severity);
    }

    [TestMethod]
    public void Validate_MultipleFramesProducesStructuredWarning()
    {
        var inspection = CreateInspection(1000, 1000) with { FrameCount = 2 };

        var result = _validator.Validate(inspection);

        Assert.IsTrue(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.MultipleFramesFirstFrameUsed &&
            issue.Severity == CoverImageIssueSeverity.Warning));
    }

    [TestMethod]
    public void Validate_PreservesInspectionFailureWithoutAddingMisleadingSizeFailure()
    {
        var inspectionIssue = new CoverImageIssue(
            CoverImageIssueCode.ImageDecodeFailed,
            CoverImageIssueSeverity.Error);
        var inspection = CreateInspection(0, 0) with { Issues = [inspectionIssue] };

        var result = _validator.Validate(inspection);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Contains(inspectionIssue));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.ImageTooSmall));
    }

    private static CoverImageInspectionResult CreateInspection(int width, int height)
    {
        return new CoverImageInspectionResult(
            "test.png",
            DetectedImageFormat.Png,
            "PNG Decoder",
            width,
            height,
            width,
            height,
            1,
            false,
            []);
    }
}
