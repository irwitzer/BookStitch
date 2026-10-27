using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Core.Validation;

public sealed class StandardCoverValidator
{
    private readonly StandardCoverPolicy _policy;

    public StandardCoverValidator(StandardCoverPolicy? policy = null)
    {
        _policy = policy ?? StandardCoverPolicy.Default;
    }

    public CoverImageIssue? ValidateInputLength(long inputBytes)
    {
        if (inputBytes <= _policy.MaxInputBytes)
        {
            return null;
        }

        return new CoverImageIssue(
            CoverImageIssueCode.InputTooLarge,
            CoverImageIssueSeverity.Error,
            new Dictionary<string, object?>
            {
                ["inputBytes"] = inputBytes,
                ["maxInputBytes"] = _policy.MaxInputBytes
            });
    }

    public StandardCoverValidationResult Validate(CoverImageInspectionResult inspection, long? inputBytes = null)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        var issues = new List<CoverImageIssue>(inspection.Issues);

        if (inputBytes is not null)
        {
            var lengthIssue = ValidateInputLength(inputBytes.Value);
            if (lengthIssue is not null)
            {
                issues.Add(lengthIssue);
            }
        }

        var usableSquare = Math.Min(inspection.OrientedPixelWidth, inspection.OrientedPixelHeight);
        var totalPixels = (long)inspection.OrientedPixelWidth * inspection.OrientedPixelHeight;

        if (inspection.IsSuccess)
        {
            if (totalPixels > _policy.MaxTotalPixels || inspection.PixelWidth > _policy.MaxDimension || inspection.PixelHeight > _policy.MaxDimension)
            {
                issues.Add(new CoverImageIssue(
                    CoverImageIssueCode.TooManyPixels,
                    CoverImageIssueSeverity.Error,
                    new Dictionary<string, object?>
                    {
                        ["totalPixels"] = totalPixels,
                        ["maxTotalPixels"] = _policy.MaxTotalPixels,
                        ["orientedPixelWidth"] = inspection.OrientedPixelWidth,
                        ["orientedPixelHeight"] = inspection.OrientedPixelHeight
                    }));
            }

            if (usableSquare < _policy.MinUsableSquarePixels)
            {
                issues.Add(new CoverImageIssue(
                    CoverImageIssueCode.ImageTooSmall,
                    CoverImageIssueSeverity.Error,
                    new Dictionary<string, object?>
                    {
                        ["usableSquarePixels"] = usableSquare,
                        ["minUsableSquarePixels"] = _policy.MinUsableSquarePixels
                    }));
            }

            if (inspection.FrameCount > 1)
            {
                issues.Add(new CoverImageIssue(
                    CoverImageIssueCode.MultipleFramesFirstFrameUsed,
                    CoverImageIssueSeverity.Warning,
                    new Dictionary<string, object?>
                    {
                        ["frameCount"] = inspection.FrameCount,
                        ["selectedFrame"] = 1
                    }));
            }
        }

        return new StandardCoverValidationResult(usableSquare, totalPixels, issues);
    }
}
