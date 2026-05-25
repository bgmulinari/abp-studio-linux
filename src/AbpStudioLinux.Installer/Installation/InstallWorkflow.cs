namespace AbpStudioLinux.Installer.Installation;

public sealed class InstallWorkflow
{
    private readonly TextWriter _stderr;
    private readonly TextWriter _stdout;

    public InstallWorkflow(TextWriter stdout, TextWriter stderr)
    {
        _stdout = stdout;
        _stderr = stderr;
    }

    public async Task<int> ExecuteAsync(InstallOptions options, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.WorkDirectory);
        Directory.CreateDirectory(options.DistDirectory);
        Directory.CreateDirectory(options.DownloadDirectory);

        PrerequisiteChecker.VerifyUserManagedPrerequisites(options.DotNetCommand, options.NodeCommand, _stderr);

        var kind = options.PackageFormat ?? PackageFormatDetector.Detect();
        Info($"Using package format: {NativePackageKindParser.ToCliValue(kind)}");

        PrerequisiteChecker.InstallOrUpdateAbpCli(options.DotNetCommand, options.RequestedVersion, _stdout, _stderr);

        var targetVersion = GetTargetVersion(options);

        if (InstalledPackageDetector.IsVersionInstalled(kind, targetVersion, out var installedVersion))
        {
            if (!options.Force)
            {
                _stderr.WriteLine($"ABP Studio {targetVersion} is already installed. Use --force to reinstall it anyway.");
                return 0;
            }

            Info($"ABP Studio {installedVersion} is already installed; reinstalling because --force was specified");
        }

        var sourcePackage = await GetSourcePackageAsync(options, targetVersion, cancellationToken);

        Info("Checking ABP Studio .NET runtime requirements");
        var packagePayloadDirectory = BuildAppCommand.ExtractPackagePayload(
            sourcePackage.Path,
            Path.Combine(options.WorkDirectory, "runtime-check"));
        var runtimeRequirements = PrerequisiteChecker.GetAppRuntimeRequirements(packagePayloadDirectory);
        using var dotNetHttpClient = new HttpClient();
        var runtimeRootDirectory = options.RuntimeRootDirectory
                                   ?? await new DotNetSdkBundleProvider(dotNetHttpClient).PrepareAsync(
                                       runtimeRequirements,
                                       options.DownloadDirectory,
                                       options.DotNetBundleDirectory,
                                       _stdout,
                                       _stderr,
                                       cancellationToken);
        Info($"Bundling .NET SDK/runtime from: {runtimeRootDirectory}");

        NativeAssetPreparer.Prepare(options, _stdout, _stderr);

        Info("Converting ABP Studio package");
        BuildAppCommand.Execute(new BuildAppOptions(
            sourcePackage.Path,
            options.AppDirectory,
            Path.Combine(options.WorkDirectory, "build-app"),
            options.NativeOverridesDirectory,
            runtimeRootDirectory,
            packagePayloadDirectory));

        var packageVersion = AppVersionDetector.GetVersion(options.AppDirectory);
        var nativePackage = PackageBuildOrchestrator.Build(kind, options, packageVersion, _stdout, _stderr);

        if (options.SkipInstall)
        {
            Info("Skipping package installation");
            _stdout.WriteLine(nativePackage);
            return 0;
        }

        Info("Installing ABP Studio package");
        var installedPackageVersion = InstalledPackageDetector.GetInstalledVersion(kind);
        var reinstallExistingVersion = options.Force
                                       && string.Equals(installedPackageVersion, packageVersion, StringComparison.OrdinalIgnoreCase);
        var allowDowngrade = NativePackageInstaller.IsDowngrade(packageVersion, installedPackageVersion);

        if (allowDowngrade)
        {
            Info($"Installed ABP Studio {installedPackageVersion} is newer than target {packageVersion}; allowing package manager downgrade");
        }

        var installPackage = NativePackageInstaller.PreparePackageForInstall(kind, nativePackage);
        int exitCode;

        try
        {
            var installCommand = NativePackageInstaller.BuildUserInstallCommand(
                kind,
                installPackage,
                reinstallExistingVersion,
                options.AssumeYes,
                allowDowngrade);
            exitCode = ProcessRunner.RunInteractive(installCommand);
        }
        finally
        {
            NativePackageInstaller.CleanupPreparedPackage(nativePackage, installPackage);
        }

        if (exitCode != 0)
        {
            return exitCode;
        }

        Info("Refreshing desktop integration");
        NativePackageInstaller.RefreshUserDesktopIntegration(_stdout, _stderr);
        return 0;
    }

    private string GetTargetVersion(InstallOptions options)
    {
        if (options.RequestedVersion is not null)
        {
            Info($"Using requested ABP Studio version: {options.RequestedVersion}");
            EnsureInstalledCliVersion(options);
            return options.RequestedVersion;
        }

        Info("Reading ABP Studio version from Volo.Abp.Studio.Cli");
        var latestVersion = UpstreamResolver.ResolveInstalledCliVersion(options.DotNetCommand);
        Info($"ABP Studio CLI version: {latestVersion}");
        return latestVersion;
    }

    private void EnsureInstalledCliVersion(InstallOptions options)
    {
        var installedCliVersion = UpstreamResolver.ResolveInstalledCliVersion(options.DotNetCommand);
        Info($"ABP Studio CLI version: {installedCliVersion}");

        if (!string.Equals(installedCliVersion, options.RequestedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ABP CLI version mismatch. Expected {options.RequestedVersion}, but dotnet tool list reported {installedCliVersion}.");
        }
    }

    private async Task<DownloadedPackage> GetSourcePackageAsync(
        InstallOptions options,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var packageUrl = UpstreamResolver.CreateStableFullPackageUri(targetVersion);

        var output = Path.Combine(options.DownloadDirectory, $"abp-studio-{targetVersion}-stable-full.zip");
        return await new PackageDownloader(httpClient).GetOrDownloadAsync(packageUrl, output, _stderr, cancellationToken);
    }

    private void Info(string message)
    {
        _stderr.WriteLine($"[INFO] {message}");
    }
}
