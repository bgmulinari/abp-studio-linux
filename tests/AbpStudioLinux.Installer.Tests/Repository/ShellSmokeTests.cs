using System.Diagnostics;

namespace AbpStudioLinux.Installer.Tests.Repository;

public sealed class ShellSmokeTests
{
    [Theory]
    [InlineData("install.sh")]
    [InlineData("scripts/lib/common.sh")]
    [InlineData("scripts/build-app.sh")]
    [InlineData("scripts/build-deb.sh")]
    [InlineData("scripts/build-rpm.sh")]
    [InlineData("scripts/build-pacman.sh")]
    [InlineData("packaging/linux/abp-studio.install")]
    [InlineData("packaging/linux/deb-postinst")]
    [InlineData("packaging/linux/deb-prerm")]
    [InlineData("packaging/linux/deb-postrm")]
    public void ShellScriptsParse(string relativePath)
    {
        var repo = TestPaths.RepositoryRoot();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "bash",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { "-n", Path.Combine(repo, relativePath) }
        })!;

        process.WaitForExit();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stderr);
    }

    [Fact]
    public void InstallerPrintsLogPathAfterHeaderAndMirrorsConsoleOutputToInstallLog()
    {
        var repo = TestPaths.RepositoryRoot();
        var root = TestPaths.CreateTempDirectory();
        var bin = Path.Combine(root, "bin");
        var log = Path.Combine(root, "logs", "install.log");
        Directory.CreateDirectory(bin);

        var dotnet = Path.Combine(bin, "dotnet");
        File.WriteAllText(dotnet, "#!/bin/sh\nexit 0\n");
        var shell = Path.Combine(bin, "zsh");
        File.WriteAllText(
            shell,
            """
            #!/bin/sh
            echo 'zsh 5.9 (x86_64-redhat-linux-gnu)'
            echo 'Copyright line that should not be logged'
            echo 'License line that should not be logged'
            """);
        var git = Path.Combine(bin, "git");
        File.WriteAllText(git, "#!/bin/sh\necho 0123456789abcdef0123456789abcdef01234567\n");

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(dotnet);
            MakeExecutable(shell);
            MakeExecutable(git);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { Path.Combine(repo, "install.sh"), "--skip-deps", "--no-install", "--yes" },
            Environment =
            {
                ["OUTPUT_ROOT"] = root,
                ["INSTALL_LOG_FILE"] = log
            }
        };
        startInfo.Environment["PATH"] = $"{bin}:{startInfo.Environment["PATH"]}";
        startInfo.Environment["SHELL"] = shell;

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(1, process.ExitCode);
        AssertStartupDiagnostics(stderr);
        Assert.Contains(".NET SDK 10.x is required", stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(log), $"Expected installer log at {log}.{Environment.NewLine}{stdout}{stderr}");

        var logText = File.ReadAllText(log);
        AssertStartupDiagnostics(logText);
        Assert.Contains(".NET SDK 10.x is required", logText, StringComparison.Ordinal);
    }

    private static void AssertStartupDiagnostics(string text)
    {
        Assert.Contains("[INFO] Startup diagnostics", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Distro:", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Kernel:", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Shell: zsh 5.9 (x86_64-redhat-linux-gnu)", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Package manager:", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   .NET:", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Node.js:", text, StringComparison.Ordinal);
        Assert.Contains("[INFO]   Options: skip_deps=1 no_install=1 fresh=0 force=0 yes=1 version=latest format=auto pkg=auto runtime_root=auto", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerPromptsBeforeActionsWithoutYes()
    {
        var repo = TestPaths.RepositoryRoot();
        var root = TestPaths.CreateTempDirectory();
        var bin = Path.Combine(root, "bin");
        var log = Path.Combine(root, "logs", "install.log");
        WriteFakePrerequisiteCommands(bin);

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { Path.Combine(repo, "install.sh"), "--skip-deps", "--no-install" },
            Environment =
            {
                ["OUTPUT_ROOT"] = root,
                ["INSTALL_LOG_FILE"] = log
            }
        };
        startInfo.Environment["PATH"] = $"{bin}:{startInfo.Environment["PATH"]}";

        using var process = Process.Start(startInfo)!;
        process.StandardInput.WriteLine("n");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        var normalizedStderr = NormalizeNewLines(stderr);
        Assert.Contains("[INFO] Using .NET SDK: 10.0.203", stderr, StringComparison.Ordinal);
        Assert.Contains("[INFO] Using Node.js: v20.11.0", stderr, StringComparison.Ordinal);
        Assert.Contains("\n\nProceed with building the ABP Studio package? [y/N]", normalizedStderr, StringComparison.Ordinal);
        Assert.True(
            normalizedStderr.IndexOf("[INFO] Using Node.js: v20.11.0", StringComparison.Ordinal) < normalizedStderr.IndexOf("Proceed with building the ABP Studio package? [y/N]", StringComparison.Ordinal),
            "User prerequisites should be checked before the confirmation prompt.");
        Assert.Contains("[INFO] Installation cancelled", stderr, StringComparison.Ordinal);

        Assert.True(File.Exists(log), $"Expected installer log at {log}.{Environment.NewLine}{stdout}{stderr}");
        var logText = File.ReadAllText(log);
        Assert.Contains("[INFO] Using .NET SDK: 10.0.203", logText, StringComparison.Ordinal);
        Assert.Contains("[INFO] Using Node.js: v20.11.0", logText, StringComparison.Ordinal);
        Assert.Contains("\n\nProceed with building the ABP Studio package? [y/N]", NormalizeNewLines(logText), StringComparison.Ordinal);
        Assert.Contains("[INFO] Installation cancelled", logText, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerLogsSigintInterrupt()
    {
        var repo = TestPaths.RepositoryRoot();
        var root = TestPaths.CreateTempDirectory();
        var bin = Path.Combine(root, "bin");
        var log = Path.Combine(root, "logs", "install.log");
        WriteFakePrerequisiteCommands(bin);

        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { Path.Combine(repo, "install.sh"), "--skip-deps", "--no-install" }
        };
        startInfo.Environment["OUTPUT_ROOT"] = root;
        startInfo.Environment["INSTALL_LOG_FILE"] = log;
        startInfo.Environment["PATH"] = $"{bin}:{startInfo.Environment["PATH"]}";

        using var process = Process.Start(startInfo)!;
        WaitForLogText(log, "Proceed with building the ABP Studio package? [y/N]");
        SendSignal("INT", process.Id);

        Assert.True(process.WaitForExit(5000), "Installer did not exit after SIGINT.");
        process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.Equal(130, process.ExitCode);
        Assert.Contains("[ERROR] Installation interrupted by SIGINT. Log saved to", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Installation failed with exit code 130", stderr, StringComparison.Ordinal);

        var logText = File.ReadAllText(log);
        Assert.Contains("[ERROR] Installation interrupted by SIGINT. Log saved to", logText, StringComparison.Ordinal);
        Assert.DoesNotContain("Installation failed with exit code 130", logText, StringComparison.Ordinal);
    }

    private static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void WaitForLogText(string path, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path) && File.ReadAllText(path).Contains(expected, StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(50);
        }

        var content = File.Exists(path) ? File.ReadAllText(path) : "<missing>";
        Assert.Fail($"Timed out waiting for log text '{expected}'. Log content:{Environment.NewLine}{content}");
    }

    private static void SendSignal(string signal, int processId)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "kill",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { $"-{signal}", processId.ToString() }
        })!;

        process.WaitForExit();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stderr);
    }

    private static void WriteFakePrerequisiteCommands(string bin)
    {
        Directory.CreateDirectory(bin);

        var dotnet = Path.Combine(bin, "dotnet");
        File.WriteAllText(
            dotnet,
            """
            #!/bin/sh
            if [ "$1" = "--list-sdks" ]; then
              echo '10.0.203 [/fake/dotnet/sdk]'
              exit 0
            fi
            exit 0
            """);

        var node = Path.Combine(bin, "node");
        File.WriteAllText(
            node,
            """
            #!/bin/sh
            echo 'v20.11.0'
            """);

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(dotnet);
            MakeExecutable(node);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
    }

    [Fact]
    public void DesktopFileUsesAbsoluteLauncherAndThemeIcon()
    {
        var repo = TestPaths.RepositoryRoot();
        var desktopFile = File.ReadAllText(Path.Combine(repo, "packaging", "linux", "abp-studio.desktop"));

        Assert.Contains("Exec=/usr/bin/abp-studio %U", desktopFile, StringComparison.Ordinal);
        Assert.Contains("Icon=abp-studio", desktopFile, StringComparison.Ordinal);
    }

    [Fact]
    public void RpmBuilderUsesConfigurableParallelPayloadCompression()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "scripts", "build-rpm.sh"));

        Assert.Contains("RPM_BUILD_NCPUS=\"$(detect_rpm_build_ncpus)\"", script, StringComparison.Ordinal);
        Assert.Contains("RPM_ZSTD_THREADS=\"${RPM_ZSTD_THREADS:-$RPM_BUILD_NCPUS}\"", script, StringComparison.Ordinal);
        Assert.Contains("RPM_BINARY_PAYLOAD=\"${RPM_BINARY_PAYLOAD:-w${RPM_ZSTD_LEVEL}T${RPM_ZSTD_THREADS}.zstdio}\"", script, StringComparison.Ordinal);
        Assert.Contains("--define \"_smp_build_ncpus $RPM_BUILD_NCPUS\"", script, StringComparison.Ordinal);
        Assert.Contains("--define \"_binary_payload $RPM_BINARY_PAYLOAD\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerReportsElapsedDurationAfterConfirmation()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));

        Assert.Contains("format_duration()", script, StringComparison.Ordinal);
        Assert.Contains("info \"ABP Studio package flow completed in $(format_duration \"$SECONDS\")\"", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("  ensure_user_prerequisites", StringComparison.Ordinal) < script.IndexOf("  confirm_installation", StringComparison.Ordinal),
            ".NET and Node.js should be checked before the confirmation prompt.");
        Assert.True(
            script.IndexOf("  confirm_installation", StringComparison.Ordinal) < script.IndexOf("  SECONDS=0", StringComparison.Ordinal),
            "Timer should start after confirmation.");
        Assert.True(
            script.IndexOf("  SECONDS=0", StringComparison.Ordinal) < script.IndexOf("  run_managed_installer", StringComparison.Ordinal),
            "Timer should start before the package flow actions.");
    }

    [Fact]
    public void InstallerLogsManagedInstallerCommandBeforeInvocation()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));

        Assert.Contains("shell_quote_command()", script, StringComparison.Ordinal);
        Assert.Contains("info \"Running managed installer: $(shell_quote_command \"$TOOL_BIN\" \"${args[@]}\")\"", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("info \"Running managed installer:", StringComparison.Ordinal) < script.IndexOf("  \"$TOOL_BIN\" \"${args[@]}\"", StringComparison.Ordinal),
            "Managed installer command should be logged before invocation.");
    }

    [Fact]
    public void BuildToolPublishesPortableDebugSymbols()
    {
        var repo = TestPaths.RepositoryRoot();
        var makefile = File.ReadAllText(Path.Combine(repo, "Makefile"));

        Assert.Contains("-p:DebugType=portable", makefile, StringComparison.Ordinal);
        Assert.Contains("-p:DebugSymbols=true", makefile, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("scripts/build-rpm.sh", "rpm-build.")]
    [InlineData("scripts/build-pacman.sh", "pacman-build.")]
    public void NativePackageBuildersUseOutputWorkForTemporaryBuildRoots(string relativePath, string buildRootPrefix)
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, relativePath));

        Assert.Contains("WORK_DIR=\"${WORK_DIR:-$OUTPUT_ROOT/work}\"", script, StringComparison.Ordinal);
        Assert.Contains("mkdir -p \"$WORK_DIR\"", script, StringComparison.Ordinal);
        Assert.Contains($"BUILD_ROOT=\"$(mktemp -d \"$WORK_DIR/{buildRootPrefix}XXXXXXXXXX\")\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerDependenciesBatchRequiredArchiveAndIcuPackages()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));

        Assert.Contains("dnf \"${dnf_args[@]}\" git make curl ca-certificates bsdtar libicu rpm-build mkcert wireguard-tools", script, StringComparison.Ordinal);
        Assert.Contains("apt-get \"${apt_args[@]}\" git make curl ca-certificates libarchive-tools libicu-dev dpkg-dev mkcert wireguard-tools", script, StringComparison.Ordinal);
        Assert.Contains("pacman \"${pacman_args[@]}\" git make curl ca-certificates libarchive icu base-devel mkcert wireguard-tools", script, StringComparison.Ordinal);
        Assert.Contains("zypper \"${zypper_args[@]}\" git make curl ca-certificates bsdtar libicu-devel rpm-build mkcert wireguard-tools", script, StringComparison.Ordinal);
        Assert.Contains("[ \"$ASSUME_YES\" -eq 1 ] && dnf_args+=(-y)", script, StringComparison.Ordinal);
        Assert.Contains("[ \"$ASSUME_YES\" -eq 1 ] && apt_args+=(-y)", script, StringComparison.Ordinal);
        Assert.Contains("[ \"$ASSUME_YES\" -eq 1 ] && pacman_args+=(--noconfirm)", script, StringComparison.Ordinal);
        Assert.Contains("[ \"$ASSUME_YES\" -eq 1 ] && zypper_args+=(--non-interactive)", script, StringComparison.Ordinal);
        Assert.Contains("No supported package manager found. Install the required dependencies manually or rerun with --skip-deps.", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerRepairsDefaultGeneratedOutputOwnership()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));

        Assert.Contains("ensure_generated_output_writable", script, StringComparison.Ordinal);
        Assert.Contains("run_sudo chown -R \"$(id -u):$(id -g)\" \"$OUTPUT_ROOT\"", script, StringComparison.Ordinal);
        Assert.Contains("Generated output directory is not writable: $OUTPUT_ROOT", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedInstallerUsesAbpCliToolVersionForStudioVersion()
    {
        var repo = TestPaths.RepositoryRoot();
        var resolver = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Upstream", "UpstreamResolver.cs"));
        var workflow = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Installation", "InstallWorkflow.cs"));

        Assert.Contains("\"tool\",", resolver, StringComparison.Ordinal);
        Assert.Contains("\"list\",", resolver, StringComparison.Ordinal);
        Assert.Contains("\"Volo.Abp.Studio.Cli\"", resolver, StringComparison.Ordinal);
        Assert.True(workflow.IndexOf("InstallOrUpdateAbpCli", StringComparison.Ordinal) < workflow.IndexOf("GetTargetVersion", StringComparison.Ordinal));
        Assert.Contains("options.RequestedVersion", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerForwardsRequestedVersionToManagedInstaller()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));

        Assert.Contains("--version VERSION", script, StringComparison.Ordinal);
        Assert.Contains("REQUESTED_VERSION=\"$2\"", script, StringComparison.Ordinal);
        Assert.Contains("args+=(--version \"$REQUESTED_VERSION\")", script, StringComparison.Ordinal);
        Assert.Contains("version=${REQUESTED_VERSION:-latest}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerPassesYesToNativePackageManagerOnlyWhenRequested()
    {
        var repo = TestPaths.RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repo, "install.sh"));
        var options = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Installation", "InstallOptions.cs"));
        var workflow = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Installation", "InstallWorkflow.cs"));
        var nativeInstaller = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Packaging", "NativePackageInstaller.cs"));

        Assert.Contains("-y, --yes", script, StringComparison.Ordinal);
        Assert.Contains("args+=(--yes)", script, StringComparison.Ordinal);
        Assert.Contains("options.Has(\"--yes\") || options.Has(\"-y\")", options, StringComparison.Ordinal);
        Assert.Contains("options.AssumeYes", workflow, StringComparison.Ordinal);
        Assert.Contains("ProcessRunner.RunInteractive(installCommand)", workflow, StringComparison.Ordinal);
        Assert.Contains("BuildPacmanInstallCommand(packagePath, assumeYes)", nativeInstaller, StringComparison.Ordinal);
        Assert.Contains("arguments.Add(\"--noconfirm\")", nativeInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void RpmInstallersUseDependencyResolvingPackageManagersWhenAvailable()
    {
        var repo = TestPaths.RepositoryRoot();
        var userInstaller = File.ReadAllText(Path.Combine(repo, "src", "AbpStudioLinux.Installer", "Packaging", "NativePackageInstaller.cs"));

        Assert.Contains("CommandExists(\"dnf\", availableCommands)", userInstaller, StringComparison.Ordinal);
        Assert.Contains("CommandExists(\"zypper\", availableCommands)", userInstaller, StringComparison.Ordinal);
        Assert.Contains("\"--allow-unsigned-rpm\"", userInstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void NativePackagesDeclareAvaloniaX11SessionDependencies()
    {
        var repo = TestPaths.RepositoryRoot();

        var control = File.ReadAllText(Path.Combine(repo, "packaging", "linux", "control.template"));
        var spec = File.ReadAllText(Path.Combine(repo, "packaging", "linux", "abp-studio.spec.template"));
        var pkgbuild = File.ReadAllText(Path.Combine(repo, "packaging", "linux", "PKGBUILD.template"));

        Assert.Contains("libice6", control, StringComparison.Ordinal);
        Assert.Contains("libsm6", control, StringComparison.Ordinal);
        Assert.Contains("Requires: libICE.so.6()(64bit)", spec, StringComparison.Ordinal);
        Assert.Contains("Requires: libSM.so.6()(64bit)", spec, StringComparison.Ordinal);
        Assert.Contains("'libice'", pkgbuild, StringComparison.Ordinal);
        Assert.Contains("'libsm'", pkgbuild, StringComparison.Ordinal);
    }
}
