using System.Security.Cryptography;
using System.Text.Json;

namespace AbpStudioLinux.Installer.Installation;

public sealed record DotNetSdkDownload(
    string RuntimeVersion,
    string SdkVersion,
    string Rid,
    Uri Url,
    string? Hash);

public sealed class DotNetSdkBundleProvider
{
    private const string RuntimeName = "Microsoft.NETCore.App";
    private const string AspNetCoreRuntimeName = "Microsoft.AspNetCore.App";
    private const string RuntimeIdentifier = "linux-x64";

    private readonly HttpClient _httpClient;

    public DotNetSdkBundleProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> PrepareAsync(
        IReadOnlyList<DotNetRuntimeRequirement> requirements,
        string downloadDirectory,
        string bundleDirectory,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var runtimeVersion = ResolveRequiredRuntimeVersion(requirements);
        var releaseMetadata = await GetReleaseMetadataAsync(runtimeVersion.Major, cancellationToken);
        var download = ResolveSdkDownload(releaseMetadata, runtimeVersion, RuntimeIdentifier);
        var archive = Path.Combine(downloadDirectory, $"dotnet-sdk-{download.SdkVersion}-{download.Rid}.tar.gz");
        var extractRoot = Path.Combine(bundleDirectory, $"dotnet-sdk-{download.SdkVersion}-{download.Rid}");

        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(bundleDirectory);

        await DownloadAsync(download, archive, stderr, cancellationToken);
        VerifyHash(archive, download.Hash);

        if (!Directory.Exists(extractRoot) || !File.Exists(Path.Combine(extractRoot, "dotnet")))
        {
            BuildAppCommand.ResetDirectory(extractRoot);
            stderr.WriteLine($"[INFO] Extracting .NET SDK {download.SdkVersion}");
            var exitCode = ProcessRunner.Run(
                new ShellCommand("bsdtar", new[] { "-xf", archive, "-C", extractRoot }),
                stdout,
                stderr);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"bsdtar exited with code {exitCode} while extracting .NET SDK {download.SdkVersion}.");
            }
        }

        return extractRoot;
    }

    public static Version ResolveRequiredRuntimeVersion(IReadOnlyList<DotNetRuntimeRequirement> requirements)
    {
        var runtimeRequirements = requirements
            .Where(requirement => requirement.Name is RuntimeName or AspNetCoreRuntimeName)
            .ToArray();

        if (runtimeRequirements.Length == 0)
        {
            throw new InvalidOperationException("Could not determine ABP Studio .NET runtime requirements.");
        }

        var major = runtimeRequirements[0].Version.Major;

        if (runtimeRequirements.Any(requirement => requirement.Version.Major != major))
        {
            throw new InvalidOperationException(
                $"ABP Studio requires multiple .NET major versions: {string.Join(", ", runtimeRequirements.Select(requirement => $"{requirement.Name} {requirement.Version}"))}");
        }

        return runtimeRequirements
            .Select(requirement => requirement.Version)
            .OrderDescending()
            .First();
    }

    public static DotNetSdkDownload ResolveSdkDownload(string releasesJson, Version runtimeVersion, string rid)
    {
        using var document = JsonDocument.Parse(releasesJson);

        if (!document.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(".NET releases metadata did not contain a releases array.");
        }

        foreach (var release in releases.EnumerateArray())
        {
            if (!ReleaseMatchesRuntime(release, runtimeVersion))
            {
                continue;
            }

            if (!release.TryGetProperty("sdks", out var sdks) || sdks.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var sdk = sdks
                .EnumerateArray()
                .Select(item => TryReadSdkDownload(item, runtimeVersion, rid))
                .OfType<SdkDownloadCandidate>()
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (sdk is not null)
            {
                return sdk.Download;
            }

            break;
        }

        throw new InvalidOperationException($".NET SDK download for runtime {runtimeVersion} and RID {rid} was not found in release metadata.");
    }

    private async Task<string> GetReleaseMetadataAsync(int majorVersion, CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://builds.dotnet.microsoft.com/dotnet/release-metadata/{majorVersion}.0/releases.json");
        return await _httpClient.GetStringAsync(uri, cancellationToken);
    }

    private async Task DownloadAsync(
        DotNetSdkDownload download,
        string archive,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (File.Exists(archive))
        {
            stderr.WriteLine($"[INFO] Reusing cached .NET SDK {download.SdkVersion}: {archive}");
            return;
        }

        stderr.WriteLine($"[INFO] Downloading .NET SDK {download.SdkVersion} for ABP Studio runtime {download.RuntimeVersion}");
        using var response = await _httpClient.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var temporary = archive + ".tmp";

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(temporary))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        File.Move(temporary, archive, true);
    }

    private static bool ReleaseMatchesRuntime(JsonElement release, Version runtimeVersion)
    {
        if (release.TryGetProperty("runtime", out var runtime)
            && runtime.TryGetProperty("version", out var version)
            && string.Equals(version.GetString(), runtimeVersion.ToString(), StringComparison.Ordinal))
        {
            return true;
        }

        return release.TryGetProperty("release-version", out var releaseVersion)
               && string.Equals(releaseVersion.GetString(), runtimeVersion.ToString(), StringComparison.Ordinal);
    }

    private static SdkDownloadCandidate? TryReadSdkDownload(JsonElement sdk, Version runtimeVersion, string rid)
    {
        if (!sdk.TryGetProperty("version", out var versionProperty)
            || Version.TryParse(versionProperty.GetString(), out var sdkVersion) is false
            || !sdk.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("rid", out var fileRid)
                || !string.Equals(fileRid.GetString(), rid, StringComparison.Ordinal)
                || !file.TryGetProperty("url", out var urlProperty)
                || Uri.TryCreate(urlProperty.GetString(), UriKind.Absolute, out var url) is false)
            {
                continue;
            }

            if (file.TryGetProperty("name", out var name)
                && name.GetString()?.Contains("sdk", StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            var hash = file.TryGetProperty("hash", out var hashProperty)
                ? hashProperty.GetString()
                : null;

            return new SdkDownloadCandidate(
                sdkVersion,
                new DotNetSdkDownload(runtimeVersion.ToString(), sdkVersion.ToString(), rid, url, hash));
        }

        return null;
    }

    private static void VerifyHash(string archive, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return;
        }

        var normalizedExpected = expectedHash.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        var actual = normalizedExpected.Length switch
        {
            64 => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant(),
            128 => Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(archive))).ToLowerInvariant(),
            _ => null
        };

        if (actual is not null && !string.Equals(actual, normalizedExpected, StringComparison.Ordinal))
        {
            File.Delete(archive);
            throw new InvalidOperationException($"Downloaded .NET SDK hash mismatch for {archive}.");
        }
    }

    private sealed record SdkDownloadCandidate(
        Version Version,
        DotNetSdkDownload Download);
}
