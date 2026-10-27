using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using CoverImageDrop.Acquisition;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImageHostWorkflowTests
{
    [TestMethod]
    public void BrowserDrop_AnalyzeThenStandardCoverLoad_UsesProfileAwareCandidateSelectionAndPreview()
    {
        RunSta(() =>
        {
            var small = TestImageFactory.CreateSolidPng(300, 300, Colors.IndianRed);
            var large = TestImageFactory.CreateSolidPng(1600, 1600, Colors.ForestGreen);
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

            var data = new DataObject();
            data.SetData(
                DataFormats.Html,
                """
                Version:1.0
                SourceURL:https://covers.example.test/page.html
                <html>
                  <body>
                    <img src="/small.png">
                    <source srcset="/large.png 1600w">
                  </body>
                </html>
                """);

            var reader = new DropImageDataReader(handler);
            var analysis = reader.Analyze(data);
            var service = new CoverImagePipelineService(
                options: null,
                acquisitionReader: reader);

            var load = service
                .LoadStandardCoverAsync(analysis)
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.IsNotNull(load.Session);
            Assert.IsNotNull(load.Inspection);
            Assert.AreEqual(1600, load.Inspection!.PixelWidth);
            Assert.IsGreaterThanOrEqualTo(2, load.CandidateAttempts.Count);
            Assert.IsTrue(load.CandidateAttempts.Any(attempt =>
                attempt.Materialized &&
                !attempt.Accepted &&
                attempt.Inspection?.PixelWidth == 300));
            Assert.IsTrue(load.CandidateAttempts.Any(attempt =>
                attempt.Accepted &&
                attempt.Inspection?.PixelWidth == 1600));

            var session = load.Session!;
            var temporaryPath = session.ImageData.LocalFilePath;
            Assert.IsNotNull(temporaryPath);
            Assert.IsTrue(System.IO.File.Exists(temporaryPath));

            var preview = session.CreatePreview(800);
            Assert.AreEqual("image/png", preview.MimeType);
            Assert.AreEqual(800, preview.Width);
            Assert.AreEqual(800, preview.Height);

            var previewCrop = session.CreatePreviewCropState(400);
            session.UpdateCropFromPreview(previewCrop);
            Assert.AreEqual(previewCrop.CropRect, session.CropState.Rect);

            var final = session.FinalizeAsync().GetAwaiter().GetResult();
            Assert.IsTrue(final.IsSuccess);
            Assert.IsNotNull(final.Output);
            Assert.AreEqual(2000, final.Output!.Width);
            Assert.AreEqual(2000, final.Output.Height);

            session.Dispose();
            Assert.IsFalse(System.IO.File.Exists(temporaryPath));
        });
    }

    private static HttpResponseMessage CreateImageResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return response;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
