namespace AbpStudioLinux.Installer.Tests.Packaging;

public sealed class PackageFormatDetectorTests
{
    [Theory]
    [InlineData("fedora", NativePackageKind.Rpm)]
    [InlineData("rhel", NativePackageKind.Rpm)]
    [InlineData("ubuntu", NativePackageKind.Deb)]
    [InlineData("debian", NativePackageKind.Deb)]
    [InlineData("arch", NativePackageKind.Pacman)]
    [InlineData("manjaro", NativePackageKind.Pacman)]
    public void DetectsPackageKindFromOsReleaseTokens(string token, NativePackageKind expected)
    {
        var detected = PackageFormatDetector.TryFromTokens(new[] { token }, out var kind);

        Assert.True(detected);
        Assert.Equal(expected, kind);
    }
}
