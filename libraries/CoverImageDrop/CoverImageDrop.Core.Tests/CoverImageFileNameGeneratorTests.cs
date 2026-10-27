using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Naming;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageFileNameGeneratorTests
{
    [TestMethod]
    public void Timestamp_UsesLocalTimeAndExactFormat()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "NamingTest+02",
            TimeSpan.FromHours(2),
            "NamingTest+02",
            "NamingTest+02");
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 20, 16, 36, 42, 317, TimeSpan.Zero),
            timeZone);
        var generator = new CoverImageFileNameGenerator(timeProvider);

        var result = generator.Generate("jpg");

        Assert.AreEqual("2026.08.20 18.36.42.317.jpg", result.FileName);
        Assert.AreEqual("2026.08.20 18.36.42.317", result.BaseName);
        Assert.AreEqual(".jpg", result.Extension);
        Assert.IsFalse(result.WasAdjusted);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void HostProvidedName_ReplacesKnownImageExtensionWithActualExtension()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            ".png",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = "Mein Cover.JPG"
            });

        Assert.AreEqual("Mein Cover.png", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
        Assert.AreEqual(CoverImageIssueCode.FileNameAdjusted, result.Issues.Single().Code);
    }

    [TestMethod]
    public void HostProvidedName_DoesNotStripLegitimateDotSuffix()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            "jpg",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = "Band 1.2"
            });

        Assert.AreEqual("Band 1.2.jpg", result.FileName);
        Assert.IsFalse(result.WasAdjusted);
    }

    [TestMethod]
    public void HostProvidedName_ReplacesInvalidCharactersAndTrimsTrailingDotsAndSpaces()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            "jpeg",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = "Mein:Cover?.  "
            });

        Assert.AreEqual("Mein_Cover_.jpeg", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
    }

    [TestMethod]
    public void HostProvidedName_AdjustsWindowsReservedDeviceName()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            "png",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = "CON.jpg"
            });

        Assert.AreEqual("_CON.png", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
    }

    [TestMethod]
    public void HostProvidedName_PreservesNormalUnicodeCharacters()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            "webp",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = "Hörbuch – Übermaß"
            });

        Assert.AreEqual("Hörbuch – Übermaß.webp", result.FileName);
        Assert.IsFalse(result.WasAdjusted);
    }

    [TestMethod]
    public void OriginalFileName_UsesOptionalOriginalNameAndCorrectsExtension()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(
            "jpg",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.OriginalFileName,
                OriginalFileName = "original.PNG"
            });

        Assert.AreEqual("original.jpg", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
    }

    [TestMethod]
    public void HostProvidedName_TruncatesToWindowsComponentLimitAndPreservesActualExtension()
    {
        var generator = new CoverImageFileNameGenerator();
        var requested = new string('x', 400);

        var result = generator.Generate(
            "jpeg",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.HostProvidedName,
                HostProvidedName = requested
            });

        Assert.AreEqual(CoverImageFileNameGenerator.MaxFileNameCharacters, result.FileName.Length);
        Assert.EndsWith(".jpeg", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
        Assert.IsTrue(result.Issues.Single().Data?["Reasons"] is string[] reasons &&
                      reasons.Contains("NameTruncatedToWindowsComponentLimit"));
    }

    [TestMethod]
    public void OriginalFileName_TruncationHappensAfterKnownExtensionReplacement()
    {
        var generator = new CoverImageFileNameGenerator();
        var requested = new string('ü', 300) + ".png";

        var result = generator.Generate(
            "jpg",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.OriginalFileName,
                OriginalFileName = requested
            });

        Assert.AreEqual(CoverImageFileNameGenerator.MaxFileNameCharacters, result.FileName.Length);
        Assert.EndsWith(".jpg", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
    }

    [TestMethod]
    public void MissingRequestedName_FallsBackToTimestampAndReportsAdjustment()
    {
        var timeProvider = new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, 5, TimeSpan.Zero),
            TimeZoneInfo.Utc);
        var generator = new CoverImageFileNameGenerator(timeProvider);

        var result = generator.Generate(
            "png",
            new CoverImageNamingOptions
            {
                Strategy = CoverImageNamingStrategy.OriginalFileName
            });

        Assert.AreEqual("2026.08.20 12.00.00.005.png", result.FileName);
        Assert.IsTrue(result.WasAdjusted);
        Assert.AreEqual(CoverImageIssueSeverity.Info, result.Issues.Single().Severity);
    }

    [TestMethod]
    public void ResultExtension_IsNormalizedAndValidated()
    {
        var generator = new CoverImageFileNameGenerator();

        var result = generator.Generate(".PNG");

        Assert.AreEqual(".png", result.Extension);
        Assert.ThrowsExactly<ArgumentException>(() => generator.Generate("tar.gz"));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
    }
}
