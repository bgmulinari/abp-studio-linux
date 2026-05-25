using System.Security.Cryptography;

namespace AbpStudioLinux.Installer.Upstream;

public sealed record DownloadedPackage(
    string Path,
    string Sha256);

public sealed class PackageDownloader
{
    private const int BufferSize = 1024 * 128;
    private readonly HttpClient _httpClient;

    public PackageDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DownloadedPackage> GetOrDownloadAsync(Uri uri, string outputPath, TextWriter stderr, CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
        {
            var hash = await ComputeSha256Async(outputPath, cancellationToken);
            stderr.WriteLine($"[INFO] Using cached package: {outputPath}");
            stderr.WriteLine($"[INFO] Package SHA256: {hash}");
            return new DownloadedPackage(outputPath, hash);
        }

        return await DownloadAsync(uri, outputPath, stderr, cancellationToken);
    }

    public async Task<DownloadedPackage> DownloadAsync(Uri uri, string outputPath, TextWriter stderr, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var tempPath = outputPath + ".download";

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("abp-studio-linux-installer/1.0");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Package download failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {uri}");
        }

        var contentLength = response.Content.Headers.ContentLength;

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await CopyWithProgressAsync(input, output, contentLength, stderr, cancellationToken);
        }

        if (new FileInfo(tempPath).Length == 0)
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Package download produced an empty file.");
        }

        File.Move(tempPath, outputPath, true);
        var hash = await ComputeSha256Async(outputPath, cancellationToken);
        stderr.WriteLine($"[INFO] Downloaded package: {outputPath}");
        stderr.WriteLine($"[INFO] Package SHA256: {hash}");
        return new DownloadedPackage(outputPath, hash);
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        long? totalBytes,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        long downloadedBytes = 0;
        long lastReportedBytes = 0;
        var lastReportAt = DateTimeOffset.MinValue;

        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            var now = DateTimeOffset.UtcNow;

            if (ShouldReportProgress(downloadedBytes, lastReportedBytes, now, lastReportAt, totalBytes))
            {
                stderr.WriteLine(FormatProgress(downloadedBytes, totalBytes));
                lastReportedBytes = downloadedBytes;
                lastReportAt = now;
            }
        }

        stderr.WriteLine(FormatProgress(downloadedBytes, totalBytes));
    }

    private static bool ShouldReportProgress(
        long downloadedBytes,
        long lastReportedBytes,
        DateTimeOffset now,
        DateTimeOffset lastReportAt,
        long? totalBytes)
    {
        if (downloadedBytes == 0)
        {
            return false;
        }

        if (totalBytes is > 0)
        {
            var currentPercent = downloadedBytes * 100 / totalBytes.Value;
            var previousPercent = lastReportedBytes * 100 / totalBytes.Value;
            return currentPercent >= previousPercent + 5 || now - lastReportAt >= TimeSpan.FromSeconds(5);
        }

        return downloadedBytes - lastReportedBytes >= 10L * 1024 * 1024 || now - lastReportAt >= TimeSpan.FromSeconds(5);
    }

    private static string FormatProgress(long downloadedBytes, long? totalBytes)
    {
        if (totalBytes is > 0)
        {
            var percent = Math.Min(100, downloadedBytes * 100 / totalBytes.Value);
            return $"[INFO] Download progress: {percent}% ({FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes.Value)})";
        }

        return $"[INFO] Download progress: {FormatBytes(downloadedBytes)} downloaded";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(file, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
