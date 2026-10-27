namespace CoverImageDrop.Acquisition;

/// <summary>
/// Profilunabhaengige Ressourcenschutzgrenzen fuer fremde Drop-/URL-Daten.
/// Diese Grenzen sind keine StandardCover-Qualitaetsregeln.
/// </summary>
public sealed record DropImageAcquisitionSafetyLimits
{
    public const long DefaultMaxRemoteDownloadBytes = 512L * 1024 * 1024;
    public const long DefaultMaxTextArtifactBytes = 256L * 1024 * 1024;
    public const long DefaultMaxEmbeddedDataUrlBytes = 96L * 1024 * 1024;
    public const long DefaultMaxBitmapPixels = 100_000_000;

    public static DropImageAcquisitionSafetyLimits Default { get; } = new();

    /// <summary>
    /// Maximale Anzahl Bytes fuer einen einzelnen HTTP-/HTTPS-Download.
    /// Gilt auch fuer PassThrough, damit Remote-Acquisition niemals unbegrenzt
    /// Temp-Speicher belegen kann. Lokale Dateien sind davon nicht betroffen.
    /// </summary>
    public long MaxRemoteDownloadBytes { get; init; } = DefaultMaxRemoteDownloadBytes;

    /// <summary>
    /// Maximale Groesse eines einzelnen Textartefakts, bevor es fuer
    /// Acquisition/URL-Analyse verworfen wird. Binary-Formate werden
    /// unabhaengig davon nicht als Text materialisiert.
    /// </summary>
    public long MaxTextArtifactBytes { get; init; } = DefaultMaxTextArtifactBytes;

    /// <summary>
    /// Maximale dekodierte Payloadgroesse einer eingebetteten Data-URL.
    /// Data-URLs werden erst materialisiert, wenn ihr Kandidat wirklich
    /// verwendet wird.
    /// </summary>
    public long MaxEmbeddedDataUrlBytes { get; init; } = DefaultMaxEmbeddedDataUrlBytes;

    /// <summary>
    /// Maximale Pixelanzahl eines WPF-Bitmap-Fallbacks, der als verlustfreies
    /// PNG materialisiert werden darf.
    /// </summary>
    public long MaxBitmapPixels { get; init; } = DefaultMaxBitmapPixels;

    internal void Validate()
    {
        ValidatePositive(MaxRemoteDownloadBytes, nameof(MaxRemoteDownloadBytes));
        ValidatePositive(MaxTextArtifactBytes, nameof(MaxTextArtifactBytes));
        ValidatePositive(MaxEmbeddedDataUrlBytes, nameof(MaxEmbeddedDataUrlBytes));
        ValidatePositive(MaxBitmapPixels, nameof(MaxBitmapPixels));
    }

    private static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} muss positiv sein.");
        }
    }
}
