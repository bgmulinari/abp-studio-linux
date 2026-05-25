namespace AbpStudioLinux.Installer.Tests.Packaging;

public sealed class NativePackageInstallerTests
{
    [Fact]
    public void DebianInstallPreparesReadableTemporaryPackageWhenAptIsAvailable()
    {
        var root = TestPaths.CreateTempDirectory();
        var package = Path.Combine(root, "abp-studio_3.0.3_amd64.deb");
        File.WriteAllText(package, "package");

        var prepared = NativePackageInstaller.PreparePackageForInstall(NativePackageKind.Deb, package);

        try
        {
            if (!CommandExists("apt-get"))
            {
                Assert.Equal(package, prepared);
                return;
            }

            Assert.NotEqual(package, prepared);
            Assert.StartsWith(Path.GetTempPath(), prepared, StringComparison.Ordinal);
            Assert.Equal("package", File.ReadAllText(prepared));

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.OtherRead,
                    File.GetUnixFileMode(prepared));

                Assert.Equal(
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute,
                    File.GetUnixFileMode(Path.GetDirectoryName(prepared)!));
            }
        }
        finally
        {
            NativePackageInstaller.CleanupPreparedPackage(package, prepared);
        }

        if (CommandExists("apt-get"))
        {
            Assert.False(File.Exists(prepared));
        }
    }

    [Fact]
    public void PacmanInstallPromptsByDefault()
    {
        var command = NativePackageInstaller.BuildUserInstallCommand(
            NativePackageKind.Pacman,
            "/tmp/abp-studio-3.0.2-1-x86_64.pkg.tar.zst");

        var commandLine = new[] { command.FileName }.Concat(command.Arguments).ToArray();

        Assert.Contains("pacman", commandLine);
        Assert.Contains("-U", commandLine);
        Assert.DoesNotContain("--noconfirm", commandLine);
    }

    [Fact]
    public void PacmanInstallUsesNoConfirmWhenAssumeYesIsSet()
    {
        var command = NativePackageInstaller.BuildUserInstallCommand(
            NativePackageKind.Pacman,
            "/tmp/abp-studio-3.0.2-1-x86_64.pkg.tar.zst",
            assumeYes: true);

        var commandLine = new[] { command.FileName }.Concat(command.Arguments).ToArray();

        Assert.Contains("pacman", commandLine);
        Assert.Contains("-U", commandLine);
        Assert.Contains("--noconfirm", commandLine);
    }

    [Theory]
    [InlineData("3.0.2", "3.0.3", true)]
    [InlineData("3.0.3", "3.0.2", false)]
    [InlineData("3.0.2", "3.0.2", false)]
    [InlineData("3.0.2-preview.1", "3.0.2", true)]
    [InlineData("3.0.2", "3.0.2-preview.1", false)]
    public void DetectsDowngradeFromPackageAndInstalledVersions(
        string packageVersion,
        string installedVersion,
        bool expected)
    {
        Assert.Equal(expected, NativePackageInstaller.IsDowngrade(packageVersion, installedVersion));
    }

    [Fact]
    public void DebianInstallAllowsDowngradesWithAptGet()
    {
        var command = NativePackageInstaller.BuildUserInstallCommand(
            NativePackageKind.Deb,
            "/tmp/abp-studio_3.0.2_amd64.deb",
            assumeYes: true,
            allowDowngrade: true,
            availableCommands: AvailableCommands("apt-get"));

        var commandLine = ToCommandLine(command);

        Assert.Contains("apt-get", commandLine);
        Assert.Contains("install", commandLine);
        Assert.Contains("--allow-downgrades", commandLine);
        Assert.Contains("-y", commandLine);
    }

    [Fact]
    public void RpmInstallUsesDnfDowngradeWhenDowngrading()
    {
        var command = NativePackageInstaller.BuildUserInstallCommand(
            NativePackageKind.Rpm,
            "/tmp/abp-studio-3.0.2-1.x86_64.rpm",
            assumeYes: true,
            allowDowngrade: true,
            availableCommands: AvailableCommands("dnf"));

        var commandLine = ToCommandLine(command);

        Assert.Contains("dnf", commandLine);
        Assert.Contains("downgrade", commandLine);
        Assert.Contains("-y", commandLine);
        Assert.DoesNotContain("install", commandLine);
    }

    [Fact]
    public void RpmInstallAllowsDowngradesWithRpmFallback()
    {
        var command = NativePackageInstaller.BuildUserInstallCommand(
            NativePackageKind.Rpm,
            "/tmp/abp-studio-3.0.2-1.x86_64.rpm",
            allowDowngrade: true,
            availableCommands: AvailableCommands());

        var commandLine = ToCommandLine(command);

        Assert.Contains("rpm", commandLine);
        Assert.Contains("-Uvh", commandLine);
        Assert.Contains("--oldpackage", commandLine);
    }

    private static string[] ToCommandLine(ShellCommand command) =>
        new[] { command.FileName }.Concat(command.Arguments).ToArray();

    private static IReadOnlySet<string> AvailableCommands(params string[] commands) =>
        new HashSet<string>(commands, StringComparer.Ordinal);

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }
}
