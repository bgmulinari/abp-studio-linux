using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AbpStudioLinux.Installer.Upstream;

public static class UpstreamResolver
{
    private const string PackageId = "Volo.Abp.Studio.Cli";
    private const string DownloadBaseUrl = "https://abp.io/api/abp-studio/download/r/osx-intel/";

    public static string ResolveInstalledCliVersion(string dotNetCommand)
    {
        var result = Capture(dotNetCommand, ["tool", "list", "-g", PackageId, "--format", "json"]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet tool list -g {PackageId} failed with exit code {result.ExitCode}: {result.Error.Trim()}");
        }

        return ResolveInstalledCliVersionFromJson(result.Output);
    }

    public static string ResolveLatestVersion(string dotNetCommand) => ResolveInstalledCliVersion(dotNetCommand);

    public static string ResolveInstalledCliVersionFromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"dotnet tool list did not return an installed {PackageId} version.");
        }

        foreach (var tool in data.EnumerateArray())
        {
            if (tool.TryGetProperty("packageId", out var packageId)
                && tool.TryGetProperty("version", out var version)
                && string.Equals(packageId.GetString(), PackageId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(version.GetString()))
            {
                return version.GetString()!;
            }
        }

        throw new InvalidOperationException($"dotnet tool list did not return an installed {PackageId} version.");
    }

    public static string ResolveLatestVersionFromJson(string json) => ResolveInstalledCliVersionFromJson(json);

    public static Uri CreateStableFullPackageUri(string version) =>
        string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Version is required.", nameof(version))
            : new Uri($"{DownloadBaseUrl}abp-studio-{Uri.EscapeDataString(version)}-stable-full.nupkg");

    private static CaptureResult Capture(string fileName, IReadOnlyList<string> arguments)
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
            throw new InvalidOperationException($"Required command not found: {fileName}", ex);
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new CaptureResult(process.ExitCode, output, error);
    }

    private sealed record CaptureResult(
        int ExitCode,
        string Output,
        string Error);
}
