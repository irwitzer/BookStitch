using CoverImageDrop.Acquisition;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Testing;

internal sealed record BrowserDropFixture
{
    public int SchemaVersion { get; init; }

    public string Origin { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<BrowserDropFixtureFormat> Formats { get; init; } = [];

    public BrowserDropFixtureBitmap? Bitmap { get; init; }

    public IReadOnlyList<BrowserDropFixtureHttpResponse> HttpResponses { get; init; } = [];

    public BrowserDropFixtureExpected Expected { get; init; } = new();
}

internal sealed record BrowserDropFixtureFormat
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

internal sealed record BrowserDropFixtureBitmap
{
    public int Width { get; init; }

    public int Height { get; init; }

    public string Argb { get; init; } = "#FF336699";
}

internal sealed record BrowserDropFixtureHttpResponse
{
    public string Url { get; init; } = string.Empty;

    public string Kind { get; init; } = "Image";

    public int? Width { get; init; }

    public int? Height { get; init; }
}

internal sealed record BrowserDropFixtureExpected
{
    public string SourceKind { get; init; } = string.Empty;

    public string SourceConfidence { get; init; } = string.Empty;

    public string PreferredCandidateKind { get; init; } = string.Empty;

    public string? PreferredCandidateUrl { get; init; }

    public string PreferredMaterializationResult { get; init; } = "Success";

    public IReadOnlyList<string> RequiredStandardCoverRequestUrls { get; init; } = [];

    public string PassThroughResult { get; init; } = string.Empty;

    public string StandardCoverResult { get; init; } = string.Empty;

    public int? ProcessingArtifactMinimumLength { get; init; }

    public int? DiagnosticArtifactMaximumLength { get; init; }
}

internal sealed class BrowserDropFixtureHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, ResponsePayload> _responses;
    private readonly ResponsePayload _defaultResponse;

    public BrowserDropFixtureHttpHandler(BrowserDropFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        _defaultResponse = fixture.HttpResponses.Count == 0
            ? CreateImagePayload(640, 640)
            : CreateHtmlNoImagePayload();
        _responses = fixture.HttpResponses.ToDictionary(
            response => response.Url,
            CreatePayload,
            StringComparer.OrdinalIgnoreCase);
    }

    public List<Uri> RequestedUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("Fixture request URI is missing.");
        RequestedUris.Add(requestUri);

        var payload = _responses.TryGetValue(
            requestUri.AbsoluteUri,
            out var configured)
            ? configured
            : _defaultResponse;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(payload.Bytes)
        };
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue(payload.ContentType);
        return Task.FromResult(response);
    }

    private static ResponsePayload CreatePayload(
        BrowserDropFixtureHttpResponse response)
    {
        return response.Kind switch
        {
            "Image" => CreateImagePayload(
                response.Width ?? 640,
                response.Height ?? 640),
            "HtmlNoImage" => CreateHtmlNoImagePayload(),
            _ => throw new InvalidOperationException(
                $"Unsupported fixture HTTP response kind: {response.Kind}")
        };
    }

    private static ResponsePayload CreateHtmlNoImagePayload()
    {
        return new ResponsePayload(
            Encoding.UTF8.GetBytes(
                "<html><body>sanitized captured page without image payload</body></html>"),
            "text/html");
    }

    private static ResponsePayload CreateImagePayload(
        int width,
        int height)
    {
        return new ResponsePayload(
            BrowserDropFixtureSupport.CreateSolidPng(
                width,
                height,
                Colors.SlateBlue),
            "image/png");
    }

    private sealed record ResponsePayload(
        byte[] Bytes,
        string ContentType);
}

internal static class BrowserDropFixtureSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<string> GetFixtureFileNames()
    {
        var root = GetFixtureRoot();
        return Directory.GetFiles(root, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Select(fileName => fileName!)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();
    }

    public static BrowserDropFixture Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var path = Path.Combine(GetFixtureRoot(), fileName);
        var json = File.ReadAllText(path);
        var fixture = JsonSerializer.Deserialize<BrowserDropFixture>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Fixture could not be deserialized: {fileName}");

        ValidateMetadata(fixture, fileName);
        return fixture;
    }

    public static DataObject CreateDataObject(BrowserDropFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var data = new DataObject();
        foreach (var format in fixture.Formats)
        {
            data.SetData(format.Name, format.Value);
        }

        if (fixture.Bitmap is not null)
        {
            data.SetData(DataFormats.Bitmap, CreateBitmap(fixture.Bitmap));
        }

        return data;
    }

    public static DropImageCandidate GetPreferredCandidate(DropAcquisitionAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return analysis.Candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("Fixture produced no image candidate.");
    }

    public static DropSourceKind ParseSourceKind(string value)
    {
        return Enum.Parse<DropSourceKind>(value, ignoreCase: false);
    }

    public static DropSourceConfidence ParseSourceConfidence(string value)
    {
        return Enum.Parse<DropSourceConfidence>(value, ignoreCase: false);
    }

    public static DropImageCandidateKind ParseCandidateKind(string value)
    {
        return Enum.Parse<DropImageCandidateKind>(value, ignoreCase: false);
    }

    public static byte[] CreateSolidPng(
        int width,
        int height,
        Color color)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static string GetFixtureRoot()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "BrowserDrops");

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Browser drop fixture directory is missing: {root}");
        }

        return root;
    }

    private static BitmapSource CreateBitmap(BrowserDropFixtureBitmap fixture)
    {
        if (fixture.Width <= 0 || fixture.Height <= 0)
        {
            throw new InvalidOperationException("Fixture bitmap dimensions must be positive.");
        }

        var color = ParseArgb(fixture.Argb);
        var stride = checked(fixture.Width * 4);
        var pixels = new byte[checked(stride * fixture.Height)];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(
            fixture.Width,
            fixture.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static Color ParseArgb(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Fixture bitmap ARGB value is missing.");
        }

        var normalized = value.Trim().TrimStart('#');
        if (normalized.Length != 8 ||
            !uint.TryParse(
                normalized,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var argb))
        {
            throw new InvalidOperationException(
                $"Fixture bitmap ARGB value is invalid: {value}");
        }

        return Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    private static void ValidateMetadata(
        BrowserDropFixture fixture,
        string fileName)
    {
        if (fixture.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported fixture schema version in {fileName}: {fixture.SchemaVersion}");
        }

        if (fixture.Origin is not ("synthetic" or "captured"))
        {
            throw new InvalidOperationException(
                $"Fixture origin must be synthetic or captured: {fileName}");
        }

        if (string.IsNullOrWhiteSpace(fixture.Id) ||
            string.IsNullOrWhiteSpace(fixture.Description))
        {
            throw new InvalidOperationException(
                $"Fixture metadata is incomplete: {fileName}");
        }

        _ = ParseSourceKind(fixture.Expected.SourceKind);
        _ = ParseSourceConfidence(fixture.Expected.SourceConfidence);
        _ = ParseCandidateKind(fixture.Expected.PreferredCandidateKind);

        if (fixture.Expected.PreferredMaterializationResult is not ("Success" or "Failure"))
        {
            throw new InvalidOperationException(
                $"Unsupported preferred-materialization expectation in {fileName}: {fixture.Expected.PreferredMaterializationResult}");
        }

        if (fixture.Expected.PassThroughResult is not ("Success" or "AcquisitionFailure"))
        {
            throw new InvalidOperationException(
                $"Unsupported PassThrough fixture expectation in {fileName}: {fixture.Expected.PassThroughResult}");
        }

        if (fixture.Expected.StandardCoverResult is not ("Success" or "ImageTooSmall" or "NoImagePayloadFound"))
        {
            throw new InvalidOperationException(
                $"Unsupported StandardCover fixture expectation in {fileName}: {fixture.Expected.StandardCoverResult}");
        }

        foreach (var requiredUrl in fixture.Expected.RequiredStandardCoverRequestUrls)
        {
            if (!Uri.TryCreate(requiredUrl, UriKind.Absolute, out var requiredUri) ||
                requiredUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    $"Required StandardCover request URL is invalid in {fileName}: {requiredUrl}");
            }
        }

        foreach (var response in fixture.HttpResponses)
        {
            if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var responseUri) ||
                responseUri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException(
                    $"Fixture HTTP response URL is invalid in {fileName}: {response.Url}");
            }

            if (response.Kind is not ("Image" or "HtmlNoImage"))
            {
                throw new InvalidOperationException(
                    $"Unsupported fixture HTTP response kind in {fileName}: {response.Kind}");
            }

            if (response.Kind == "Image" &&
                (response.Width is <= 0 || response.Height is <= 0))
            {
                throw new InvalidOperationException(
                    $"Fixture image response dimensions must be positive in {fileName}: {response.Url}");
            }
        }
    }
}
