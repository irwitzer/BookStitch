using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BookStitch.Services;

public sealed class CoverDropService
{
    private const int MaxDownloadedBytes = 50 * 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    public bool CanContainCoverFile(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            return files.Any(IsSupportedImageFile);

        if (data.GetDataPresent("FileGroupDescriptorW") || data.GetDataPresent("FileGroupDescriptor") || data.GetDataPresent("FileContents"))
            return true;

        if (TryGetUrlFromDataObject(data, out _))
            return true;

        if (data.GetDataPresent(DataFormats.Html) || data.GetDataPresent(DataFormats.Text) || data.GetDataPresent(DataFormats.UnicodeText))
            return true;

        return data.GetDataPresent(DataFormats.Bitmap);
    }

    public async Task<string?> MaterializeFirstCoverFileAsync(IDataObject data, string targetFolder, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);

        var localFile = GetFirstLocalSupportedFile(data);
        if (!string.IsNullOrWhiteSpace(localFile))
            return localFile;

        var virtualFile = await TrySaveVirtualFileAsync(data, targetFolder, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(virtualFile))
            return virtualFile;

        var bitmapFile = TrySaveBitmapData(data, targetFolder);
        if (!string.IsNullOrWhiteSpace(bitmapFile))
            return bitmapFile;

        if (TryGetUrlFromDataObject(data, out var url))
            return await TrySaveFromUrlAsync(url, targetFolder, cancellationToken).ConfigureAwait(false);

        return null;
    }

    private static string? GetFirstLocalSupportedFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return null;

        if (data.GetData(DataFormats.FileDrop) is not string[] files)
            return null;

        return files.FirstOrDefault(IsSupportedImageFile);
    }

    private static async Task<string?> TrySaveVirtualFileAsync(IDataObject data, string targetFolder, CancellationToken cancellationToken)
    {
        if (!data.GetDataPresent("FileContents"))
            return null;

        var fileName = TryGetFirstVirtualFileName(data);
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            extension = ".jpg";

        var safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "browser-cover";

        var targetPath = Path.Combine(targetFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safeName}{extension.ToLowerInvariant()}");
        var content = data.GetData("FileContents");

        await using var output = File.Create(targetPath);

        if (content is MemoryStream memoryStream)
        {
            memoryStream.Position = 0;
            await CopyLimitedAsync(memoryStream, output, cancellationToken).ConfigureAwait(false);
            return targetPath;
        }

        if (content is Stream stream)
        {
            await CopyLimitedAsync(stream, output, cancellationToken).ConfigureAwait(false);
            return targetPath;
        }

        output.Close();
        TryDeleteFile(targetPath);
        return null;
    }

    private static string? TrySaveBitmapData(IDataObject data, string targetFolder)
    {
        if (!data.GetDataPresent(DataFormats.Bitmap))
            return null;

        var bitmapData = data.GetData(DataFormats.Bitmap);
        if (bitmapData is not BitmapSource bitmapSource)
            return null;

        var targetPath = Path.Combine(targetFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-browser-cover.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        using var output = File.Create(targetPath);
        encoder.Save(output);

        return targetPath;
    }

    private static async Task<string?> TrySaveFromUrlAsync(string urlText, string targetFolder, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
            return null;

        if (uri.IsFile)
        {
            var localPath = uri.LocalPath;
            return IsSupportedImageFile(localPath) ? localPath : null;
        }

        if (uri.Scheme is not "http" and not "https")
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var extension = GetImageExtensionFromResponse(response) ?? GetImageExtensionFromUrl(uri) ?? ".jpg";
        var targetPath = Path.Combine(targetFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-browser-cover{extension}");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(targetPath);
        await CopyLimitedAsync(input, output, cancellationToken).ConfigureAwait(false);

        return targetPath;
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var totalBytes = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            totalBytes += read;
            if (totalBytes > MaxDownloadedBytes)
                throw new InvalidOperationException("Das abgelegte Bild ist größer als 50 MB und wurde deshalb nicht übernommen.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string? TryGetFirstVirtualFileName(IDataObject data)
    {
        if (data.GetDataPresent("FileGroupDescriptorW") && data.GetData("FileGroupDescriptorW") is MemoryStream unicodeDescriptor)
            return TryReadFileGroupDescriptorFileName(unicodeDescriptor, Encoding.Unicode);

        if (data.GetDataPresent("FileGroupDescriptor") && data.GetData("FileGroupDescriptor") is MemoryStream ansiDescriptor)
            return TryReadFileGroupDescriptorFileName(ansiDescriptor, Encoding.Default);

        return null;
    }

    private static string? TryReadFileGroupDescriptorFileName(MemoryStream descriptorStream, Encoding encoding)
    {
        var bytes = descriptorStream.ToArray();
        if (bytes.Length < 4 + 72)
            return null;

        var fileNameOffset = 4 + 72;
        var maxFileNameBytes = Math.Min(bytes.Length - fileNameOffset, encoding == Encoding.Unicode ? 520 : 260);
        if (maxFileNameBytes <= 0)
            return null;

        var rawName = encoding.GetString(bytes, fileNameOffset, maxFileNameBytes);
        var nullIndex = rawName.IndexOf('\0');
        if (nullIndex >= 0)
            rawName = rawName[..nullIndex];

        return string.IsNullOrWhiteSpace(rawName) ? null : rawName.Trim();
    }

    private static bool TryGetUrlFromDataObject(IDataObject data, out string url)
    {
        url = "";

        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", "text/x-moz-url", "text/uri-list" })
        {
            if (!data.GetDataPresent(format))
                continue;

            var value = ReadDataObjectValueAsString(data.GetData(format));
            url = ExtractFirstUrl(value);
            if (!string.IsNullOrWhiteSpace(url))
                return true;
        }

        foreach (var format in new[] { DataFormats.Html, DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format))
                continue;

            var value = ReadDataObjectValueAsString(data.GetData(format));
            url = ExtractFirstUrl(value);
            if (!string.IsNullOrWhiteSpace(url))
                return true;
        }

        return false;
    }

    private static string ExtractFirstUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decoded = WebUtility.HtmlDecode(value).Trim();

        var imageSourceMatch = Regex.Match(decoded, @"<img[^>]+src\s*=\s*['""](?<url>[^'"" >]+)", RegexOptions.IgnoreCase);
        if (imageSourceMatch.Success)
            return imageSourceMatch.Groups["url"].Value.Trim();

        var urlMatch = Regex.Match(decoded, @"(?<url>https?://[^\s'""<>]+|file:///[^\s'""<>]+)", RegexOptions.IgnoreCase);
        if (urlMatch.Success)
            return urlMatch.Groups["url"].Value.Trim();

        var firstLine = decoded.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return Uri.TryCreate(firstLine, UriKind.Absolute, out _) ? firstLine : "";
    }

    private static string ReadDataObjectValueAsString(object? value)
    {
        if (value is null)
            return "";

        if (value is string text)
            return text;

        if (value is MemoryStream memoryStream)
        {
            var bytes = memoryStream.ToArray();
            if (bytes.Length >= 2 && bytes[1] == 0)
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');

            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        }

        if (value is Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd().TrimEnd('\0');
        }

        return value.ToString() ?? "";
    }

    private static string? GetImageExtensionFromResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        return mediaType switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => null
        };
    }

    private static string? GetImageExtensionFromUrl(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension) ? extension : null;
    }

    private static bool IsSupportedImageFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        return SupportedExtensions.Contains(Path.GetExtension(filePath));
    }

    private static string MakeSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned.Length <= 80 ? cleaned : cleaned[..80];
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failure is not important here.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("BookStitch/1.0");
        return client;
    }
}
