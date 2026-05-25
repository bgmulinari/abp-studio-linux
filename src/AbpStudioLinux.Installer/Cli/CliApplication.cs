namespace AbpStudioLinux.Installer.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h")
        {
            WriteHelp(stdout);
            return 0;
        }

        try
        {
            var command = args[0];
            var options = new OptionReader(args.Skip(1).ToArray());

            return command switch
            {
                "install" => await RunInstallAsync(options, stdout, stderr, cancellationToken),
                "build-app" => RunBuildApp(options, stdout),
                "app-version" => RunAppVersion(options, stdout),
                "stage-package" => RunStagePackage(options, stdout),
                _ => Fail(stderr, $"Unknown command: {command}")
            };
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"[ERROR] {ex}");
            return 1;
        }
    }

    private static async Task<int> RunInstallAsync(
        OptionReader options,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var installOptions = InstallOptions.From(options);
        return await new InstallWorkflow(stdout, stderr).ExecuteAsync(installOptions, cancellationToken);
    }

    private static int RunBuildApp(OptionReader options, TextWriter stdout)
    {
        var commandOptions = new BuildAppOptions(
            RequiredPath(options, "--pkg"),
            Path.GetFullPath(options.Value("--output") ?? Path.Combine("output", "abp-studio-app")),
            Path.GetFullPath(options.Value("--work-dir") ?? Path.Combine("output", "work", "build-app")),
            OptionalPath(options, "--native-overrides"),
            OptionalPath(options, "--runtime-root"),
            OptionalPath(options, "--fixture-payload-dir"));

        var result = BuildAppCommand.Execute(commandOptions);
        stdout.WriteLine($"Generated app: {result.OutputDirectory}");
        stdout.WriteLine($"Removed files: {result.RemovedFileCount}");
        stdout.WriteLine($"App version: {result.AppVersion}");
        return 0;
    }

    private static int RunAppVersion(OptionReader options, TextWriter stdout)
    {
        stdout.WriteLine(AppVersionDetector.GetVersion(RequiredPath(options, "--app-dir")));
        return 0;
    }

    private static int RunStagePackage(OptionReader options, TextWriter stdout)
    {
        var stageOptions = new PackageStageOptions(
            NativePackageKindParser.Parse(options.Value("--format") ?? throw new ArgumentException("--format is required")),
            Path.GetFullPath(options.Value("--repo-root") ?? Directory.GetCurrentDirectory()),
            RequiredPath(options, "--app-dir"),
            RequiredPath(options, "--staging-root"));

        PackageStager.Stage(stageOptions);
        stdout.WriteLine($"Staged package root: {stageOptions.StagingRoot}");
        return 0;
    }

    private static string RequiredPath(OptionReader options, string name) => Path.GetFullPath(options.Value(name) ?? throw new ArgumentException($"{name} is required"));

    private static string? OptionalPath(OptionReader options, string name)
    {
        var value = options.Value(name);
        return value is null ? null : Path.GetFullPath(value);
    }

    private static int Fail(TextWriter stderr, string message)
    {
        stderr.WriteLine(message);
        return 1;
    }

    private static void WriteHelp(TextWriter stdout)
    {
        stdout.WriteLine("ABP Studio Linux packaging tool");
        stdout.WriteLine();
        stdout.WriteLine("Commands:");
        stdout.WriteLine("  install [--version <version>] [--format <deb|rpm|pacman>] [--output-root <dir>] [--runtime-root <dir>] [--no-install] [-y|--yes] [--force]");
        stdout.WriteLine("  build-app --pkg <path> [--output <dir>] [--runtime-root <dir>]");
        stdout.WriteLine("  app-version --app-dir <dir>");
        stdout.WriteLine("  stage-package --format <deb|rpm|pacman> --app-dir <dir> --staging-root <dir>");
    }
}
