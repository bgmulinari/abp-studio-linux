namespace AbpStudioLinux.Installer.App;

public sealed record BuildAppOptions(
    string PackagePath,
    string OutputDirectory,
    string WorkDirectory,
    string? NativeOverridesDirectory,
    string? RuntimeRootDirectory,
    string? FixturePayloadDirectory);

public sealed record BuildAppResult(
    string OutputDirectory,
    int RemovedFileCount,
    string AppVersion);

public static class BuildAppCommand
{
    private static readonly byte[][] MachOMagicNumbers =
    {
        new byte[] { 0xfe, 0xed, 0xfa, 0xcf },
        new byte[] { 0xcf, 0xfa, 0xed, 0xfe },
        new byte[] { 0xfe, 0xed, 0xfa, 0xce },
        new byte[] { 0xce, 0xfa, 0xed, 0xfe },
        new byte[] { 0xca, 0xfe, 0xba, 0xbe },
        new byte[] { 0xbe, 0xba, 0xfe, 0xca }
    };

    public static BuildAppResult Execute(BuildAppOptions options)
    {
        if (options.FixturePayloadDirectory is null && !File.Exists(options.PackagePath))
        {
            throw new FileNotFoundException("Package file does not exist.", options.PackagePath);
        }

        ResetDirectory(options.OutputDirectory);
        Directory.CreateDirectory(options.WorkDirectory);

        var macOsDirectory = options.FixturePayloadDirectory is not null
            ? options.FixturePayloadDirectory
            : ExtractPackagePayload(options.PackagePath, options.WorkDirectory);

        CopyDirectory(macOsDirectory, options.OutputDirectory);

        var removedCount = CleanMacOsArtifacts(options.OutputDirectory);

        if (options.NativeOverridesDirectory is not null)
        {
            CopyDirectory(options.NativeOverridesDirectory, options.OutputDirectory, true);
            removedCount += CleanMacOsArtifacts(options.OutputDirectory);
        }

        RuntimeConfigPatcher.PatchRuntimeConfigs(options.OutputDirectory);

        if (options.RuntimeRootDirectory is not null)
        {
            var appLocalDotNetRoot = Path.Combine(options.OutputDirectory, "dotnet");
            CopyDirectory(options.RuntimeRootDirectory, appLocalDotNetRoot, true);
            MakeExecutableIfExists(Path.Combine(appLocalDotNetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
            removedCount += CleanOptionalDotNetDiagnosticArtifacts(appLocalDotNetRoot);
        }

        MakeKnownExecutableHelpers(options.OutputDirectory);
        WriteLauncher(options.OutputDirectory);
        return new BuildAppResult(
            options.OutputDirectory,
            removedCount,
            AppVersionDetector.GetVersion(options.OutputDirectory));
    }

    internal static string ExtractPackagePayload(string packagePath, string workDirectory)
    {
        var expandedPackageDirectory = Path.Combine(workDirectory, "expanded-pkg");
        var payloadDirectory = Path.Combine(workDirectory, "payload");
        ResetDirectory(expandedPackageDirectory);
        ResetDirectory(payloadDirectory);

        ProcessRunner.Run(
            new ShellCommand("bsdtar", new[] { "-xf", packagePath, "-C", expandedPackageDirectory }),
            TextWriter.Null,
            Console.Error);

        var directMacOsDirectory = FindExtractedMacOsDirectory(expandedPackageDirectory);

        if (directMacOsDirectory is not null)
        {
            return directMacOsDirectory;
        }

        var payloadPath = FindExtractedPayloadPath(expandedPackageDirectory);

        ProcessRunner.Run(
            new ShellCommand("bsdtar", new[] { "-xf", payloadPath, "-C", payloadDirectory }),
            TextWriter.Null,
            Console.Error);

        var macOsDirectory = FindExtractedMacOsDirectory(payloadDirectory);
        return macOsDirectory ?? throw new DirectoryNotFoundException("Could not find ABP Studio.app/Contents/MacOS in payload.");
    }

    internal static string? FindExtractedMacOsDirectory(string rootDirectory)
    {
        return Directory
            .EnumerateDirectories(rootDirectory, "MacOS", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.EndsWith(Path.Combine("Contents", "MacOS"), StringComparison.Ordinal));
    }

    internal static string FindExtractedPayloadPath(string expandedPackageDirectory)
    {
        var payloadPath = Directory
            .EnumerateFiles(expandedPackageDirectory, "Payload", SearchOption.AllDirectories)
            .OrderBy(GetPayloadPathPreference)
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return payloadPath
               ?? throw new FileNotFoundException(
                   "Expanded package did not contain a Payload file.",
                   Path.Combine(expandedPackageDirectory, "1.pkg", "Payload"));
    }

    private static int GetPayloadPathPreference(string path)
    {
        if (path.EndsWith(Path.Combine("1.pkg", "Payload"), StringComparison.Ordinal))
        {
            return 0;
        }

        if (path.EndsWith(Path.Combine("Payload1.pkg", "Payload"), StringComparison.Ordinal))
        {
            return 1;
        }

        return 2;
    }

    public static int CleanMacOsArtifacts(string appDirectory)
    {
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(appDirectory, "*", SearchOption.AllDirectories).ToArray())
        {
            var fileName = Path.GetFileName(file);

            if (fileName.StartsWith("._", StringComparison.Ordinal)
                || fileName.Equals("UpdateMac", StringComparison.Ordinal)
                || IsOptionalDotNetDiagnosticArtifact(fileName)
                || fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                || IsMachO(file))
            {
                File.Delete(file);
                removed++;
            }
        }

        return removed;
    }

    private static bool IsOptionalDotNetDiagnosticArtifact(string fileName) =>
        fileName is "createdump"
            or "libcoreclrtraceptprovider.so"
            or "libmscordaccore.so"
            or "libmscordbi.so";

    private static int CleanOptionalDotNetDiagnosticArtifacts(string directory)
    {
        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray())
        {
            if (IsOptionalDotNetDiagnosticArtifact(Path.GetFileName(file)))
            {
                File.Delete(file);
                removed++;
            }
        }

        return removed;
    }

    private static bool IsMachO(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var file = File.OpenRead(path);

        if (file.Read(header) != header.Length)
        {
            return false;
        }

        foreach (var magic in MachOMagicNumbers)
        {
            if (header.SequenceEqual(magic))
            {
                return true;
            }
        }

        return false;
    }

    private static void MakeKnownExecutableHelpers(string appDirectory)
    {
        MakeExecutableIfExists(Path.Combine(appDirectory, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess"));
    }

    private static void MakeExecutableIfExists(string path)
    {
        if (File.Exists(path))
        {
            MakeExecutable(path);
        }
    }

    private static void WriteLauncher(string appDirectory)
    {
        var launcher = Path.Combine(appDirectory, "start.sh");
        File.WriteAllText(
            launcher,
            """
            #!/bin/bash
            set -euo pipefail

            APP_DIR="$(cd "$(dirname "$0")" && pwd)"
            dotnet_bin=""
            user_home="${HOME:-}"
            if [ -z "$user_home" ] && command -v getent >/dev/null 2>&1 && command -v id >/dev/null 2>&1; then
              user_home="$(getent passwd "$(id -u)" | cut -d: -f6 || true)"
            fi

            has_dotnet_10_sdk() {
              "$1" --list-sdks 2>/dev/null | awk '{ print $1 }' | grep -Eq '^10\.'
            }

            try_dotnet() {
              local candidate="$1"
              if [ -x "$candidate" ] && has_dotnet_10_sdk "$candidate"; then
                dotnet_bin="$candidate"
                return 0
              fi
              return 1
            }

            if [ -n "${ABP_STUDIO_DOTNET:-}" ]; then
              try_dotnet "$ABP_STUDIO_DOTNET" || true
            fi
            if [ -z "$dotnet_bin" ]; then
              try_dotnet "$APP_DIR/dotnet/dotnet" || true
            fi
            if [ -z "$dotnet_bin" ] && [ -n "${DOTNET_ROOT:-}" ]; then
              try_dotnet "$DOTNET_ROOT/dotnet" || true
            fi
            if [ -z "$dotnet_bin" ] && command -v dotnet >/dev/null 2>&1; then
              try_dotnet "$(command -v dotnet)" || true
            fi
            if [ -z "$dotnet_bin" ] && [ -n "$user_home" ]; then
              try_dotnet "$user_home/.dotnet/dotnet" || true
            fi
            if [ -z "$dotnet_bin" ]; then
              try_dotnet /usr/share/dotnet/dotnet || true
            fi
            if [ -z "$dotnet_bin" ]; then
              echo ".NET SDK 10.x is required to run ABP Studio." >&2
              exit 127
            fi

            dotnet_dir="$(dirname "$dotnet_bin")"
            case ":${PATH:-}:" in
              *":$dotnet_dir:"*) ;;
              *) export PATH="$dotnet_dir:${PATH:-}" ;;
            esac
            if [ -n "$user_home" ] && [ -d "$user_home/.dotnet/tools" ]; then
              case ":$PATH:" in
                *":$user_home/.dotnet/tools:"*) ;;
                *) export PATH="$user_home/.dotnet/tools:$PATH" ;;
              esac
            fi
            if [ -z "${DOTNET_ROOT:-}" ]; then
              if [ "$dotnet_bin" = "$APP_DIR/dotnet/dotnet" ] || [ "$(basename "$dotnet_dir")" = ".dotnet" ]; then
                export DOTNET_ROOT="$dotnet_dir"
              fi
            fi
            export DOTNET_HOST_PATH="$dotnet_bin"
            export DOTNET_ReadyToRun=0
            exec "$dotnet_bin" "$APP_DIR/Volo.Abp.Studio.UI.Host.dll" "$@"
            """);
        MakeExecutable(launcher);
    }

    internal static void CopyDirectory(string source, string target, bool overwrite = false)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }

        Directory.CreateDirectory(target);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite);
            PreserveUnixFileMode(file, destination);
        }
    }

    private static void PreserveUnixFileMode(string source, string destination)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
    }

    internal static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }

        Directory.CreateDirectory(path);
    }

    internal static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
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
    }
}
