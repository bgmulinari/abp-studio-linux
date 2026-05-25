namespace AbpStudioLinux.Installer.Packaging;

public static class PackageFormatDetector
{
    public static NativePackageKind Detect(string osReleasePath = "/etc/os-release")
    {
        if (File.Exists(osReleasePath))
        {
            var values = ParseOsRelease(File.ReadAllLines(osReleasePath));
            var tokens = new[] { values.GetValueOrDefault("ID") }
                .Concat((values.GetValueOrDefault("ID_LIKE") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(token => !string.IsNullOrWhiteSpace(token));

            if (TryFromTokens(tokens, out var kind))
            {
                return kind;
            }
        }

        if (CommandExists("dpkg-deb"))
        {
            return NativePackageKind.Deb;
        }

        if (CommandExists("rpmbuild"))
        {
            return NativePackageKind.Rpm;
        }

        if (CommandExists("makepkg"))
        {
            return NativePackageKind.Pacman;
        }

        throw new InvalidOperationException("Could not detect a supported package format.");
    }

    public static bool TryFromTokens(IEnumerable<string?> tokens, out NativePackageKind kind)
    {
        foreach (var token in tokens)
        {
            switch (token?.Trim().ToLowerInvariant())
            {
                case "arch":
                case "archlinux":
                case "cachyos":
                case "manjaro":
                case "endeavouros":
                    kind = NativePackageKind.Pacman;
                    return true;
                case "fedora":
                case "rhel":
                case "centos":
                case "rocky":
                case "almalinux":
                case "suse":
                case "opensuse":
                    kind = NativePackageKind.Rpm;
                    return true;
                case "debian":
                case "ubuntu":
                case "linuxmint":
                case "pop":
                case "elementary":
                    kind = NativePackageKind.Deb;
                    return true;
            }
        }

        kind = default;
        return false;
    }

    private static Dictionary<string, string> ParseOsRelease(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');

            if (separator <= 0)
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            values[line[..separator]] = value;
        }

        return values;
    }

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }
}
