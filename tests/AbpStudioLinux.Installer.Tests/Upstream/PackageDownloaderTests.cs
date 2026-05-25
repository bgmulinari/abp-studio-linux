using System.Net;
using System.Text;

namespace AbpStudioLinux.Installer.Tests.Upstream;

public sealed class PackageDownloaderTests
{
    [Fact]
    public async Task DownloadAsyncReportsProgress()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "abp-studio.pkg");
        var content = Encoding.UTF8.GetBytes(new string('a', 1024 * 1024));
        using var httpClient = new HttpClient(new StubHandler(content));
        using var stderr = new StringWriter();

        var result = await new PackageDownloader(httpClient).DownloadAsync(
            new Uri("https://example.invalid/abp-studio.pkg"),
            output,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.Equal(output, result.Path);
        Assert.Equal(content, await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        Assert.Contains("[INFO] Download progress:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("[INFO] Downloaded package:", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOrDownloadAsyncUsesExistingNonEmptyPackage()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "abp-studio-3.0.3-stable-full.zip");
        var content = Encoding.UTF8.GetBytes("cached package");
        await File.WriteAllBytesAsync(output, content, TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient(new ThrowingHandler());
        using var stderr = new StringWriter();

        var result = await new PackageDownloader(httpClient).GetOrDownloadAsync(
            new Uri("https://example.invalid/abp-studio-3.0.3-stable-full.zip"),
            output,
            stderr,
            TestContext.Current.CancellationToken);

        Assert.Equal(output, result.Path);
        Assert.Equal(content, await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        Assert.Contains("[INFO] Using cached package:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("[INFO] Package SHA256:", stderr.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public StubHandler(byte[] content)
        {
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            response.Content.Headers.ContentLength = _content.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HTTP should not be used when a cached package is available.");
    }
}
