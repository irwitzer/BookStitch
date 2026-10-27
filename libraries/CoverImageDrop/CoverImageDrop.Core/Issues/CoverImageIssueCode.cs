namespace CoverImageDrop.Core.Issues;

public enum CoverImageIssueCode
{
    ImageTooSmall,
    InputTooLarge,
    TooManyPixels,
    UnsupportedImageFormat,
    DecoderUnavailable,
    WebPDecoderUnavailable,
    ImageDecodeFailed,
    UpscalingRequired,
    MultipleFramesFirstFrameUsed,
    TransparencyFlattened,
    ColorProfileConversionFailed,
    SourceDetectionUncertain,
    NoImagePayloadFound,
    PersistenceFailed,
    FileNameAdjusted,
    MetadataProbeFailed,
    BitmapFallbackEncodedAsPng
}
