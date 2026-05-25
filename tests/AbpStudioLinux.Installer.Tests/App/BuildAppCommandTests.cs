using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;

namespace AbpStudioLinux.Installer.Tests.App;

public sealed class BuildAppCommandTests
{
    [Fact]
    public void FindExtractedPayloadPathSupportsDebianLibarchiveNestedLayout()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "1.pkg", "Payload1.pkg", "Payload");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        File.WriteAllText(payload, "payload");

        var method = typeof(BuildAppCommand).GetMethod(
            "FindExtractedPayloadPath",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(payload, method.Invoke(null, new object[] { root }));
    }

    [Fact]
    public void FindExtractedMacOsDirectorySupportsNuGetZipLayout()
    {
        var root = TestPaths.CreateTempDirectory();
        var macOsDirectory = Path.Combine(root,
            "lib",
            "app",
            "Contents",
            "MacOS");
        Directory.CreateDirectory(macOsDirectory);

        var method = typeof(BuildAppCommand).GetMethod(
            "FindExtractedMacOsDirectory",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var result = method.Invoke(null, new object[] { root });

        Assert.Equal(macOsDirectory, result);
    }

    [Fact]
    public void FixtureBuildCleansMacArtifactsAndWritesLinuxLauncher()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var output = Path.Combine(root, "app");
        var nativeOverrides = Path.Combine(root, "native");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(nativeOverrides);
        File.WriteAllText(Path.Combine(payload, "._metadata"), "mac metadata");
        File.WriteAllText(Path.Combine(payload, "libold.dylib"), "mac native");
        File.WriteAllText(Path.Combine(payload, "UpdateMac"), "mac updater");
        File.WriteAllBytes(Path.Combine(payload, "Volo.Abp.Studio.UI.Host"), new byte[] { 0xcf, 0xfa, 0xed, 0xfe, 0, 0 });
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "includedFrameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  },
                  {
                    "name": "Microsoft.AspNetCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);
        File.WriteAllText(Path.Combine(nativeOverrides, "libcef.so"), "linux native");
        Directory.CreateDirectory(Path.Combine(nativeOverrides, "CefGlueBrowserProcess"));
        File.WriteAllText(Path.Combine(nativeOverrides, "CefGlueBrowserProcess", "libcoreclrtraceptprovider.so"), "optional tracing provider");
        File.WriteAllText(Path.Combine(nativeOverrides, "CefGlueBrowserProcess", "libmscordaccore.so"), "optional diagnostics");
        File.WriteAllText(Path.Combine(nativeOverrides, "CefGlueBrowserProcess", "libmscordbi.so"), "optional diagnostics");
        File.WriteAllText(Path.Combine(nativeOverrides, "CefGlueBrowserProcess", "createdump"), "optional diagnostics");

        var result = BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            nativeOverrides,
            null,
            payload));

        Assert.Equal(output, result.OutputDirectory);
        Assert.True(result.RemovedFileCount >= 4);
        Assert.False(File.Exists(Path.Combine(output, "._metadata")));
        Assert.False(File.Exists(Path.Combine(output, "libold.dylib")));
        Assert.False(File.Exists(Path.Combine(output, "UpdateMac")));
        Assert.False(File.Exists(Path.Combine(output, "Volo.Abp.Studio.UI.Host")));
        Assert.True(File.Exists(Path.Combine(output, "Volo.Abp.Studio.UI.Host.dll")));
        Assert.Equal(BuildAppTestFixtures.ExpectedTestAssemblyVersion, result.AppVersion);
        Assert.True(File.Exists(Path.Combine(output, "libcef.so")));
        Assert.False(File.Exists(Path.Combine(output, "CefGlueBrowserProcess", "libcoreclrtraceptprovider.so")));
        Assert.False(File.Exists(Path.Combine(output, "CefGlueBrowserProcess", "libmscordaccore.so")));
        Assert.False(File.Exists(Path.Combine(output, "CefGlueBrowserProcess", "libmscordbi.so")));
        Assert.False(File.Exists(Path.Combine(output, "CefGlueBrowserProcess", "createdump")));
        Assert.True(File.Exists(Path.Combine(output, "start.sh")));
        var launcher = File.ReadAllText(Path.Combine(output, "start.sh"));
        Assert.Contains("$user_home/.dotnet/dotnet", launcher, StringComparison.Ordinal);
        Assert.Contains("export PATH=", launcher, StringComparison.Ordinal);
        Assert.Contains("DOTNET_HOST_PATH", launcher, StringComparison.Ordinal);
        Assert.Contains("/usr/share/dotnet/dotnet", launcher, StringComparison.Ordinal);
        Assert.Contains("has_dotnet_10_sdk", launcher, StringComparison.Ordinal);
        Assert.Contains(".NET SDK 10.x is required", launcher, StringComparison.Ordinal);

        var runtimeConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "Volo.Abp.Studio.UI.Host.runtimeconfig.json")))!;
        Assert.Null(runtimeConfig["runtimeOptions"]?["includedFrameworks"]);
        Assert.Equal("Microsoft.NETCore.App", runtimeConfig["runtimeOptions"]?["frameworks"]?[0]?["name"]?.GetValue<string>());
        Assert.Equal("10.0.7", runtimeConfig["runtimeOptions"]?["frameworks"]?[0]?["version"]?.GetValue<string>());
        Assert.Equal("Microsoft.AspNetCore.App", runtimeConfig["runtimeOptions"]?["frameworks"]?[1]?["name"]?.GetValue<string>());
        Assert.Equal("10.0.7", runtimeConfig["runtimeOptions"]?["frameworks"]?[1]?["version"]?.GetValue<string>());
        Assert.Equal("LatestPatch", runtimeConfig["runtimeOptions"]?["rollForward"]?.GetValue<string>());
        Assert.False(runtimeConfig["runtimeOptions"]?["configProperties"]?["System.Reflection.Metadata.MetadataUpdater.IsSupported"]?.GetValue<bool>());
    }

    [Fact]
    public void BuildCopiesRuntimeRootAndPreservesExecutableModes()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var runtimeRoot = Path.Combine(root, "dotnet-root");
        var output = Path.Combine(root, "app");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "sdk", "10.0.203"));
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        var sdkRuntimeConfig = Path.Combine(runtimeRoot, "sdk", "10.0.203", "dotnet.runtimeconfig.json");
        File.WriteAllText(
            sdkRuntimeConfig,
            """
            {
              "runtimeOptions": {}
            }
            // SDK-owned file must not be parsed or patched by the app converter.
            """);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);

        var dotnet = Path.Combine(runtimeRoot, "dotnet");
        File.WriteAllText(dotnet, "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(runtimeRoot, "createdump"), "linux runtime diagnostic helper");
        File.WriteAllText(Path.Combine(runtimeRoot, "libcoreclrtraceptprovider.so"), "optional tracing provider");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                dotnet,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            null,
            runtimeRoot,
            payload));

        var bundledDotnet = Path.Combine(output, "dotnet", "dotnet");
        Assert.True(File.Exists(bundledDotnet));
        Assert.False(File.Exists(Path.Combine(output, "dotnet", "createdump")));
        Assert.False(File.Exists(Path.Combine(output, "dotnet", "libcoreclrtraceptprovider.so")));
        Assert.True(Directory.Exists(Path.Combine(output, "dotnet", "sdk", "10.0.203")));
        Assert.Equal(
            File.ReadAllText(sdkRuntimeConfig),
            File.ReadAllText(Path.Combine(output, "dotnet", "sdk", "10.0.203", "dotnet.runtimeconfig.json")));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True((File.GetUnixFileMode(bundledDotnet) & UnixFileMode.UserExecute) != 0);
            Assert.True((File.GetUnixFileMode(bundledDotnet) & UnixFileMode.GroupExecute) != 0);
            Assert.True((File.GetUnixFileMode(bundledDotnet) & UnixFileMode.OtherExecute) != 0);
        }
    }

    [Fact]
    public void BuildMakesCefGlueBrowserProcessExecutableForAllUsers()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var output = Path.Combine(root, "app");
        var nativeOverrides = Path.Combine(root, "native");
        var browserProcess = Path.Combine(nativeOverrides, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(Path.GetDirectoryName(browserProcess)!);
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);
        File.WriteAllText(browserProcess, "linux helper");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                browserProcess,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead);
        }

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            nativeOverrides,
            null,
            payload));

        var outputBrowserProcess = Path.Combine(output, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess");
        Assert.True(File.Exists(outputBrowserProcess));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute,
                File.GetUnixFileMode(outputBrowserProcess));
        }
    }

    [Fact]
    public void BuildPreservesSelfContainedCefGlueBrowserProcessRuntimeConfig()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var output = Path.Combine(root, "app");
        var nativeOverrides = Path.Combine(root, "native");
        var browserProcessDirectory = Path.Combine(nativeOverrides, "CefGlueBrowserProcess");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(browserProcessDirectory);
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "includedFrameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);
        File.WriteAllText(Path.Combine(browserProcessDirectory, "libhostpolicy.so"), "linux hostpolicy");
        File.WriteAllText(
            Path.Combine(browserProcessDirectory, "Xilium.CefGlue.BrowserProcess.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net8.0",
                "includedFrameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "8.0.22"
                  }
                ],
                "configProperties": {}
              }
            }
            """);

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            nativeOverrides,
            null,
            payload));

        var mainRuntimeConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "Volo.Abp.Studio.UI.Host.runtimeconfig.json")))!;
        Assert.Null(mainRuntimeConfig["runtimeOptions"]?["includedFrameworks"]);
        Assert.Equal("Microsoft.NETCore.App", mainRuntimeConfig["runtimeOptions"]?["frameworks"]?[0]?["name"]?.GetValue<string>());
        Assert.Equal("10.0.7", mainRuntimeConfig["runtimeOptions"]?["frameworks"]?[0]?["version"]?.GetValue<string>());

        var browserRuntimeConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "CefGlueBrowserProcess", "Xilium.CefGlue.BrowserProcess.runtimeconfig.json")))!;
        Assert.Null(browserRuntimeConfig["runtimeOptions"]?["frameworks"]);
        Assert.Equal("Microsoft.NETCore.App", browserRuntimeConfig["runtimeOptions"]?["includedFrameworks"]?[0]?["name"]?.GetValue<string>());
        Assert.Equal("8.0.22", browserRuntimeConfig["runtimeOptions"]?["includedFrameworks"]?[0]?["version"]?.GetValue<string>());
        Assert.Null(browserRuntimeConfig["runtimeOptions"]?["rollForward"]);
    }

    [Fact]
    public void LauncherPrefersBundledDotNetAndExportsDotNetRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var runtimeRoot = Path.Combine(root, "dotnet-root");
        var output = Path.Combine(root, "app");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(runtimeRoot);
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);

        var fakeDotnet = Path.Combine(runtimeRoot, "dotnet");
        File.WriteAllText(
            fakeDotnet,
            """
            #!/bin/sh
            if [ "$1" = "--list-sdks" ]; then
              printf '10.0.203 [/test/sdk]\n'
              exit 0
            fi
            printf 'DOTNET_ROOT=%s\n' "${DOTNET_ROOT:-}"
            printf 'DOTNET_HOST_PATH=%s\n' "${DOTNET_HOST_PATH:-}"
            printf 'ARGS=%s\n' "$*"
            """);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fakeDotnet,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            null,
            runtimeRoot,
            payload));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(output, "start.sh"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }.WithEnvironment(("HOME", Path.Combine(root, "home")),
            ("PATH", "/usr/bin:/bin"),
            ("DOTNET_ROOT", null),
            ("DOTNET_HOST_PATH", null),
            ("ABP_STUDIO_DOTNET", null));
        startInfo.ArgumentList.Add("--probe");

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        var bundledDotnet = Path.Combine(output, "dotnet", "dotnet");

        Assert.Equal(0, process.ExitCode);
        Assert.Contains($"DOTNET_ROOT={Path.Combine(output, "dotnet")}", stdout, StringComparison.Ordinal);
        Assert.Contains($"DOTNET_HOST_PATH={bundledDotnet}", stdout, StringComparison.Ordinal);
        Assert.Contains("Volo.Abp.Studio.UI.Host.dll --probe", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherExportsResolvedDotNetPathForChildProcesses()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var output = Path.Combine(root, "app");
        var home = Path.Combine(root, "home");
        var dotnetRoot = Path.Combine(home, ".dotnet");
        var dotnetTools = Path.Combine(dotnetRoot, "tools");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dotnetTools);
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);

        var fakeDotnet = Path.Combine(dotnetRoot, "dotnet");
        File.WriteAllText(
            fakeDotnet,
            """
            #!/bin/sh
            if [ "$1" = "--list-sdks" ]; then
              printf '10.0.203 [/test/sdk]\n'
              exit 0
            fi
            printf 'PATH=%s\n' "$PATH"
            printf 'DOTNET_ROOT=%s\n' "${DOTNET_ROOT:-}"
            printf 'DOTNET_HOST_PATH=%s\n' "${DOTNET_HOST_PATH:-}"
            printf 'ARGS=%s\n' "$*"
            """);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fakeDotnet,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            null,
            null,
            payload));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(output, "start.sh"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }.WithEnvironment(("HOME", home),
            ("PATH", "/usr/bin:/bin"),
            ("DOTNET_ROOT", null),
            ("DOTNET_HOST_PATH", null),
            ("ABP_STUDIO_DOTNET", null));
        startInfo.ArgumentList.Add("--probe");

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains($"PATH={dotnetTools}:{dotnetRoot}:/usr/bin:/bin", stdout, StringComparison.Ordinal);
        Assert.Contains($"DOTNET_ROOT={dotnetRoot}", stdout, StringComparison.Ordinal);
        Assert.Contains($"DOTNET_HOST_PATH={fakeDotnet}", stdout, StringComparison.Ordinal);
        Assert.Contains("Volo.Abp.Studio.UI.Host.dll --probe", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void LauncherRejectsDotNetWithoutRequiredSdk()
    {
        var root = TestPaths.CreateTempDirectory();
        var payload = Path.Combine(root, "payload");
        var output = Path.Combine(root, "app");
        var dotnetRoot = Path.Combine(root, "dotnet-runtime-only");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(dotnetRoot);
        BuildAppTestFixtures.CopyHostAssemblyFixture(payload);
        File.WriteAllText(
            Path.Combine(payload, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.7"
                  }
                ],
                "configProperties": {}
              }
            }
            """);

        var fakeDotnet = Path.Combine(dotnetRoot, "dotnet");
        File.WriteAllText(
            fakeDotnet,
            """
            #!/bin/sh
            if [ "$1" = "--list-sdks" ]; then
              printf '9.0.305 [/test/sdk]\n'
              exit 0
            fi
            exit 1
            """);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fakeDotnet,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        BuildAppCommand.Execute(new BuildAppOptions(
            "/not/used.pkg",
            output,
            Path.Combine(root, "work"),
            null,
            null,
            payload));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(output, "start.sh"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }.WithEnvironment(("HOME", Path.Combine(root, "home")),
            ("PATH", $"{dotnetRoot}:/usr/bin:/bin"),
            ("DOTNET_ROOT", null),
            ("DOTNET_HOST_PATH", null),
            ("ABP_STUDIO_DOTNET", null));

        using var process = Process.Start(startInfo)!;
        process.WaitForExit();
        process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.Equal(127, process.ExitCode);
        Assert.Contains(".NET SDK 10.x is required to run ABP Studio.", stderr, StringComparison.Ordinal);
    }
}

internal static class BuildAppTestFixtures
{
    public static string ExpectedTestAssemblyVersion
    {
        get
        {
            var version = typeof(BuildAppCommandTests).Assembly.GetName().Version!;
            return version.Revision > 0
                ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static void CopyHostAssemblyFixture(string directory)
    {
        File.Copy(
            typeof(BuildAppCommandTests).Assembly.Location,
            Path.Combine(directory, "Volo.Abp.Studio.UI.Host.dll"),
            true);
    }
}

internal static class ProcessStartInfoTestExtensions
{
    public static ProcessStartInfo WithEnvironment(this ProcessStartInfo startInfo, params (string Key, string? Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }
}
