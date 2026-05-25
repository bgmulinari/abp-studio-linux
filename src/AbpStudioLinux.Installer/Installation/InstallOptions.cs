namespace AbpStudioLinux.Installer.Installation;

public sealed record NativeAssetVersions(
    string Cef,
    string CefGlue,
    string SkiaSharp,
    string HarfBuzzSharp,
    string SqlitePclRaw,
    string LibGit2SharpNative,
    string MongoDbLibmongocrypt,
    string TmdsDBusProtocol)
{
    public static NativeAssetVersions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("CEF_VERSION") ?? "120.1.8",
        Environment.GetEnvironmentVariable("CEFGLUE_VERSION") ?? "1.1.0",
        Environment.GetEnvironmentVariable("SKIASHARP_VERSION") ?? "3.116.1",
        Environment.GetEnvironmentVariable("HARFBUZZSHARP_VERSION") ?? "8.3.1.1",
        Environment.GetEnvironmentVariable("SQLITEPCLRAW_VERSION") ?? "2.1.10",
        Environment.GetEnvironmentVariable("LIBGIT2SHARP_NATIVE_VERSION") ?? "2.0.323",
        Environment.GetEnvironmentVariable("MONGODB_LIBMONGOCRYPT_VERSION") ?? "1.12.0",
        Environment.GetEnvironmentVariable("TMDS_DBUS_PROTOCOL_VERSION") ?? "0.92.0");
}

public sealed record InstallOptions(
    string RepositoryRoot,
    string WorkDirectory,
    string DistDirectory,
    string AppDirectory,
    string? RequestedVersion,
    NativePackageKind? PackageFormat,
    bool SkipInstall,
    bool AssumeYes,
    bool Force,
    string DotNetCommand,
    string NodeCommand,
    string? RuntimeRootDirectory,
    string NativeOverridesDirectory,
    string DownloadDirectory,
    string DotNetBundleDirectory,
    string NuGetPackagesDirectory,
    NativeAssetVersions NativeAssetVersions)
{
    public static InstallOptions From(OptionReader options)
    {
        var repositoryRoot = Path.GetFullPath(options.Value("--repo-root") ?? Directory.GetCurrentDirectory());
        var outputRoot = Path.GetFullPath(options.Value("--output-root") ?? Path.Combine(repositoryRoot, "output"));
        var workDirectory = Path.GetFullPath(options.Value("--work-dir") ?? Path.Combine(outputRoot, "work", "install"));
        var requestedVersion = ReadRequestedVersion(options);
        var distDirectory = Path.GetFullPath(options.Value("--dist-dir") ?? Path.Combine(outputRoot, "dist"));
        var packageFormat = options.Value("--format") is { } format
            ? NativePackageKindParser.Parse(format)
            : (NativePackageKind?)null;

        return new InstallOptions(
            repositoryRoot,
            workDirectory,
            distDirectory,
            Path.GetFullPath(options.Value("--app-dir") ?? Path.Combine(outputRoot, "abp-studio-app")),
            requestedVersion,
            packageFormat,
            options.Has("--no-install"),
            options.Has("--yes") || options.Has("-y"),
            options.Has("--force"),
            options.Value("--dotnet") ?? Environment.GetEnvironmentVariable("DOTNET_CMD") ?? "dotnet",
            options.Value("--node") ?? Environment.GetEnvironmentVariable("NODE_CMD") ?? "node",
            options.Value("--runtime-root") is { } runtimeRoot
                ? Path.GetFullPath(runtimeRoot)
                : Environment.GetEnvironmentVariable("RUNTIME_ROOT") is { Length: > 0 } runtimeRootFromEnvironment
                    ? Path.GetFullPath(runtimeRootFromEnvironment)
                    : null,
            Path.GetFullPath(options.Value("--native-overrides") ?? Path.Combine(workDirectory, "native-overrides")),
            Path.GetFullPath(options.Value("--download-dir") ?? Path.Combine(workDirectory, "downloads")),
            Path.GetFullPath(options.Value("--dotnet-bundle-dir") ?? Path.Combine(workDirectory, "dotnet-bundle")),
            Path.GetFullPath(options.Value("--nuget-packages") ?? Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(workDirectory, "nuget-packages")),
            NativeAssetVersions.FromEnvironment());
    }

    private static string? ReadRequestedVersion(OptionReader options)
    {
        var version = options.Value("--version");
        var upstreamVersion = options.Value("--upstream-version");

        if (version is not null
            && upstreamVersion is not null
            && !string.Equals(version, upstreamVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("--version and --upstream-version cannot specify different values.");
        }

        var value = version ?? upstreamVersion;

        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("--version requires a value.");
        }

        return value.Trim();
    }
}
