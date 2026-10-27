using System.Net;
using System.Net.Http;
using System.Windows.Media;
using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImagePipelineCandidateSelectionTests
{
    [TestMethod]
    public async Task LoadStandardCoverAsync_OversizedDeclaredDownloadSkipsToLaterCandidate()
    {
        const long maxInputBytes = 100_000;
        var first = TestImageFactory.CreateSolidPng(500, 500, Colors.Red);
        var second = TestImageFactory.CreateSolidPng(500, 500, Colors.Green);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/oversized.png" => CreateImageResponse(first),
                "/valid.png" => CreateImageResponse(second),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            if (request.RequestUri?.AbsolutePath == "/oversized.png")
            {
                response.Content.Headers.ContentLength = maxInputBytes + 1;
            }

            return Task.FromResult(response);
        });
        var analysis = CreateUrlAnalysis(
            ("https://covers.example.test/oversized.png", 10, 500),
            ("https://covers.example.test/valid.png", 20, 500));
        var options = new CoverImagePipelineOptions
        {
            Policy = StandardCoverPolicy.Default with
            {
                MaxInputBytes = maxInputBytes
            }
        };
        var service = new CoverImagePipelineService(
            options,
            new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[0].Materialized);
        Assert.IsTrue(result.CandidateAttempts[0].DownloadAttempts.Any(attempt =>
            (attempt.Error ?? string.Empty).Contains(
                "konfigurierte Limit",
                StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.CandidateAttempts[1].Accepted);

        await result.Session!.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_FirstUrlTooSmall_UsesLaterLargeUrl()
    {
        var small = TestImageFactory.CreateSolidPng(300, 300, Colors.Red);
        var large = TestImageFactory.CreateSolidPng(1600, 1600, Colors.Green);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var bytes = request.RequestUri?.AbsolutePath switch
            {
                "/small.png" => small,
                "/large.png" => large,
                _ => null
            };
            return Task.FromResult(bytes is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : CreateImageResponse(bytes));
        });
        var analysis = CreateUrlAnalysis(
            ("https://covers.example.test/small.png", 10, 300),
            ("https://covers.example.test/large.png", 20, 1600));
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Session);
        Assert.IsNotNull(result.Inspection);
        Assert.IsNotNull(result.SourceDetection);
        Assert.AreEqual(1600, result.Inspection!.PixelWidth);
        Assert.AreEqual(DropSourceKind.Chromium, result.SourceDetection!.Kind);
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[0].Accepted);
        Assert.IsTrue(result.CandidateAttempts[0].Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.ImageTooSmall));
        Assert.IsTrue(result.CandidateAttempts[1].Accepted);

        var session = result.Session!;
        var temporaryPath = session.ImageData.LocalFilePath!;
        Assert.IsFalse(string.IsNullOrWhiteSpace(temporaryPath));
        Assert.IsTrue(System.IO.File.Exists(temporaryPath));

        await session.DisposeAsync();

        Assert.IsFalse(System.IO.File.Exists(temporaryPath));
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_InvalidFirstUrl_ContinuesToNextCandidate()
    {
        var large = TestImageFactory.CreateSolidPng(1600, 1600, Colors.Blue);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/not-image.jpg" => CreateContentResponse("%PDF-1.7"u8.ToArray(), "image/jpeg"),
                "/large.png" => CreateImageResponse(large),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var analysis = CreateUrlAnalysis(
            ("https://covers.example.test/not-image.jpg", 10, null),
            ("https://covers.example.test/large.png", 20, 1600));
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[0].Materialized);
        Assert.IsFalse(result.CandidateAttempts[0].Accepted);
        Assert.IsTrue(result.CandidateAttempts[1].Accepted);

        await result.Session!.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_SmallPortraitDropPrefersLargerPortraitVariantFromPage()
    {
        const string smallUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
        const string landscapeUrl =
            "https://cdn.example.test/GettyImages-166673845-1440x720-1440x720-c-default.jpeg";
        const string portraitUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-800x1200-c-default.jpeg";
        const string pageUrl = "https://www.example.test/article";
        var small = TestImageFactory.CreateSolidPng(417, 626, Colors.SaddleBrown);
        var portrait = TestImageFactory.CreateSolidPng(800, 1200, Colors.ForestGreen);
        var requestedUrls = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUrls.Add(request.RequestUri!.AbsoluteUri);
            var response = request.RequestUri.AbsoluteUri switch
            {
                smallUrl => CreateImageResponse(small),
                pageUrl => CreateContentResponse(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"<meta property=\"og:image\" content=\"{landscapeUrl}\">" +
                        $"<img src=\"{smallUrl}\" srcset=\"{portraitUrl} 800w\">"),
                    "text/html"),
                portraitUrl => CreateImageResponse(portrait),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var analysis = CreateBrowserVariantAnalysis(smallUrl, pageUrl);
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Inspection);
        Assert.AreEqual(800, result.Inspection!.OrientedPixelWidth);
        Assert.AreEqual(1200, result.Inspection.OrientedPixelHeight);
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[0].Accepted);
        Assert.IsTrue(result.CandidateAttempts[0].Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.ImageTooSmall));
        Assert.IsTrue(result.CandidateAttempts[1].Accepted);
        Assert.IsFalse(requestedUrls.Contains(landscapeUrl));
        Assert.AreEqual(0, result.Session!.CropState.Rect.X);
        Assert.AreEqual(200, result.Session.CropState.Rect.Y);
        Assert.AreEqual(800, result.Session.CropState.Rect.Width);
        Assert.AreEqual(800, result.Session.CropState.Rect.Height);

        await result.Session.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_SmallPortraitDropDoesNotSilentlyAcceptLandscapeSocialCrop()
    {
        const string smallUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
        const string landscapeUrl =
            "https://cdn.example.test/GettyImages-166673845-1440x720-1440x720-c-default.jpeg";
        const string pageUrl = "https://www.example.test/article";
        var small = TestImageFactory.CreateSolidPng(417, 626, Colors.SaddleBrown);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = request.RequestUri!.AbsoluteUri switch
            {
                smallUrl => CreateImageResponse(small),
                pageUrl => CreateContentResponse(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"<meta property=\"og:image\" content=\"{landscapeUrl}\">"),
                    "text/html"),
                landscapeUrl => throw new AssertFailedException(
                    "Die deutlich anders beschnittene Querformatvariante darf nicht geladen werden."),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var analysis = CreateBrowserVariantAnalysis(smallUrl, pageUrl);
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.ImageTooSmall));
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[1].Accepted);
        Assert.IsFalse(result.CandidateAttempts[1].Materialized);
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_VariantReferenceDoesNotRejectBitmapFallback()
    {
        const string smallUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
        var small = TestImageFactory.CreateSolidPng(417, 626, Colors.SaddleBrown);
        var bitmap = TestImageFactory.CreateSolidPng(700, 700, Colors.MediumPurple);
        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsoluteUri == smallUrl
                ? CreateImageResponse(small)
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
        var bitmapData = DropImageData.FromBytes(bitmap, "Browser-Bitmap");
        var urlCandidate = new DropUrlCandidate(
            smallUrl,
            "HTML Format:src",
            20,
            417,
            DropImageCandidateKind.HtmlDerivedUrl);
        var analysis = new DropAcquisitionAnalysis(
            new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["Test fixture"]),
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            [
                new DropImageCandidate(
                    urlCandidate.Kind,
                    urlCandidate.Source,
                    urlCandidate.Priority,
                    urlCandidate.Width,
                    null,
                    urlCandidate.Url),
                new DropImageCandidate(
                    DropImageCandidateKind.BitmapFallback,
                    "Bitmap",
                    1_000,
                    null,
                    bitmapData,
                    null)
            ],
            [urlCandidate]);
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Inspection);
        Assert.AreEqual(700, result.Inspection!.OrientedPixelWidth);
        Assert.AreEqual(700, result.Inspection.OrientedPixelHeight);
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsFalse(result.CandidateAttempts[0].Accepted);
        Assert.IsTrue(result.CandidateAttempts[0].Issues.Any(issue =>
            issue.Code == CoverImageIssueCode.ImageTooSmall));
        Assert.IsTrue(result.CandidateAttempts[1].Accepted);
        Assert.AreEqual(DropImageCandidateKind.BitmapFallback, result.CandidateAttempts[1].Kind);

        await result.Session!.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_GoogleImageReferencePageRecoversSameShapePortraitVariant()
    {
        const string smallUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-417x626-c-default.jpeg";
        const string thumbnailUrl =
            "https://encrypted-tbn0.gstatic.com/images?q=thumbnail";
        const string articleUrl =
            "https://www.example.test/article";
        const string googlePageUrl =
            "https://www.google.com/imgres?q=elefant";
        const string landscapeUrl =
            "https://cdn.example.test/GettyImages-166673845-1440x720-1440x720-c-default.jpeg";
        const string portraitUrl =
            "https://cdn.example.test/GettyImages-166673845-768x1152-800x1200-c-default.jpeg";

        var small = TestImageFactory.CreateSolidPng(417, 626, Colors.SaddleBrown);
        var portrait = TestImageFactory.CreateSolidPng(800, 1200, Colors.ForestGreen);
        var requestedUrls = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUrls.Add(request.RequestUri!.AbsoluteUri);
            var response = request.RequestUri.AbsoluteUri switch
            {
                smallUrl => CreateImageResponse(small),
                thumbnailUrl => CreateImageResponse(small),
                articleUrl => CreateContentResponse(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"<meta property=\"og:image\" content=\"{landscapeUrl}\">" +
                        $"<img src=\"{smallUrl}\" srcset=\"{portraitUrl} 800w\">"),
                    "text/html"),
                portraitUrl => CreateImageResponse(portrait),
                landscapeUrl => throw new AssertFailedException(
                    "Die anders beschnittene Querformatvariante darf nicht geladen werden."),
                googlePageUrl => throw new AssertFailedException(
                    "Die Google-Resultatseite darf nicht benoetigt werden, wenn imgrefurl vorhanden ist."),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });

        var urlCandidates = new[]
        {
            new DropUrlCandidate(
                smallUrl,
                "System.String:imgurl",
                10,
                417,
                DropImageCandidateKind.HtmlDerivedUrl),
            new DropUrlCandidate(
                thumbnailUrl,
                "HTML Format:src",
                20,
                null,
                DropImageCandidateKind.HtmlDerivedUrl),
            new DropUrlCandidate(
                articleUrl,
                "System.String:imgrefurl",
                80,
                null,
                DropImageCandidateKind.DirectUrl),
            new DropUrlCandidate(
                googlePageUrl,
                "System.String",
                90,
                null,
                DropImageCandidateKind.DirectUrl)
        };
        var analysis = new DropAcquisitionAnalysis(
            new DropSourceDetection(
                DropSourceKind.Chromium,
                DropSourceConfidence.High,
                ["Test fixture"]),
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            urlCandidates.Select(value => new DropImageCandidate(
                value.Kind,
                value.Source,
                value.Priority,
                value.Width,
                null,
                value.Url)).ToArray(),
            urlCandidates);
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsTrue(result.IsSuccess, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Inspection);
        Assert.AreEqual(800, result.Inspection!.OrientedPixelWidth);
        Assert.AreEqual(1200, result.Inspection.OrientedPixelHeight);
        Assert.IsTrue(requestedUrls.Contains(articleUrl));
        Assert.IsTrue(requestedUrls.Contains(portraitUrl));
        Assert.IsFalse(requestedUrls.Contains(landscapeUrl));
        Assert.IsFalse(requestedUrls.Contains(googlePageUrl));
        Assert.IsTrue(result.CandidateAttempts.Last().Accepted);
        Assert.AreEqual(0, result.Session!.CropState.Rect.X);
        Assert.AreEqual(200, result.Session.CropState.Rect.Y);

        await result.Session.DisposeAsync();
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_CallerCancellationStopsCandidateLoop()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref requestCount);
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var analysis = CreateUrlAnalysis(
            ("https://covers.example.test/slow.png", 10, 1600),
            ("https://covers.example.test/never.png", 20, 1600));
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));
        using var cancellation = new CancellationTokenSource();

        var loadTask = service.LoadStandardCoverAsync(analysis, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        try
        {
            await loadTask;
            Assert.Fail("Caller-Cancellation haette weitergereicht werden muessen.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected.
        }

        Assert.AreEqual(1, requestCount);
    }

    [TestMethod]
    public async Task LoadStandardCoverAsync_AllCandidatesRejected_ReturnsMostInformativeValidationFailure()
    {
        var small = TestImageFactory.CreateSolidPng(300, 300, Colors.Orange);
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/not-image.jpg" => CreateContentResponse("not-an-image"u8.ToArray(), "application/octet-stream"),
                "/small.png" => CreateImageResponse(small),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        });
        var analysis = CreateUrlAnalysis(
            ("https://covers.example.test/not-image.jpg", 10, null),
            ("https://covers.example.test/small.png", 20, 300));
        var service = new CoverImagePipelineService(
            options: null,
            acquisitionReader: new DropImageDataReader(handler));

        var result = await service.LoadStandardCoverAsync(analysis);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(2, result.CandidateAttempts.Count);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == CoverImageIssueCode.ImageTooSmall));
        Assert.IsNotNull(result.Inspection);
        Assert.AreEqual(300, result.Inspection!.PixelWidth);
    }

    private static DropAcquisitionAnalysis CreateBrowserVariantAnalysis(
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
            .Select(value => new DropImageCandidate(
                value.Kind,
                value.Source,
                value.Priority,
                value.Width,
                null,
                value.Url))
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

    private static DropAcquisitionAnalysis CreateUrlAnalysis(
        params (string Url, int Priority, int? Width)[] values)
    {
        var urlCandidates = values
            .Select(value => new DropUrlCandidate(
                value.Url,
                "test",
                value.Priority,
                value.Width,
                DropImageCandidateKind.DirectUrl))
            .ToArray();
        var candidates = urlCandidates
            .Select(value => new DropImageCandidate(
                value.Kind,
                value.Source,
                value.Priority,
                value.Width,
                null,
                value.Url))
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

    private static HttpResponseMessage CreateImageResponse(byte[] bytes)
    {
        return CreateContentResponse(bytes, "image/png");
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

    private static string FormatIssues(IEnumerable<CoverImageIssue> issues)
    {
        return string.Join(
            Environment.NewLine,
            issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.TechnicalCause}"));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
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
