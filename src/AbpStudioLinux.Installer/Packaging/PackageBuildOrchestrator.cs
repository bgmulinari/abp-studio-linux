namespace AbpStudioLinux.Installer.Packaging;

public static class PackageBuildOrchestrator
{
    public static string Build(
        NativePackageKind kind,
        InstallOptions options,
        string packageVersion,
        TextWriter stdout,
        TextWriter stderr)
    {
        var script = Path.Combine(options.RepositoryRoot, "scripts", $"build-{NativePackageKindParser.ToCliValue(kind)}.sh");

        if (!File.Exists(script))
        {
            throw new FileNotFoundException("Package build script is missing.", script);
        }

        stderr.WriteLine($"[INFO] Building native {NativePackageKindParser.ToCliValue(kind)} package");
        var exitCode = ProcessRunner.Run(
            new ShellCommand(script, Array.Empty<string>()),
            stdout,
            stderr,
            options.RepositoryRoot,
            new Dictionary<string, string>
            {
                ["APP_DIR"] = options.AppDirectory,
                ["DIST_DIR"] = options.DistDirectory,
                ["PACKAGE_VERSION"] = packageVersion
            });

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(script)} exited with code {exitCode}.");
        }

        return FindLatestPackage(kind, options.DistDirectory);
    }

    public static string FindLatestPackage(NativePackageKind kind, string distDirectory)
    {
        var pattern = kind switch
        {
            NativePackageKind.Deb => "abp-studio_*.deb",
            NativePackageKind.Rpm => "abp-studio-*.rpm",
            NativePackageKind.Pacman => "abp-studio-*.pkg.tar.*",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        var package = Directory.Exists(distDirectory)
            ? Directory.EnumerateFiles(distDirectory, pattern)
                .OrderBy(File.GetLastWriteTimeUtc)
                .LastOrDefault()
            : null;

        return package ?? throw new FileNotFoundException($"No {NativePackageKindParser.ToCliValue(kind)} package was produced in {distDirectory}.");
    }
}
