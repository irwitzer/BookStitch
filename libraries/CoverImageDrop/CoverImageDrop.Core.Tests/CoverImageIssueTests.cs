using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageIssueTests
{
    [TestMethod]
    public void Issue_CarriesStableCodeSeverityAndStructuredData()
    {
        IReadOnlyDictionary<string, object?> data = new Dictionary<string, object?>
        {
            ["actualPixels"] = 499,
            ["requiredPixels"] = 500
        };

        var issue = new CoverImageIssue(
            CoverImageIssueCode.ImageTooSmall,
            CoverImageIssueSeverity.Error,
            data,
            TechnicalCause: "Synthetic test cause",
            SuggestedAction: "Choose a larger image");

        Assert.AreEqual(CoverImageIssueCode.ImageTooSmall, issue.Code);
        Assert.AreEqual(CoverImageIssueSeverity.Error, issue.Severity);
        Assert.AreSame(data, issue.Data);
        Assert.AreEqual("Synthetic test cause", issue.TechnicalCause);
        Assert.AreEqual("Choose a larger image", issue.SuggestedAction);
    }

    [TestMethod]
    public void IssueCode_DefinesRequiredV1ContractCodes()
    {
        var requiredCodes = new[]
        {
            CoverImageIssueCode.ImageTooSmall,
            CoverImageIssueCode.InputTooLarge,
            CoverImageIssueCode.TooManyPixels,
            CoverImageIssueCode.UnsupportedImageFormat,
            CoverImageIssueCode.DecoderUnavailable,
            CoverImageIssueCode.WebPDecoderUnavailable,
            CoverImageIssueCode.ImageDecodeFailed,
            CoverImageIssueCode.UpscalingRequired,
            CoverImageIssueCode.MultipleFramesFirstFrameUsed,
            CoverImageIssueCode.TransparencyFlattened,
            CoverImageIssueCode.SourceDetectionUncertain,
            CoverImageIssueCode.NoImagePayloadFound,
            CoverImageIssueCode.PersistenceFailed,
            CoverImageIssueCode.FileNameAdjusted
        };

        foreach (var code in requiredCodes)
        {
            Assert.IsTrue(Enum.IsDefined(typeof(CoverImageIssueCode), code));
        }
    }
}
