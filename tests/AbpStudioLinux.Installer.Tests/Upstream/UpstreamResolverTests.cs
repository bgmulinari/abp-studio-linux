namespace AbpStudioLinux.Installer.Tests.Upstream;

public sealed class UpstreamResolverTests
{
    [Fact]
    public void ResolveLatestVersionFromJsonReturnsInstalledCliToolVersion()
    {
        const string json = """
                            {
                              "version": 1,
                              "data": [
                                {
                                  "packageId": "volo.abp.studio.cli",
                                  "version": "3.0.2",
                                  "commands": [
                                    "abp"
                                  ]
                                }
                              ]
                            }
                            """;

        var version = UpstreamResolver.ResolveLatestVersionFromJson(json);

        Assert.Equal("3.0.2", version);
    }

    [Fact]
    public void ResolveLatestVersionFromJsonReturnsCliToolVersionWhenClientPackageIsPresent()
    {
        const string json = """
                            {
                              "version": 1,
                              "data": [
                                {
                                  "packageId": "Volo.Abp.Studio.Client",
                                  "version": "9.9.9",
                                  "commands": []
                                },
                                {
                                  "packageId": "Volo.Abp.Studio.Cli",
                                  "version": "3.0.2",
                                  "commands": [
                                    "abp"
                                  ]
                                }
                              ]
                            }
                            """;

        var version = UpstreamResolver.ResolveLatestVersionFromJson(json);

        Assert.Equal("3.0.2", version);
    }

    [Fact]
    public void ResolveLatestVersionFromJsonRejectsMissingCliTool()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            UpstreamResolver.ResolveLatestVersionFromJson("""{"version":1,"data":[]}"""));

        Assert.Contains("Volo.Abp.Studio.Cli", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStableFullPackageUriUsesVersionedMacIntelEndpoint()
    {
        var uri = UpstreamResolver.CreateStableFullPackageUri("3.0.2");

        Assert.Equal(
            "https://abp.io/api/abp-studio/download/r/osx-intel/abp-studio-3.0.2-stable-full.nupkg",
            uri.ToString());
    }
}
