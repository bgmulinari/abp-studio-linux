namespace AbpStudioLinux.Installer.Tests.Installation;

public sealed class PrerequisiteCheckerTests
{
    [Fact]
    public void ParsesDotNetSdkVersionsFromListSdksOutput()
    {
        var versions = PrerequisiteChecker.ParseDotNetSdkVersions(
            """
            9.0.305 [/usr/share/dotnet/sdk]
            10.0.203 [/usr/share/dotnet/sdk]
            """);

        Assert.Contains(new Version(10, 0, 203), versions);
    }

    [Fact]
    public void ParsesDotNetRuntimeVersionsFromListRuntimesOutput()
    {
        var versions = PrerequisiteChecker.ParseDotNetRuntimeVersions(
            """
            Microsoft.AspNetCore.App 10.0.3 [/fake/dotnet/shared/Microsoft.AspNetCore.App]
            Microsoft.NETCore.App 10.0.8 [/fake/dotnet/shared/Microsoft.NETCore.App]
            """);

        Assert.Contains(new DotNetRuntimeVersion("Microsoft.NETCore.App", new Version(10, 0, 8)), versions);
    }

    [Theory]
    [InlineData("10.0.3", "10.0.8", false)]
    [InlineData("10.0.8", "10.0.8", true)]
    [InlineData("10.0.9", "10.0.8", true)]
    [InlineData("10.1.0", "10.0.8", false)]
    public void ChecksRuntimePatchCompatibility(string installedVersion, string requiredVersion, bool expected)
    {
        var installed = new DotNetRuntimeVersion("Microsoft.NETCore.App", Version.Parse(installedVersion));
        var required = new DotNetRuntimeRequirement("Microsoft.NETCore.App", Version.Parse(requiredVersion));

        Assert.Equal(expected, installed.Satisfies(required));
    }

    [Fact]
    public void VerifyAppDotNetRuntimesRejectsTooOldRuntimeFromIncludedFrameworks()
    {
        var root = TestPaths.CreateTempDirectory();
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(
            Path.Combine(app, "Volo.Abp.Studio.UI.Host.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "includedFrameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.8"
                  },
                  {
                    "name": "Microsoft.AspNetCore.App",
                    "version": "10.0.8"
                  }
                ]
              }
            }
            """);

        var dotnet = WriteFakeDotNet(root,
            """
            Microsoft.NETCore.App 10.0.3 [/fake/dotnet/shared/Microsoft.NETCore.App]
            Microsoft.AspNetCore.App 10.0.3 [/fake/dotnet/shared/Microsoft.AspNetCore.App]
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => PrerequisiteChecker.VerifyAppDotNetRuntimes(app, dotnet, TextWriter.Null));

        Assert.Contains("Microsoft.NETCore.App 10.0.8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.App 10.0.8", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.NETCore.App 10.0.3", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v20.11.0", true)]
    [InlineData("v22.11.0", true)]
    [InlineData("v24.15.0", true)]
    [InlineData("v20.10.9", false)]
    public void ChecksSupportedNodeVersion(string nodeOutput, bool expected)
    {
        var version = PrerequisiteChecker.ParseNodeVersion(nodeOutput);

        Assert.Equal(expected, PrerequisiteChecker.IsNodeVersionSupported(version));
    }

    [Fact]
    public void InstallOrUpdateAbpCliPassesRequestedToolVersion()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = TestPaths.CreateTempDirectory();
        var log = Path.Combine(root, "dotnet-args.log");
        var dotnet = Path.Combine(root, "dotnet");
        File.WriteAllText(
            dotnet,
            $"""
             #!/bin/sh
             printf '%s\n' "$@" > '{log}'
             exit 0
             """);
        MakeExecutable(dotnet);

        PrerequisiteChecker.InstallOrUpdateAbpCli(dotnet, "3.0.2", TextWriter.Null, TextWriter.Null);

        Assert.Equal(
            new[] { "tool", "update", "-g", "Volo.Abp.Studio.Cli", "--version", "3.0.2", "--allow-downgrade" },
            File.ReadAllLines(log));
    }

    private static string WriteFakeDotNet(string root, string runtimeOutput)
    {
        var dotnet = Path.Combine(root, "dotnet");
        File.WriteAllText(
            dotnet,
            $"""
             #!/bin/sh
             if [ "$1" = "--list-runtimes" ]; then
               cat <<'EOF'
             {runtimeOutput}
             EOF
               exit 0
             fi
             exit 1
             """);

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(dotnet);
        }

        return dotnet;
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
}
