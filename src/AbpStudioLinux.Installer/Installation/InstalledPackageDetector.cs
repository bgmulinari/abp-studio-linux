using System.ComponentModel;
using System.Diagnostics;

namespace AbpStudioLinux.Installer.Installation;

public static class InstalledPackageDetector
{
    private const string PackageName = "abp-studio";

    public static bool IsVersionInstalled(NativePackageKind kind, string expectedVersion, out string? installedVersion)
    {
        installedVersion = GetInstalledVersion(kind);
        return string.Equals(installedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetInstalledVersion(NativePackageKind kind)
    {
        var version = kind switch
        {
            NativePackageKind.Deb => CaptureDebVersion(),
            NativePackageKind.Rpm => CaptureVersion("rpm", new[] { "-q", "--qf", "%{VERSION}", PackageName }),
            NativePackageKind.Pacman => CapturePacmanVersion(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        return string.IsNullOrWhiteSpace(version)
            ? null
            : NormalizeInstalledVersion(kind, version);
    }

    internal static string NormalizeInstalledVersion(NativePackageKind kind, string version)
    {
        var normalized = version.Trim();

        if (normalized.Length == 0)
        {
            return normalized;
        }

        var epochSeparator = normalized.IndexOf(':');

        if (epochSeparator >= 0 && epochSeparator < normalized.Length - 1)
        {
            normalized = normalized[(epochSeparator + 1)..];
        }

        return kind switch
        {
            NativePackageKind.Deb => StripSuffix(normalized, '-'),
            NativePackageKind.Rpm => normalized,
            NativePackageKind.Pacman => StripSuffix(normalized, '-'),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string StripSuffix(string value, char separator)
    {
        var index = value.LastIndexOf(separator);
        return index > 0 ? value[..index] : value;
    }

    private static string? CaptureDebVersion()
    {
        var output = CaptureVersion("dpkg-query", new[] { "-W", "-f=${Status}\t${Version}", PackageName });
        return ParseDebVersion(output);
    }

    internal static string? ParseDebVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var parts = output.Split('\t', 2, StringSplitOptions.TrimEntries);
        return parts is ["install ok installed", { Length: > 0 } version]
            ? version
            : null;
    }

    private static string? CapturePacmanVersion()
    {
        var output = CaptureVersion("pacman", new[] { "-Q", PackageName });

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static string? CaptureVersion(string fileName, IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : null;
    }
}
