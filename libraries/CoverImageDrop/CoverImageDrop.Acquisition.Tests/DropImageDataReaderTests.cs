using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Acquisition.Tests;

[TestClass]
public sealed class DropImageDataReaderTests
{
    [TestMethod]
    public void CanRead_ReturnsTrueForHtmlDropData()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.Html, "<img src=\"https://cdn.example.test/cover.jpg\">");

            Assert.IsTrue(new DropImageDataReader().CanRead(data));
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_ExtractsGoogleImageUrlCandidate()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                DataFormats.Html,
                "<a href=\"https://www.google.com/imgres?imgurl=https%3A%2F%2Fcdn.example.test%2Fcover.jpg&source=lnms\">cover</a>");

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.AreEqual("Diagnostic", result.AcquisitionMethod);
            Assert.IsTrue(result.UrlCandidates.Any(candidate => candidate.Url == "https://cdn.example.test/cover.jpg"));
        });
    }

    [TestMethod]
    public void Analyze_ExtractsGoogleImageReferencePageCandidate()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                DataFormats.UnicodeText,
                "https://www.google.com/imgres?imgurl=https%3A%2F%2Fcdn.example.test%2Fcover-417x626.jpg&imgrefurl=https%3A%2F%2Fwww.example.test%2Farticle");

            var analysis = new DropImageDataReader().Analyze(data);

            var page = analysis.UrlCandidates.Single(candidate =>
                candidate.Source.EndsWith(
                    ":imgrefurl",
                    StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(
                "https://www.example.test/article",
                page.Url.TrimEnd('/'));
            Assert.AreEqual(DropImageCandidateKind.DirectUrl, page.Kind);
            Assert.AreEqual(80, page.Priority);
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_ExtractsMetaImageCandidate()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                "HTML Format",
                "<meta property=\"og:image\" content=\"https://images.example.test/artwork.webp\">");

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.IsTrue(result.UrlCandidates.Any(candidate => candidate.Url == "https://images.example.test/artwork.webp"));
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_IgnoresChromiumRendererTaintAsPlainUrlSource()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData("chromium/x-renderer-taint", "https://open.spotifycdn.com/cdn/images/devices/mac.3fbeb8c6.png");

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.AreEqual("Chromium-basiert", result.InferredSource);
            Assert.AreEqual(DropSourceKind.Chromium, result.SourceDetection.Kind);
            Assert.IsEmpty(result.UrlCandidates);
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_ChromiumMarkerWinsOverMozillaCompatibilityFormats()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData("Chromium Web Custom MIME Data Format", "image payload metadata");
            data.SetData("chromium/x-renderer-taint", "renderer metadata");
            data.SetData("text/x-moz-url", "https://example.test/cover.jpg");

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.AreEqual("Chromium-basiert", result.InferredSource);
            Assert.AreEqual(DropSourceKind.Chromium, result.SourceDetection.Kind);
            Assert.AreEqual(DropSourceConfidence.High, result.SourceDetection.Confidence);
            Assert.IsTrue(result.SourceDetection.Reasons.Any(reason =>
                reason.Contains("Vorrang", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_InfersFirefoxSourceFromMozFormats()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData("application/x-moz-file-promise-url", "https://example.test/cover.jpg");

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.AreEqual("Firefox", result.InferredSource);
            Assert.AreEqual(DropSourceKind.Firefox, result.SourceDetection.Kind);
            Assert.AreEqual(DropSourceConfidence.High, result.SourceDetection.Confidence);
            Assert.IsTrue(result.SourceDetection.Reasons.Count > 0);
        });
    }

    [TestMethod]
    public void Analyze_LongDataUrl_IsNotDecodedUntilCandidateMaterialization()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = Enumerable.Range(0, 18_000)
                .Select(index => (byte)(index % 251))
                .ToArray();
            var dataUrl =
                "data:image/png;base64," +
                Convert.ToBase64String(expected);
            Assert.IsTrue(dataUrl.Length > 20_000);

            var data = new DataObject();
            data.SetData(
                DataFormats.Html,
                "<img src=\"" + dataUrl + "\">");

            var reader = new DropImageDataReader();
            var analysis = reader.Analyze(data);

            Assert.IsTrue(analysis.ProcessingTextArtifacts.Values.Any(
                value => value.Contains(
                    dataUrl,
                    StringComparison.Ordinal)));
            Assert.IsTrue(analysis.DiagnosticTextArtifacts.Values.All(
                value => value.Length <= 20_000));

            var candidate = analysis.Candidates.First(item =>
                item.Kind == DropImageCandidateKind.EncodedDataUrl);
            Assert.IsNull(candidate.SourceData);

            var materialized = await reader.MaterializeCandidateAsync(
                analysis,
                candidate);

            Assert.IsTrue(materialized.IsSuccess, materialized.FailureReason);
            using var imageData = materialized.ImageData!;
            Assert.IsTrue(imageData.TryGetInMemoryBytes(out var actual));
            CollectionAssert.AreEqual(expected, actual);
        });
    }

    [TestMethod]
    public void CreateDiagnosticResult_LongText_KeepsLegacyTextArtifactsBounded()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.Html, new string('x', 25_000));

            var result = new DropImageDataReader().CreateDiagnosticResult(data, "Diagnostic");

            Assert.AreEqual(20_000, result.TextArtifacts[DataFormats.Html].Length);
            Assert.AreEqual(20_000, result.DiagnosticTextArtifacts[DataFormats.Html].Length);
        });
    }

    [TestMethod]
    public void Analyze_RelativeSrc_ResolvesAgainstSourceUrl()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                "HTML Format",
                "Version:1.0\r\nSourceURL:https://example.test/books/page.html\r\n<html><img src=\"../images/cover.jpg\"></html>");

            var analysis = new DropImageDataReader().Analyze(data);

            Assert.IsTrue(analysis.UrlCandidates.Any(candidate =>
                candidate.Url == "https://example.test/images/cover.jpg" &&
                candidate.Kind == DropImageCandidateKind.HtmlDerivedUrl));
        });
    }

    [TestMethod]
    public void Analyze_SrcSet_RanksLargestWidthFirst()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                "HTML Format",
                "SourceURL:https://example.test/book/page.html\r\n<img srcset=\"/small.jpg 320w, /large.jpg 1600w, /medium.jpg 800w\">");

            var analysis = new DropImageDataReader().Analyze(data);
            var srcSetCandidates = analysis.UrlCandidates
                .Where(candidate => candidate.Source.EndsWith(":srcset", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual(3, srcSetCandidates.Length);
            Assert.AreEqual(1600, srcSetCandidates[0].Width);
            Assert.AreEqual("https://example.test/large.jpg", srcSetCandidates[0].Url);
        });
    }

    [TestMethod]
    public void Analyze_SameUrlFromSrcAndSrcset_MergesBestPriorityAndKnownWidth()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                "HTML Format",
                "SourceURL:https://example.test/book/page.html\r\n<img src=\"/small.jpg\" srcset=\"/small.jpg 320w, /large.jpg 1600w\">");

            var analysis = new DropImageDataReader().Analyze(data);
            var small = analysis.UrlCandidates.Single(candidate =>
                candidate.Url == "https://example.test/small.jpg");
            var large = analysis.UrlCandidates.Single(candidate =>
                candidate.Url == "https://example.test/large.jpg");

            Assert.AreEqual(20, small.Priority);
            Assert.AreEqual(320, small.Width);
            Assert.AreEqual(25, large.Priority);
            Assert.AreEqual(1600, large.Width);
            Assert.AreEqual(small.Url, analysis.UrlCandidates[0].Url);
        });
    }

    [TestMethod]
    public void Analyze_BitmapFallback_LosesAgainstPlausibleWebResource()
    {
        StaTestRunner.Run(() =>
        {
            var pixels = new byte[] { 0, 0, 255, 255 };
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, bitmap);
            data.SetData(DataFormats.Html, "<img src=\"https://cdn.example.test/cover.jpg\">");

            var analysis = new DropImageDataReader().Analyze(data);

            var webIndex = analysis.Candidates.ToList().FindIndex(candidate => candidate.Url == "https://cdn.example.test/cover.jpg");
            var bitmapIndex = analysis.Candidates.ToList().FindIndex(candidate => candidate.Kind == DropImageCandidateKind.BitmapFallback);
            Assert.IsTrue(webIndex >= 0);
            Assert.IsTrue(bitmapIndex > webIndex);
        });
    }

    [TestMethod]
    public void Analyze_BitmapFallback_IsEncodedOnlyWhenMaterialized()
    {
        StaTestRunner.Run(async () =>
        {
            var pixels = new byte[]
            {
                0, 0, 255, 255,
                0, 255, 0, 255
            };
            var bitmap = BitmapSource.Create(
                2,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                8);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, bitmap);

            var reader = new DropImageDataReader();
            var analysis = reader.Analyze(data);
            var candidate = analysis.Candidates.Single(item =>
                item.Kind == DropImageCandidateKind.BitmapFallback);

            Assert.IsNull(candidate.SourceData);

            var materialized = await reader.MaterializeCandidateAsync(
                analysis,
                candidate);

            Assert.IsTrue(materialized.IsSuccess, materialized.FailureReason);
            using var imageData = materialized.ImageData!;
            Assert.IsTrue(imageData.TryGetInMemoryBytes(out var bytes));
            Assert.IsTrue(bytes.Length > 8);
            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                },
                bytes.Take(8).ToArray());
        });
    }

    [TestMethod]
    public void Analyze_IgnoresUnrecognizedBinaryFormatsInsteadOfMaterializingThemAsText()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                "application/x-coverimagedrop-binary",
                new MemoryStream(
                    Enumerable.Repeat((byte)0x41, 4096).ToArray(),
                    writable: false));
            data.SetData(
                DataFormats.Html,
                "<img src=\"https://cdn.example.test/cover.jpg\">");

            var analysis = new DropImageDataReader().Analyze(data);

            Assert.IsFalse(analysis.ProcessingTextArtifacts.ContainsKey(
                "application/x-coverimagedrop-binary"));
            Assert.IsTrue(analysis.ProcessingTextArtifacts.ContainsKey(
                DataFormats.Html));
            Assert.IsTrue(analysis.UrlCandidates.Any(candidate =>
                candidate.Url == "https://cdn.example.test/cover.jpg"));
        });
    }

    [TestMethod]
    public void AnalyzeAsync_PreservesBrowserCandidateDiscovery()
    {
        StaTestRunner.Run(async () =>
        {
            var data = new DataObject();
            data.SetData(
                "Chromium Web Custom MIME Data Format",
                "metadata");
            data.SetData(
                DataFormats.Html,
                "<img src=\"https://cdn.example.test/cover.jpg\">");

            var analysis = await new DropImageDataReader().AnalyzeAsync(data);

            Assert.AreEqual(
                DropSourceKind.Chromium,
                analysis.SourceDetection.Kind);
            Assert.IsTrue(analysis.UrlCandidates.Any(candidate =>
                candidate.Url == "https://cdn.example.test/cover.jpg"));
        });
    }

    [TestMethod]
    public void Analyze_CustomTextSafetyLimitSkipsOversizedTextArtifact()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(
                DataFormats.Html,
                "<img src=\"https://cdn.example.test/cover.jpg\">");
            var reader = new DropImageDataReader(
                new DropImageAcquisitionSafetyLimits
                {
                    MaxTextArtifactBytes = 32
                });

            var analysis = reader.Analyze(data);

            Assert.IsFalse(analysis.ProcessingTextArtifacts.ContainsKey(
                DataFormats.Html));
            Assert.IsEmpty(analysis.UrlCandidates);
        });
    }

    [TestMethod]
    public void ReadAsync_BitmapOnlyDropMaterializesLosslessPngOnDemand()
    {
        StaTestRunner.Run(async () =>
        {
            var pixels = new byte[]
            {
                0, 0, 255, 255
            };
            var bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                4);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, bitmap);

            var result = await new DropImageDataReader().ReadAsync(data);

            Assert.AreEqual("Bitmap", result.AcquisitionMethod);
            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.IsTrue(imageData.TryGetInMemoryBytes(out var bytes));
            CollectionAssert.AreEqual(
                new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                },
                bytes.Take(8).ToArray());
        });
    }

    [TestMethod]
    public void ReadAsync_FailedUrlThenBitmapFallbackPreservesDownloadAttempts()
    {
        StaTestRunner.Run(async () =>
        {
            var pixels = new byte[]
            {
                0, 0, 255, 255
            };
            var bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                4);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, bitmap);
            data.SetData(
                DataFormats.Html,
                "<img src=\"https://cdn.example.test/not-an-image.jpg\">");
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(CreateContentResponse(
                    "%PDF-1.7"u8.ToArray(),
                    "application/pdf")));

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.AreEqual("Bitmap", result.AcquisitionMethod);
            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.IsTrue(imageData.TryGetInMemoryBytes(out _));
            Assert.IsTrue(result.DownloadAttempts.Count > 0);
            Assert.IsFalse(result.DownloadAttempts[0].Accepted);
        });
    }

    [TestMethod]
    public void Analyze_FileDrop_ProducesTypedExplorerDetectionAndTopCandidate()
    {
        StaTestRunner.Run(() =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { @"C:\Temp\cover.png" });

            var analysis = new DropImageDataReader().Analyze(data);

            Assert.AreEqual(DropSourceKind.WindowsExplorer, analysis.SourceDetection.Kind);
            Assert.AreEqual(DropSourceConfidence.High, analysis.SourceDetection.Confidence);
            Assert.AreEqual(DropImageCandidateKind.LocalFile, analysis.Candidates[0].Kind);
        });
    }

    [TestMethod]
    public void ReadAsync_ReadsEmbeddedDataUrlBeforeUrlDownloads()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = new byte[] { 0, 1, 2, 3 };
            var data = new DataObject();
            data.SetData(DataFormats.Html, "prefix data:image/png;base64,AAECAw== suffix <a href=\"https://www.google.com/imgres?imgurl=https%3A%2F%2Fcdn.example.test%2Fcover.jpg\">cover</a>");

            var result = await new DropImageDataReader().ReadAsync(data);

            Assert.AreEqual("HtmlDataUrl", result.AcquisitionMethod);
            Assert.IsNotNull(result.ImageData);
            Assert.IsTrue(result.ImageData.TryGetInMemoryBytes(out var bytes));
            CollectionAssert.AreEqual(expected, bytes);
            Assert.AreEqual("Browser-data-URL", result.ImageData.SourceName);
        });
    }

    [TestMethod]
    public void ReadAsync_ReadsFirstFileDropPath()
    {
        StaTestRunner.Run(async () =>
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"CoverImageDrop_{Guid.NewGuid():N}.bin");
            var expected = new byte[] { 5, 6, 7 };

            try
            {
                await File.WriteAllBytesAsync(filePath, expected);
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { filePath });

                var result = await new DropImageDataReader().ReadAsync(data);

                Assert.AreEqual("FileDrop", result.AcquisitionMethod);
                Assert.IsNotNull(result.ImageData);
                Assert.AreEqual(Path.GetFileName(filePath), result.ImageData.SourceName);
                Assert.AreEqual(filePath, result.ImageData.LocalFilePath);
                Assert.AreEqual(DropImagePayloadKind.LocalFile, result.ImageData.PayloadKind);
                Assert.AreEqual(expected.LongLength, result.ImageData.Length);
                Assert.IsFalse(result.ImageData.TryGetInMemoryBytes(out _));
                Assert.AreEqual(DropSourceKind.WindowsExplorer, result.SourceDetection.Kind);

                await using var source = await result.ImageData.OpenReadAsync();
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy);
                CollectionAssert.AreEqual(expected, copy.ToArray());
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        });
    }

    [TestMethod]
    public void ReadAsync_NonImageCandidate_TriesNextCandidateAndUsesSignatureOverMimeType()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = CreatePngPayload();
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                var response = request.RequestUri?.AbsolutePath switch
                {
                    "/a.jpg" => CreateContentResponse("%PDF-1.7"u8.ToArray(), "image/jpeg"),
                    "/b.png" => CreateContentResponse(expected, "application/octet-stream"),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
                return Task.FromResult(response);
            });
            var data = CreateHtmlUrlDrop(
                "<img src=\"https://cdn.example.test/a.jpg\"><img src=\"https://cdn.example.test/b.png\">");

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.AreEqual("UrlDownload", result.AcquisitionMethod);
            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, imageData.PayloadKind);
            Assert.IsFalse(imageData.TryGetInMemoryBytes(out _));
            CollectionAssert.AreEqual(expected, await ReadImageBytesAsync(imageData));
            Assert.AreEqual(2, result.DownloadAttempts.Count);
            Assert.IsFalse(result.DownloadAttempts[0].Accepted);
            StringAssert.Contains(result.DownloadAttempts[0].Error ?? string.Empty, "Bildpayload");
            Assert.IsTrue(result.DownloadAttempts[1].Accepted);
        });
    }

    [TestMethod]
    public void ReadAsync_FollowsFiveControlledRedirects()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = CreatePngPayload();
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                var response = request.RequestUri?.AbsolutePath switch
                {
                    "/start.jpg" => CreateRedirectResponse("/r1"),
                    "/r1" => CreateRedirectResponse("/r2"),
                    "/r2" => CreateRedirectResponse("/r3"),
                    "/r3" => CreateRedirectResponse("/r4"),
                    "/r4" => CreateRedirectResponse("/image.png"),
                    "/image.png" => CreateContentResponse(expected, "image/png"),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
                return Task.FromResult(response);
            });
            var data = CreateHtmlUrlDrop("<img src=\"https://redirect.example.test/start.jpg\">");

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, imageData.PayloadKind);
            Assert.IsNotNull(result.DownloadInfo);
            Assert.AreEqual("https://redirect.example.test/start.jpg", result.DownloadInfo.RequestedUrl);
            Assert.AreEqual("https://redirect.example.test/image.png", result.DownloadInfo.FinalUrl);
            Assert.IsTrue(result.DownloadAttempts.Single().Accepted);
        });
    }

    [TestMethod]
    public void ReadAsync_RejectsSixthRedirect()
    {
        StaTestRunner.Run(async () =>
        {
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                var path = request.RequestUri?.AbsolutePath;
                var response = path switch
                {
                    "/start.jpg" => CreateRedirectResponse("/r1"),
                    "/r1" => CreateRedirectResponse("/r2"),
                    "/r2" => CreateRedirectResponse("/r3"),
                    "/r3" => CreateRedirectResponse("/r4"),
                    "/r4" => CreateRedirectResponse("/r5"),
                    "/r5" => CreateRedirectResponse("/r6"),
                    _ => new HttpResponseMessage(HttpStatusCode.NotFound)
                };
                return Task.FromResult(response);
            });
            var data = CreateHtmlUrlDrop("<img src=\"https://redirect.example.test/start.jpg\">");

            try
            {
                await new DropImageDataReader(handler).ReadAsync(data);
                Assert.Fail("Ein sechster Redirect haette abgelehnt werden muessen.");
            }
            catch (DropDownloadException ex)
            {
                Assert.IsTrue(ex.Attempts.Any(attempt =>
                    (attempt.Error ?? string.Empty).Contains("Redirectlimit", StringComparison.Ordinal)));
            }
        });
    }

    [TestMethod]
    public void ReadAsync_RejectsRedirectToNonHttpScheme()
    {
        StaTestRunner.Run(async () =>
        {
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(CreateRedirectResponse("file:///C:/Temp/cover.png")));
            var data = CreateHtmlUrlDrop("<img src=\"https://redirect.example.test/start.jpg\">");

            try
            {
                await new DropImageDataReader(handler).ReadAsync(data);
                Assert.Fail("Ein Redirect auf file: haette abgelehnt werden muessen.");
            }
            catch (DropDownloadException ex)
            {
                Assert.IsTrue(ex.Attempts.Any(attempt =>
                    (attempt.Error ?? string.Empty).Contains("URL-Schema", StringComparison.Ordinal)));
            }
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_PageFallbackPrefersLargerSameShapeImageFamilyVariant()
    {
        StaTestRunner.Run(async () =>
        {
            const string smallUrl =
                "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
            const string landscapeUrl =
                "https://cdn.example.test/GettyImages-166673845-1440x720-1440x720-c-default.jpeg";
            const string portraitUrl =
                "https://cdn.example.test/GettyImages-166673845-768x1152-768x1152-c-default.jpeg";
            const string pageUrl = "https://www.example.test/article";
            var requestedPaths = new List<string>();
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                requestedPaths.Add(request.RequestUri!.AbsolutePath);
                if (request.RequestUri.AbsoluteUri == pageUrl)
                {
                    var html = $"<meta property=\"og:image\" content=\"{landscapeUrl}\">" +
                               $"<img src=\"{smallUrl}\" srcset=\"{portraitUrl} 768w\">";
                    return Task.FromResult(CreateContentResponse(
                        Encoding.UTF8.GetBytes(html),
                        "text/html"));
                }

                if (request.RequestUri.AbsoluteUri == portraitUrl)
                {
                    return Task.FromResult(CreateContentResponse(
                        CreatePngPayload(),
                        "image/png"));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            var analysis = CreateVariantAnalysis(smallUrl, pageUrl);
            var pageCandidate = analysis.Candidates.Single(candidate =>
                candidate.Url == pageUrl);
            var reference = new DropImageVariantReference(smallUrl, 417, 626);

            var result = await new DropImageDataReader(handler)
                .MaterializeCandidateAsync(
                    analysis,
                    pageCandidate,
                    reference);

            Assert.IsTrue(result.IsSuccess, result.FailureReason);
            using var imageData = result.ImageData!;
            Assert.AreEqual(portraitUrl, result.ReadResult.DownloadInfo?.RequestedUrl);
            Assert.IsFalse(requestedPaths.Any(path => path.Contains(
                "1440x720",
                StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_PageFallbackRejectsDifferentCropWhenNoRelatedGeometryExists()
    {
        StaTestRunner.Run(async () =>
        {
            const string smallUrl =
                "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
            const string landscapeUrl =
                "https://cdn.example.test/GettyImages-166673845-1440x720-1440x720-c-default.jpeg";
            const string pageUrl = "https://www.example.test/article";
            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.AbsoluteUri == pageUrl)
                {
                    var html = $"<meta property=\"og:image\" content=\"{landscapeUrl}\">";
                    return Task.FromResult(CreateContentResponse(
                        Encoding.UTF8.GetBytes(html),
                        "text/html"));
                }

                Assert.Fail("Die unpassende Querformatvariante darf nicht heruntergeladen werden.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            var analysis = CreateVariantAnalysis(smallUrl, pageUrl);
            var pageCandidate = analysis.Candidates.Single(candidate =>
                candidate.Url == pageUrl);
            var reference = new DropImageVariantReference(smallUrl, 417, 626);

            var result = await new DropImageDataReader(handler)
                .MaterializeCandidateAsync(
                    analysis,
                    pageCandidate,
                    reference);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue((result.FailureReason ?? string.Empty).Contains(
                "keine ausreichend verwandte Bildvariante",
                StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void VariantReference_RequiresSameOrientationAndApproximatelySameAspectRatio()
    {
        var reference = new DropImageVariantReference(
            "https://cdn.example.test/elephant-417x626.jpg",
            417,
            626);

        Assert.IsTrue(reference.IsGeometryCompatible(800, 1200));
        Assert.IsTrue(reference.IsGeometryCompatible(700, 1000));
        Assert.IsFalse(reference.IsGeometryCompatible(1000, 1000));
        Assert.IsFalse(reference.IsGeometryCompatible(1440, 720));
        Assert.IsFalse(reference.IsGeometryCompatible(900, 1000));
    }

    [TestMethod]
    public void ReadAsync_CallerCancellationIsNotConvertedToCandidateFailure()
    {
        StaTestRunner.Run(async () =>
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var data = CreateHtmlUrlDrop("<img src=\"https://slow.example.test/cover.jpg\">");
            using var cancellation = new CancellationTokenSource();
            var readTask = new DropImageDataReader(handler).ReadAsync(data, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();

            try
            {
                await readTask;
                Assert.Fail("Caller-Cancellation haette weitergereicht werden muessen.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Expected: caller cancellation remains cancellation, not DropDownloadException.
            }
        });
    }

    [TestMethod]
    public void ReadAsync_RequestTimeoutBecomesCandidateFailureWithoutCallerCancellation()
    {
        StaTestRunner.Run(async () =>
        {
            var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var data = CreateHtmlUrlDrop("<img src=\"https://slow.example.test/cover.jpg\">");

            try
            {
                await new DropImageDataReader(handler, TimeSpan.FromMilliseconds(50)).ReadAsync(data);
                Assert.Fail("Der konfigurierte Request-Timeout haette ausloesen muessen.");
            }
            catch (DropDownloadException ex)
            {
                Assert.IsTrue(ex.InnerException is TimeoutException);
            }
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_MaxDownloadBytesRejectsOversizedDeclaredLength()
    {
        StaTestRunner.Run(async () =>
        {
            const long maxDownloadBytes = 1024;
            var handler = new StubHttpMessageHandler((_, _) =>
            {
                var response = CreateContentResponse(CreatePngPayload(), "image/png");
                response.Content.Headers.ContentLength = maxDownloadBytes + 1;
                return Task.FromResult(response);
            });
            var reader = new DropImageDataReader(handler);
            var data = CreateHtmlUrlDrop(
                "<img src=\"https://large.example.test/cover.png\">");
            var analysis = reader.Analyze(data);
            var candidate = analysis.Candidates.Single(value => value.Url is not null);

            var result = await reader.MaterializeCandidateAsync(
                analysis,
                candidate,
                variantReference: null,
                cancellationToken: default,
                maxDownloadBytes: maxDownloadBytes);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.ImageData);
            Assert.IsTrue(result.ReadResult.DownloadAttempts.Any(attempt =>
                (attempt.Error ?? string.Empty).Contains(
                    "konfigurierte Limit",
                    StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_HtmlFallbackRejectsOversizedProcessingText()
    {
        StaTestRunner.Run(async () =>
        {
            var oversizedHtml = "<html><body>" +
                new string('x', DropImageDataReader.MaxDownloadedHtmlCharacters + 1) +
                "</body></html>";
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(CreateContentResponse(
                    System.Text.Encoding.UTF8.GetBytes(oversizedHtml),
                    "text/html")));
            var reader = new DropImageDataReader(handler);
            var data = CreateHtmlUrlDrop(
                "https://page.example.test/book");
            var analysis = reader.Analyze(data);
            var candidate = analysis.Candidates.Single(value => value.Url is not null);

            var result = await reader.MaterializeCandidateAsync(
                analysis,
                candidate);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.ImageData);
            Assert.IsTrue(result.ReadResult.DownloadAttempts.Any(attempt =>
                (attempt.Error ?? string.Empty).Contains(
                    "HTML-Fallback",
                    StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public void ReadAsync_LegacyFiftyMegabyteContentLengthDoesNotRejectPassThroughAcquisition()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = CreatePngPayload();
            var handler = new StubHttpMessageHandler((_, _) =>
            {
                var response = CreateContentResponse(expected, "image/png");
                response.Content.Headers.ContentLength = 60L * 1024 * 1024;
                return Task.FromResult(response);
            });
            var data = CreateHtmlUrlDrop("<img src=\"https://large.example.test/cover.png\">");

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, imageData.PayloadKind);
            Assert.IsFalse(imageData.TryGetInMemoryBytes(out _));
            CollectionAssert.AreEqual(expected, await ReadImageBytesAsync(imageData));
            Assert.IsNotNull(result.DownloadInfo);
            Assert.AreEqual(60L * 1024 * 1024, result.DownloadInfo.ContentLength);
        });
    }

    [TestMethod]
    public void ReadAsync_CustomRemoteSafetyLimitRejectsOversizedDeclaredLength()
    {
        StaTestRunner.Run(async () =>
        {
            const long maxRemoteDownloadBytes = 1024;
            var handler = new StubHttpMessageHandler((_, _) =>
            {
                var response = CreateContentResponse(CreatePngPayload(), "image/png");
                response.Content.Headers.ContentLength = maxRemoteDownloadBytes + 1;
                return Task.FromResult(response);
            });
            var data = CreateHtmlUrlDrop(
                "<img src=\"https://large.example.test/cover.png\">");
            var reader = new DropImageDataReader(
                handler,
                safetyLimits: new DropImageAcquisitionSafetyLimits
                {
                    MaxRemoteDownloadBytes = maxRemoteDownloadBytes
                });

            try
            {
                await reader.ReadAsync(data);
                Assert.Fail("Das profilunabhaengige Remote-Safety-Limit haette ausloesen muessen.");
            }
            catch (DropDownloadException ex)
            {
                Assert.IsTrue(ex.Attempts.Any(attempt =>
                    (attempt.Error ?? string.Empty).Contains(
                        "konfigurierte Limit",
                        StringComparison.OrdinalIgnoreCase)));
            }
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_CustomDataUrlSafetyLimitRejectsOversizedPayload()
    {
        StaTestRunner.Run(async () =>
        {
            var payload = Enumerable.Range(0, 64)
                .Select(index => (byte)index)
                .ToArray();
            var data = new DataObject();
            data.SetData(
                DataFormats.Html,
                "<img src=\"data:image/png;base64," +
                Convert.ToBase64String(payload) +
                "\">");
            var reader = new DropImageDataReader(
                new DropImageAcquisitionSafetyLimits
                {
                    MaxEmbeddedDataUrlBytes = 16
                });
            var analysis = reader.Analyze(data);
            var candidate = analysis.Candidates.Single(item =>
                item.Kind == DropImageCandidateKind.EncodedDataUrl);

            var result = await reader.MaterializeCandidateAsync(
                analysis,
                candidate);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.ImageData);
            StringAssert.Contains(
                result.FailureReason ?? string.Empty,
                "Safety-Limit");
        });
    }

    [TestMethod]
    public void MaterializeCandidateAsync_CustomBitmapSafetyLimitRejectsOversizedBitmap()
    {
        StaTestRunner.Run(async () =>
        {
            var pixels = new byte[2 * 2 * 4];
            var bitmap = BitmapSource.Create(
                2,
                2,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                2 * 4);
            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, bitmap);
            var reader = new DropImageDataReader(
                new DropImageAcquisitionSafetyLimits
                {
                    MaxBitmapPixels = 3
                });
            var analysis = reader.Analyze(data);
            var candidate = analysis.Candidates.Single(item =>
                item.Kind == DropImageCandidateKind.BitmapFallback);

            var result = await reader.MaterializeCandidateAsync(
                analysis,
                candidate);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.ImageData);
            StringAssert.Contains(
                result.FailureReason ?? string.Empty,
                "Safety-Limit");
        });
    }

    [TestMethod]
    public void ReadAsync_UrlDownloadUsesOwnedTemporaryBackingAndDisposeCleansIt()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = CreatePngPayload().Concat(Enumerable.Repeat((byte)0xA5, 32_768)).ToArray();
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(CreateContentResponse(expected, "image/png")));
            var data = CreateHtmlUrlDrop("<img src=\"https://stream.example.test/cover.png\">");

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.IsNotNull(result.ImageData);
            var imageData = result.ImageData!;
            var tempPath = imageData.LocalFilePath;
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, imageData.PayloadKind);
            Assert.IsTrue(imageData.OwnsBackingFile);
            Assert.IsFalse(imageData.TryGetInMemoryBytes(out _));
            Assert.AreEqual(expected.LongLength, imageData.Length);
            Assert.AreEqual("https://stream.example.test/cover.png", imageData.SourceUrl);
            Assert.AreEqual("image/png", imageData.ContentType);
            Assert.IsNotNull(tempPath);
            Assert.IsTrue(File.Exists(tempPath));
            CollectionAssert.AreEqual(expected, await ReadImageBytesAsync(imageData));

            imageData.Dispose();

            Assert.IsTrue(imageData.IsDisposed);
            Assert.IsFalse(File.Exists(tempPath));
        });
    }

    [TestMethod]
    public void ReadAsync_ImageSignatureWinsOverIncorrectHtmlContentType()
    {
        StaTestRunner.Run(async () =>
        {
            var expected = CreatePngPayload();
            var handler = new StubHttpMessageHandler((_, _) =>
                Task.FromResult(CreateContentResponse(expected, "text/html")));
            var data = CreateHtmlUrlDrop("<img src=\"https://mislabelled.example.test/cover.png\">");

            var result = await new DropImageDataReader(handler).ReadAsync(data);

            Assert.IsNotNull(result.ImageData);
            using var imageData = result.ImageData!;
            Assert.AreEqual(DropImagePayloadKind.TemporaryFile, imageData.PayloadKind);
            CollectionAssert.AreEqual(expected, await ReadImageBytesAsync(imageData));
            Assert.IsFalse(result.DownloadInfo?.LooksLikeHtml ?? true);
        });
    }

    private static DropAcquisitionAnalysis CreateVariantAnalysis(
        string smallImageUrl,
        string pageUrl)
    {
        var urlCandidates = new[]
        {
            new DropUrlCandidate(
                smallImageUrl,
                "HTML Format:src",
                20,
                417,
                DropImageCandidateKind.HtmlDerivedUrl),
            new DropUrlCandidate(
                pageUrl,
                "System.String",
                90,
                null,
                DropImageCandidateKind.DirectUrl)
        };
        var candidates = urlCandidates
            .Select(candidate => new DropImageCandidate(
                candidate.Kind,
                candidate.Source,
                candidate.Priority,
                candidate.Width,
                null,
                candidate.Url))
            .ToArray();

        return new DropAcquisitionAnalysis(
            new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["Test fixture"]),
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            candidates,
            urlCandidates);
    }

    private static async Task<byte[]> ReadImageBytesAsync(DropImageData imageData)
    {
        await using var source = await imageData.OpenReadAsync();
        using var output = new MemoryStream();
        await source.CopyToAsync(output);
        return output.ToArray();
    }

    private static DataObject CreateHtmlUrlDrop(string html)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Html, html);
        return data;
    }

    private static byte[] CreatePngPayload()
    {
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x00
        ];
    }

    private static HttpResponseMessage CreateContentResponse(byte[] bytes, string contentType)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private static HttpResponseMessage CreateRedirectResponse(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }
}
