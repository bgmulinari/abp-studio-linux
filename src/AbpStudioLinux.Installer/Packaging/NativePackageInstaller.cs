namespace AbpStudioLinux.Installer.Packaging;

public static class NativePackageInstaller
{
    private const string TemporaryPackagePrefix = "abp-studio-linux-";

    public static string PreparePackageForInstall(NativePackageKind kind, string packagePath)
    {
        if (kind != NativePackageKind.Deb || !CommandExists("apt-get"))
        {
            return packagePath;
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), TemporaryPackagePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temporaryDirectory,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        var temporaryPackage = Path.Combine(temporaryDirectory, Path.GetFileName(packagePath));
        File.Copy(packagePath, temporaryPackage, true);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                temporaryPackage,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead);
        }

        return temporaryPackage;
    }

    public static void CleanupPreparedPackage(string originalPackagePath, string preparedPackagePath)
    {
        if (string.Equals(
                Path.GetFullPath(originalPackagePath),
                Path.GetFullPath(preparedPackagePath),
                StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.GetDirectoryName(preparedPackagePath);

        if (directory is null
            || !Path.GetFileName(directory).StartsWith(TemporaryPackagePrefix, StringComparison.Ordinal)
            || !Directory.Exists(directory))
        {
            return;
        }

        Directory.Delete(directory, true);
    }

    public static ShellCommand BuildUserInstallCommand(
        NativePackageKind kind,
        string packagePath,
        bool forceReinstall = false,
        bool assumeYes = false,
        bool allowDowngrade = false,
        IReadOnlySet<string>? availableCommands = null)
    {
        var command = kind switch
        {
            NativePackageKind.Deb => BuildDebInstallCommand(packagePath, forceReinstall, assumeYes, allowDowngrade, availableCommands),
            NativePackageKind.Rpm => BuildRpmInstallCommand(packagePath, forceReinstall, assumeYes, allowDowngrade, availableCommands),
            NativePackageKind.Pacman => BuildPacmanInstallCommand(packagePath, assumeYes),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return IsRoot()
            ? command
            : new ShellCommand("sudo", new[] { command.FileName }.Concat(command.Arguments).ToArray());
    }

    public static bool IsDowngrade(string packageVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion)
            || string.IsNullOrWhiteSpace(packageVersion)
            || string.Equals(packageVersion, installedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SemanticVersion.TryParse(packageVersion, out var package)
               && SemanticVersion.TryParse(installedVersion, out var installed)
               && package.CompareTo(installed) < 0;
    }

    public static void RefreshUserDesktopIntegration(TextWriter stdout, TextWriter stderr)
    {
        RunOptional(new ShellCommand("xdg-desktop-menu", new[] { "forceupdate" }), stdout, stderr);
        RunOptional(new ShellCommand("kbuildsycoca6", new[] { "--noincremental" }), stdout, stderr);
        RunOptional(new ShellCommand("kbuildsycoca5", new[] { "--noincremental" }), stdout, stderr);
    }

    private static void RunOptional(ShellCommand command, TextWriter stdout, TextWriter stderr)
    {
        if (!CommandExists(command.FileName))
        {
            return;
        }

        var exitCode = ProcessRunner.Run(command, stdout, TextWriter.Null);

        if (exitCode != 0)
        {
            stderr.WriteLine($"[WARN] {command.ToDisplayString()} exited with code {exitCode}.");
        }
    }

    private static bool IsRoot() => string.Equals(Environment.UserName, "root", StringComparison.Ordinal);

    private static ShellCommand BuildDebInstallCommand(
        string packagePath,
        bool forceReinstall,
        bool assumeYes,
        bool allowDowngrade,
        IReadOnlySet<string>? availableCommands)
    {
        if (!CommandExists("apt-get", availableCommands))
        {
            return new ShellCommand("dpkg", new[] { "-i", packagePath });
        }

        var arguments = new List<string> { "install" };

        if (forceReinstall)
        {
            arguments.Add("--reinstall");
        }

        if (allowDowngrade)
        {
            arguments.Add("--allow-downgrades");
        }

        if (assumeYes)
        {
            arguments.Add("-y");
        }

        arguments.Add(packagePath);
        return new ShellCommand("apt-get", arguments);
    }

    private static ShellCommand BuildRpmInstallCommand(
        string packagePath,
        bool forceReinstall,
        bool assumeYes,
        bool allowDowngrade,
        IReadOnlySet<string>? availableCommands)
    {
        if (CommandExists("dnf", availableCommands))
        {
            var arguments = new List<string> { allowDowngrade ? "downgrade" : forceReinstall ? "reinstall" : "install" };

            if (assumeYes)
            {
                arguments.Add("-y");
            }

            arguments.Add(packagePath);
            return new ShellCommand("dnf", arguments);
        }

        if (CommandExists("zypper", availableCommands))
        {
            var arguments = new List<string>();

            if (assumeYes)
            {
                arguments.Add("--non-interactive");
            }

            arguments.Add("install");

            if (forceReinstall)
            {
                arguments.Add("--force");
            }

            if (allowDowngrade)
            {
                arguments.Add("--oldpackage");
            }

            arguments.Add("--allow-unsigned-rpm");
            arguments.Add(packagePath);
            return new ShellCommand("zypper", arguments);
        }

        var rpmArguments = new List<string> { "-Uvh" };

        if (forceReinstall)
        {
            rpmArguments.Add("--replacepkgs");
        }

        if (allowDowngrade)
        {
            rpmArguments.Add("--oldpackage");
        }

        rpmArguments.Add(packagePath);
        return new ShellCommand("rpm", rpmArguments);
    }

    private static ShellCommand BuildPacmanInstallCommand(string packagePath, bool assumeYes)
    {
        var arguments = new List<string> { "-U" };

        if (assumeYes)
        {
            arguments.Add("--noconfirm");
        }

        arguments.Add(packagePath);
        return new ShellCommand("pacman", arguments);
    }

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }

    private static bool CommandExists(string command, IReadOnlySet<string>? availableCommands) =>
        availableCommands?.Contains(command) ?? CommandExists(command);

    private sealed class SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(IReadOnlyList<long> release, IReadOnlyList<string> prerelease)
        {
            Release = release;
            Prerelease = prerelease;
        }

        private IReadOnlyList<long> Release { get; }

        private IReadOnlyList<string> Prerelease { get; }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null)
            {
                return 1;
            }

            var maxReleaseParts = Math.Max(Release.Count, other.Release.Count);

            for (var index = 0; index < maxReleaseParts; index++)
            {
                var left = index < Release.Count ? Release[index] : 0;
                var right = index < other.Release.Count ? other.Release[index] : 0;
                var releaseComparison = left.CompareTo(right);

                if (releaseComparison != 0)
                {
                    return releaseComparison;
                }
            }

            if (Prerelease.Count == 0 && other.Prerelease.Count == 0)
            {
                return 0;
            }

            if (Prerelease.Count == 0)
            {
                return 1;
            }

            if (other.Prerelease.Count == 0)
            {
                return -1;
            }

            var maxPrereleaseParts = Math.Max(Prerelease.Count, other.Prerelease.Count);

            for (var index = 0; index < maxPrereleaseParts; index++)
            {
                if (index >= Prerelease.Count)
                {
                    return -1;
                }

                if (index >= other.Prerelease.Count)
                {
                    return 1;
                }

                var prereleaseComparison = ComparePrereleasePart(Prerelease[index], other.Prerelease[index]);

                if (prereleaseComparison != 0)
                {
                    return prereleaseComparison;
                }
            }

            return 0;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            var normalized = value.Trim();
            var buildMetadataIndex = normalized.IndexOf('+');

            if (buildMetadataIndex >= 0)
            {
                normalized = normalized[..buildMetadataIndex];
            }

            var prerelease = Array.Empty<string>();
            var prereleaseIndex = normalized.IndexOf('-');

            if (prereleaseIndex >= 0)
            {
                prerelease = normalized[(prereleaseIndex + 1)..]
                    .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                normalized = normalized[..prereleaseIndex];
            }

            var release = normalized
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => long.TryParse(part, out var parsed) && parsed >= 0 ? parsed : (long?)null)
                .ToArray();

            if (release.Length == 0 || release.Any(part => part is null))
            {
                version = null!;
                return false;
            }

            version = new SemanticVersion(release.Select(part => part!.Value).ToArray(), prerelease);
            return true;
        }

        private static int ComparePrereleasePart(string left, string right)
        {
            var leftIsNumeric = long.TryParse(left, out var leftNumber);
            var rightIsNumeric = long.TryParse(right, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (leftIsNumeric)
            {
                return -1;
            }

            if (rightIsNumeric)
            {
                return 1;
            }

            return string.CompareOrdinal(left, right);
        }
    }
}
