using CoverImageDrop.Testing;

namespace CoverImageDrop.Acquisition.Tests;

[TestClass]
public sealed class BrowserDropFixtureTests
{
    [TestMethod]
    public void FixtureCorpus_AnalyzesToExpectedSourceAndPreferredCandidate()
    {
        foreach (var fileName in BrowserDropFixtureSupport.GetFixtureFileNames())
        {
            StaTestRunner.Run(() =>
            {
                var fixture = BrowserDropFixtureSupport.Load(fileName);
                var data = BrowserDropFixtureSupport.CreateDataObject(fixture);

                var analysis = new DropImageDataReader().Analyze(data);
                var preferred = BrowserDropFixtureSupport.GetPreferredCandidate(analysis);

                Assert.AreEqual(
                    BrowserDropFixtureSupport.ParseSourceKind(fixture.Expected.SourceKind),
                    analysis.SourceDetection.Kind,
                    $"Source kind mismatch for fixture {fixture.Id}.");
                Assert.AreEqual(
                    BrowserDropFixtureSupport.ParseSourceConfidence(fixture.Expected.SourceConfidence),
                    analysis.SourceDetection.Confidence,
                    $"Source confidence mismatch for fixture {fixture.Id}.");
                Assert.AreEqual(
                    BrowserDropFixtureSupport.ParseCandidateKind(fixture.Expected.PreferredCandidateKind),
                    preferred.Kind,
                    $"Preferred candidate kind mismatch for fixture {fixture.Id}.");
                Assert.AreEqual(
                    fixture.Expected.PreferredCandidateUrl,
                    preferred.Url,
                    $"Preferred candidate URL mismatch for fixture {fixture.Id}.");

                if (fixture.Expected.ProcessingArtifactMinimumLength is int minimumLength)
                {
                    Assert.IsTrue(
                        analysis.ProcessingTextArtifacts.Values.Any(value => value.Length >= minimumLength),
                        $"Processing artifact was unexpectedly truncated for fixture {fixture.Id}.");
                }

                if (fixture.Expected.DiagnosticArtifactMaximumLength is int maximumLength)
                {
                    Assert.IsTrue(
                        analysis.DiagnosticTextArtifacts.Values.All(value => value.Length <= maximumLength),
                        $"Diagnostic artifact exceeded its bounded snippet length for fixture {fixture.Id}.");
                }
            });
        }
    }

    [TestMethod]
    public void FixtureCorpus_PreferredCandidateMaterializationMatchesExpectation()
    {
        foreach (var fileName in BrowserDropFixtureSupport.GetFixtureFileNames())
        {
            StaTestRunner.Run(async () =>
            {
                var fixture = BrowserDropFixtureSupport.Load(fileName);
                var data = BrowserDropFixtureSupport.CreateDataObject(fixture);
                using var handler = new BrowserDropFixtureHttpHandler(fixture);
                var reader = new DropImageDataReader(
                    handler,
                    TimeSpan.FromSeconds(2));

                var analysis = reader.Analyze(data);
                var preferred = BrowserDropFixtureSupport.GetPreferredCandidate(analysis);
                var materialized = await reader.MaterializeCandidateAsync(
                    analysis,
                    preferred);

                switch (fixture.Expected.PreferredMaterializationResult)
                {
                    case "Success":
                        Assert.IsTrue(
                            materialized.IsSuccess,
                            $"Preferred candidate did not materialize for fixture {fixture.Id}: {materialized.FailureReason}");
                        Assert.IsNotNull(materialized.ImageData);
                        materialized.ImageData!.Dispose();
                        break;

                    case "Failure":
                        Assert.IsFalse(
                            materialized.IsSuccess,
                            $"Preferred candidate unexpectedly materialized for fixture {fixture.Id}.");
                        Assert.IsNull(materialized.ImageData);
                        break;

                    default:
                        Assert.Fail(
                            $"Unsupported preferred-materialization expectation for fixture {fixture.Id}: {fixture.Expected.PreferredMaterializationResult}");
                        break;
                }
            });
        }
    }
}
