using CoverImageDrop.Acquisition;
using CoverImageDrop.Acquisition.Tests;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Testing;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class BrowserDropFixturePipelineTests
{
    [TestMethod]
    public void FixtureCorpus_ProducesDeclaredProfileResults()
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
                var pipeline = new CoverImagePipelineService(
                    options: null,
                    acquisitionReader: reader);

                var analysis = reader.Analyze(data);
                var preferred = BrowserDropFixtureSupport.GetPreferredCandidate(analysis);

                var standardCover = await pipeline.LoadStandardCoverAsync(analysis);
                try
                {
                    switch (fixture.Expected.StandardCoverResult)
                    {
                        case "Success":
                            Assert.IsTrue(
                                standardCover.IsSuccess,
                                $"StandardCover unexpectedly failed for fixture {fixture.Id}: {FormatIssues(standardCover.Issues)}");
                            Assert.IsNotNull(standardCover.Session);
                            break;

                        case "ImageTooSmall":
                            Assert.IsFalse(
                                standardCover.IsSuccess,
                                $"StandardCover unexpectedly succeeded for fixture {fixture.Id}.");
                            Assert.IsTrue(
                                standardCover.Issues.Any(issue =>
                                    issue.Code == CoverImageIssueCode.ImageTooSmall),
                                $"Expected ImageTooSmall for fixture {fixture.Id}; actual: {FormatIssues(standardCover.Issues)}");
                            break;

                        case "NoImagePayloadFound":
                            Assert.IsFalse(
                                standardCover.IsSuccess,
                                $"StandardCover unexpectedly succeeded for fixture {fixture.Id}.");
                            Assert.IsTrue(
                                standardCover.Issues.Any(issue =>
                                    issue.Code == CoverImageIssueCode.NoImagePayloadFound),
                                $"Expected NoImagePayloadFound for fixture {fixture.Id}; actual: {FormatIssues(standardCover.Issues)}");
                            break;

                        default:
                            Assert.Fail(
                                $"Unsupported StandardCover expectation for fixture {fixture.Id}: {fixture.Expected.StandardCoverResult}");
                            break;
                    }

                    if (preferred.Url is not null)
                    {
                        Assert.IsTrue(
                            handler.RequestedUris.Count > 0,
                            $"No HTTP request was observed for fixture {fixture.Id}.");
                        Assert.AreEqual(
                            preferred.Url,
                            handler.RequestedUris[0].ToString(),
                            $"The preferred candidate was not requested first for fixture {fixture.Id}.");
                    }
                }
                finally
                {
                    if (standardCover.Session is not null)
                    {
                        await standardCover.Session.DisposeAsync();
                    }
                }

                foreach (var requiredUrl in fixture.Expected.RequiredStandardCoverRequestUrls)
                {
                    Assert.IsTrue(
                        handler.RequestedUris.Any(uri =>
                            string.Equals(
                                uri.AbsoluteUri,
                                requiredUrl,
                                StringComparison.OrdinalIgnoreCase)),
                        $"StandardCover did not request required captured URL for fixture {fixture.Id}: {requiredUrl}");
                }

                if (fixture.Expected.PassThroughResult == "AcquisitionFailure")
                {
                    await Assert.ThrowsExactlyAsync<DropDownloadException>(
                        async () => await reader.ReadAsync(data));
                }
                else
                {
                    var passThroughRead = await reader.ReadAsync(data);
                    try
                    {
                        var passThrough = await pipeline.ProcessPassThroughAsync(
                            passThroughRead);

                        Assert.AreEqual(
                            "Success",
                            fixture.Expected.PassThroughResult,
                            $"Unsupported PassThrough expectation for fixture {fixture.Id}: {fixture.Expected.PassThroughResult}");
                        Assert.IsTrue(
                            passThrough.IsSuccess,
                            $"PassThrough unexpectedly failed for fixture {fixture.Id}: {FormatIssues(passThrough.Issues)}");
                        Assert.IsNotNull(passThrough.Source);
                    }
                    finally
                    {
                        passThroughRead.ImageData?.Dispose();
                    }
                }
            });
        }
    }

    private static string FormatIssues(
        IReadOnlyList<CoverImageIssue> issues)
    {
        return string.Join(
            ", ",
            issues.Select(issue => $"{issue.Severity}:{issue.Code}"));
    }
}
