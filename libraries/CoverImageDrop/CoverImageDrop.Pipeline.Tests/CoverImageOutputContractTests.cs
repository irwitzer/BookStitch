using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImageOutputContractTests
{
    [TestMethod]
    public void PipelineProfiles_DefineBothV1Profiles()
    {
        CollectionAssert.AreEquivalent(
            new[] { "PassThrough", "StandardCover" },
            Enum.GetNames<CoverImagePipelineProfile>());
    }

    [TestMethod]
    public void StandardCoverOutputOptions_DefaultToAutoJpegAutoTransparency()
    {
        var options = new StandardCoverOutputOptions();

        Assert.AreEqual(CoverTargetSizeMode.Auto, options.TargetSize);
        Assert.AreEqual(CoverOutputFormat.Jpeg, options.Format);
        Assert.AreEqual(JpegTransparencyMode.Auto, options.Transparency);
    }

    [TestMethod]
    public void StandardCoverOutputContract_DefinesRequiredV1Choices()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Auto", "Size1000", "Size2000", "Size3000" },
            Enum.GetNames<CoverTargetSizeMode>());
        CollectionAssert.AreEquivalent(
            new[] { "Jpeg", "Png" },
            Enum.GetNames<CoverOutputFormat>());
        CollectionAssert.AreEquivalent(
            new[] { "Auto", "White", "Black" },
            Enum.GetNames<JpegTransparencyMode>());
    }
}
