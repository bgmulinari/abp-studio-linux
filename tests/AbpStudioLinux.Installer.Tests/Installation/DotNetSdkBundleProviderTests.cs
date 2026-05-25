namespace AbpStudioLinux.Installer.Tests.Installation;

public sealed class DotNetSdkBundleProviderTests
{
    [Fact]
    public void ResolvesRequiredRuntimeVersionFromAppRequirements()
    {
        var requirements = new[]
        {
            new DotNetRuntimeRequirement("Microsoft.NETCore.App", new Version(10, 0, 7)),
            new DotNetRuntimeRequirement("Microsoft.AspNetCore.App", new Version(10, 0, 7))
        };

        Assert.Equal(new Version(10, 0, 7), DotNetSdkBundleProvider.ResolveRequiredRuntimeVersion(requirements));
    }

    [Fact]
    public void ResolvesHighestSdkForRequiredRuntimeFromReleaseMetadata()
    {
        var download = DotNetSdkBundleProvider.ResolveSdkDownload(
            """
            {
              "releases": [
                {
                  "release-version": "10.0.8",
                  "runtime": {
                    "version": "10.0.8"
                  },
                  "sdks": [
                    {
                      "version": "10.0.300",
                      "files": [
                        {
                          "name": "dotnet-sdk-linux-x64.tar.gz",
                          "rid": "linux-x64",
                          "url": "https://example.invalid/dotnet-sdk-10.0.300-linux-x64.tar.gz",
                          "hash": "hash-300"
                        }
                      ]
                    }
                  ]
                },
                {
                  "release-version": "10.0.7",
                  "runtime": {
                    "version": "10.0.7"
                  },
                  "sdks": [
                    {
                      "version": "10.0.107",
                      "files": [
                        {
                          "name": "dotnet-sdk-linux-x64.tar.gz",
                          "rid": "linux-x64",
                          "url": "https://example.invalid/dotnet-sdk-10.0.107-linux-x64.tar.gz"
                        }
                      ]
                    },
                    {
                      "version": "10.0.203",
                      "files": [
                        {
                          "name": "dotnet-sdk-linux-x64.tar.gz",
                          "rid": "linux-x64",
                          "url": "https://example.invalid/dotnet-sdk-10.0.203-linux-x64.tar.gz",
                          "hash": "hash-203"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """,
            new Version(10, 0, 7),
            "linux-x64");

        Assert.Equal("10.0.7", download.RuntimeVersion);
        Assert.Equal("10.0.203", download.SdkVersion);
        Assert.Equal("https://example.invalid/dotnet-sdk-10.0.203-linux-x64.tar.gz", download.Url.ToString());
        Assert.Equal("hash-203", download.Hash);
    }
}
