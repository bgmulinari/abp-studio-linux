using AbpStudioLinux.Installer.Cli;

namespace AbpStudioLinux.Installer.Tests.Installation;

public sealed class InstallOptionsTests
{
    [Fact]
    public void FromParsesRequestedVersion()
    {
        var options = InstallOptions.From(new OptionReader(new[] { "--version", "3.0.2" }));

        Assert.Equal("3.0.2", options.RequestedVersion);
    }

    [Fact]
    public void FromTrimsRequestedVersion()
    {
        var options = InstallOptions.From(new OptionReader(new[] { "--version", " 3.0.2 " }));

        Assert.Equal("3.0.2", options.RequestedVersion);
    }
}
