using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AbpStudioLinux.Installer.Installation;

public static partial class PrerequisiteChecker
{
    public const int RequiredDotNetSdkMajor = 10;
    public static readonly Version RequiredNodeVersion = new(20, 11, 0);

    public static void VerifyUserManagedPrerequisites(string dotNetCommand, string nodeCommand, TextWriter stderr)
    {
        var sdks = GetDotNetSdkVersions(dotNetCommand);
        var selectedSdk = sdks
            .Where(version => version.Major == RequiredDotNetSdkMajor)
            .OrderDescending()
            .FirstOrDefault();

        if (selectedSdk is null)
        {
            var installed = sdks.Count == 0 ? "none" : string.Join(", ", sdks);
            throw new InvalidOperationException($".NET SDK {RequiredDotNetSdkMajor}.x is required. Installed SDKs: {installed}. Install it manually, then re-run the installer.");
        }

        var nodeVersion = GetNodeVersion(nodeCommand);

        if (!IsNodeVersionSupported(nodeVersion))
        {
            throw new InvalidOperationException($"Node.js {RequiredNodeVersion} or newer is required. Found {nodeVersion}.");
        }

        stderr.WriteLine($"[INFO] Verified .NET SDK: {selectedSdk}");
        stderr.WriteLine($"[INFO] Verified Node.js: {nodeVersion}");
    }

    public static void VerifyAppDotNetRuntimes(string appDirectory, string dotNetCommand, TextWriter stderr)
    {
        var requirements = GetAppRuntimeRequirements(appDirectory);

        if (requirements.Count == 0)
        {
            return;
        }

        var installedRuntimes = GetDotNetRuntimeVersions(dotNetCommand);
        var missing = requirements
            .Where(requirement => !installedRuntimes.Any(runtime => runtime.Satisfies(requirement)))
            .ToArray();

        if (missing.Length > 0)
        {
            var required = string.Join(", ", missing.Select(requirement => $"{requirement.Name} {requirement.Version}"));
            var installed = installedRuntimes.Count == 0
                ? "none"
                : string.Join(", ", installedRuntimes.Select(runtime => $"{runtime.Name} {runtime.Version}"));
            throw new InvalidOperationException($"The ABP Studio package requires .NET runtime {required}. Installed runtimes: {installed}. Install or update .NET, then re-run the installer.");
        }

        stderr.WriteLine($"[INFO] Verified .NET runtimes: {string.Join(", ", requirements.Select(requirement => $"{requirement.Name} {requirement.Version}"))}");
    }

    public static bool IsNodeVersionSupported(Version version) => version >= RequiredNodeVersion;

    public static void InstallOrUpdateAbpCli(
        string dotNetCommand,
        string? requestedVersion,
        TextWriter stdout,
        TextWriter stderr)
    {
        var arguments = new List<string> { "tool", "update", "-g", "Volo.Abp.Studio.Cli" };

        if (requestedVersion is not null)
        {
            arguments.Add("--version");
            arguments.Add(requestedVersion);
            arguments.Add("--allow-downgrade");
        }

        stderr.WriteLine(requestedVersion is null
            ? "[INFO] Installing/updating ABP CLI"
            : $"[INFO] Installing/updating ABP CLI {requestedVersion}");
        var exitCode = ProcessRunner.Run(
            new ShellCommand(dotNetCommand, arguments),
            stdout,
            stderr);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(requestedVersion is null
                ? "dotnet tool update -g Volo.Abp.Studio.Cli failed."
                : $"dotnet tool update -g Volo.Abp.Studio.Cli --version {requestedVersion} --allow-downgrade failed.");
        }
    }

    public static IReadOnlyList<Version> ParseDotNetSdkVersions(string output)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(ParseVersion)
            .OfType<Version>()
            .ToArray();
    }

    public static IReadOnlyList<DotNetRuntimeVersion> ParseDotNetRuntimeVersions(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseRuntimeLine)
            .OfType<DotNetRuntimeVersion>()
            .ToArray();

    public static Version ParseNodeVersion(string output) =>
        ParseVersion(output.Trim().TrimStart('v'))
        ?? throw new InvalidOperationException($"Could not parse Node.js version from: {output.Trim()}");

    private static IReadOnlyList<Version> GetDotNetSdkVersions(string dotNetCommand)
    {
        var result = Capture(dotNetCommand, "--list-sdks");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($".NET SDK {RequiredDotNetSdkMajor}.x is required, but `{dotNetCommand} --list-sdks` failed: {result.Error.Trim()}");
        }

        return ParseDotNetSdkVersions(result.Output);
    }

    private static IReadOnlyList<DotNetRuntimeVersion> GetDotNetRuntimeVersions(string dotNetCommand)
    {
        var result = Capture(dotNetCommand, "--list-runtimes");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not verify required .NET runtimes because `{dotNetCommand} --list-runtimes` failed: {result.Error.Trim()}");
        }

        return ParseDotNetRuntimeVersions(result.Output);
    }

    public static IReadOnlyList<DotNetRuntimeRequirement> GetAppRuntimeRequirements(string appDirectory)
    {
        return Directory
            .EnumerateFiles(appDirectory, "*.runtimeconfig.json", SearchOption.AllDirectories)
            .Where(runtimeConfig => !UsesAppLocalHostPolicy(runtimeConfig))
            .SelectMany(ReadRuntimeRequirements)
            .GroupBy(requirement => requirement.Name, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(requirement => requirement.Version).First())
            .OrderBy(requirement => requirement.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<DotNetRuntimeRequirement> ReadRuntimeRequirements(string runtimeConfig)
    {
        var root = JsonNode.Parse(File.ReadAllText(runtimeConfig))?.AsObject();
        var runtimeOptions = root?["runtimeOptions"] as JsonObject;

        if (runtimeOptions is null)
        {
            yield break;
        }

        if (runtimeOptions["framework"] is JsonObject framework
            && TryReadRuntimeRequirement(framework, out var singleRequirement))
        {
            yield return singleRequirement;
        }

        foreach (var requirement in ReadRuntimeRequirementArray(runtimeOptions["frameworks"] as JsonArray))
        {
            yield return requirement;
        }

        foreach (var requirement in ReadRuntimeRequirementArray(runtimeOptions["includedFrameworks"] as JsonArray))
        {
            yield return requirement;
        }
    }

    private static IEnumerable<DotNetRuntimeRequirement> ReadRuntimeRequirementArray(JsonArray? frameworks)
    {
        if (frameworks is null)
        {
            yield break;
        }

        foreach (var item in frameworks.OfType<JsonObject>())
        {
            if (TryReadRuntimeRequirement(item, out var requirement))
            {
                yield return requirement;
            }
        }
    }

    private static bool TryReadRuntimeRequirement(JsonObject framework, out DotNetRuntimeRequirement requirement)
    {
        if (framework["name"]?.GetValue<string>() is { Length: > 0 } name
            && framework["version"]?.GetValue<string>() is { Length: > 0 } versionValue
            && ParseVersion(versionValue) is { } version)
        {
            requirement = new DotNetRuntimeRequirement(name, version);
            return true;
        }

        requirement = default;
        return false;
    }

    private static bool UsesAppLocalHostPolicy(string runtimeConfig)
    {
        var directory = Path.GetDirectoryName(runtimeConfig);
        return directory is not null && File.Exists(Path.Combine(directory, "libhostpolicy.so"));
    }

    private static Version GetNodeVersion(string nodeCommand)
    {
        var result = Capture(nodeCommand, "--version");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Node.js {RequiredNodeVersion} or newer is required, but `{nodeCommand} --version` failed: {result.Error.Trim()}");
        }

        return ParseNodeVersion(result.Output);
    }

    private static Version? ParseVersion(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = SemVerPrefixRegex().Match(value);
        return match.Success
            ? new Version(
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value))
            : null;
    }

    private static DotNetRuntimeVersion? ParseRuntimeLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is [{ Length: > 0 } name, { Length: > 0 } versionValue, ..]
               && ParseVersion(versionValue) is { } version
            ? new DotNetRuntimeVersion(name, version)
            : null;
    }

    private static CaptureResult Capture(string fileName, string argument)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Required command not found: {fileName}", ex);
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CaptureResult(process.ExitCode, output, error);
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)")]
    private static partial Regex SemVerPrefixRegex();

    private sealed record CaptureResult(
        int ExitCode,
        string Output,
        string Error);
}

public readonly record struct DotNetRuntimeRequirement(
    string Name,
    Version Version);

public readonly record struct DotNetRuntimeVersion(
    string Name,
    Version Version)
{
    public bool Satisfies(DotNetRuntimeRequirement requirement) =>
        string.Equals(Name, requirement.Name, StringComparison.Ordinal)
        && Version.Major == requirement.Version.Major
        && Version.Minor == requirement.Version.Minor
        && Version >= requirement.Version;
}
