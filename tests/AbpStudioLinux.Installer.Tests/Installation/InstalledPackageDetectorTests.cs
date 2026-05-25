using System.Reflection;

namespace AbpStudioLinux.Installer.Tests.Installation;

public sealed class InstalledPackageDetectorTests
{
    [Theory]
    [InlineData(NativePackageKind.Deb, "1:3.0.2-1", "3.0.2")]
    [InlineData(NativePackageKind.Rpm, "3.0.2", "3.0.2")]
    [InlineData(NativePackageKind.Pacman, "3.0.2-1", "3.0.2")]
    [InlineData(NativePackageKind.Pacman, "3.0.2-preview.1-1", "3.0.2-preview.1")]
    public void NormalizeInstalledVersionRemovesPackageManagerRevision(
        NativePackageKind kind,
        string installedVersion,
        string expectedVersion)
    {
        var method = typeof(InstalledPackageDetector).GetMethod(
            "NormalizeInstalledVersion",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var normalized = method.Invoke(null, new object[] { kind, installedVersion });

        Assert.Equal(expectedVersion, normalized);
    }

    [Theory]
    [InlineData("install ok installed\t3.0.3", "3.0.3")]
    [InlineData("deinstall ok config-files\t3.0.3", null)]
    [InlineData("unknown ok not-installed\t", null)]
    public void ParseDebVersionRequiresInstalledStatus(string output, string? expectedVersion)
    {
        var method = typeof(InstalledPackageDetector).GetMethod(
            "ParseDebVersion",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var version = method.Invoke(null, new object[] { output });

        Assert.Equal(expectedVersion, version);
    }
}
