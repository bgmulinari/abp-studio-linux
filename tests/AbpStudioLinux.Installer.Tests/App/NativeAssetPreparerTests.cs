namespace AbpStudioLinux.Installer.Tests.App;

public sealed class NativeAssetPreparerTests
{
    [Fact]
    public void PrepareCopiesFixedTmdsDBusProtocolOverride()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = TestPaths.CreateTempDirectory();
        var packages = Path.Combine(root, "packages");
        var versions = new NativeAssetVersions(
            "cef",
            "cefglue",
            "skia",
            "harfbuzz",
            "sqlite",
            "libgit",
            "mongo",
            "0.92.0");
        var options = new InstallOptions(
            root,
            Path.Combine(root, "work"),
            Path.Combine(root, "dist"),
            Path.Combine(root, "app"),
            null,
            null,
            true,
            true,
            false,
            WriteFakeDotNet(root),
            "node",
            null,
            Path.Combine(root, "native-overrides"),
            Path.Combine(root, "downloads"),
            Path.Combine(root, "dotnet-bundle"),
            packages,
            versions);

        WritePackageFile(packages, "cef.redist.linux64", versions.Cef, Path.Combine("CEF", "libcef.so"), "cef");
        WritePackageFile(packages, "volo.cefglue.common", versions.CefGlue, Path.Combine("bin", "linux-x64", "Xilium.CefGlue.BrowserProcess"), "browser");
        WritePackageFile(packages, "volo.cefglue.avalonia", versions.CefGlue, Path.Combine("lib", "net8.0", "Xilium.CefGlue.Avalonia.dll"), "avalonia");
        WritePackageFile(packages, "volo.cefglue.common", versions.CefGlue, Path.Combine("lib", "net8.0", "Xilium.CefGlue.Common.Shared.dll"), "shared");
        WritePackageFile(packages, "volo.cefglue.common", versions.CefGlue, Path.Combine("lib", "net8.0", "Xilium.CefGlue.Common.dll"), "common");
        WritePackageFile(packages, "volo.cefglue.common", versions.CefGlue, Path.Combine("lib", "net8.0", "Xilium.CefGlue.dll"), "cefglue");
        WritePackageFile(packages, "skiasharp.nativeassets.linux", versions.SkiaSharp, Path.Combine("runtimes", "linux-x64", "native", "libSkiaSharp.so"), "skia");
        WritePackageFile(packages, "harfbuzzsharp.nativeassets.linux", versions.HarfBuzzSharp, Path.Combine("runtimes", "linux-x64", "native", "libHarfBuzzSharp.so"), "harfbuzz");
        WritePackageFile(packages, "sqlitepclraw.lib.e_sqlite3", versions.SqlitePclRaw, Path.Combine("runtimes", "linux-x64", "native", "libe_sqlite3.so"), "sqlite");
        WritePackageFile(packages, "libgit2sharp.nativebinaries", versions.LibGit2SharpNative, Path.Combine("runtimes", "linux-x64", "native", "libgit2-3f4182d.so"), "libgit");
        WritePackageFile(packages, "mongodb.libmongocrypt", versions.MongoDbLibmongocrypt, Path.Combine("runtimes", "linux", "native", "libmongocrypt.so"), "mongo");
        WritePackageFile(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, Path.Combine("lib", "net9.0", "Tmds.DBus.Protocol.dll"), "fixed dbus");

        NativeAssetPreparer.Prepare(options, TextWriter.Null, TextWriter.Null);

        Assert.Equal(
            "fixed dbus",
            File.ReadAllText(Path.Combine(options.NativeOverridesDirectory, "Tmds.DBus.Protocol.dll")));
        Assert.Contains(
            """<PackageReference Include="Tmds.DBus.Protocol" Version="0.92.0" />""",
            File.ReadAllText(Path.Combine(options.WorkDirectory, "native-restore", "NativeAssets.csproj")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAssetVersionsDefaultsToTmdsDBusProtocolWithShutdownFix()
    {
        var previous = Environment.GetEnvironmentVariable("TMDS_DBUS_PROTOCOL_VERSION");

        try
        {
            Environment.SetEnvironmentVariable("TMDS_DBUS_PROTOCOL_VERSION", null);

            Assert.Equal("0.92.0", NativeAssetVersions.FromEnvironment().TmdsDBusProtocol);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMDS_DBUS_PROTOCOL_VERSION", previous);
        }
    }

    private static string WriteFakeDotNet(string root)
    {
        var dotnet = Path.Combine(root, "dotnet");
        File.WriteAllText(
            dotnet,
            """
            #!/bin/sh
            exit 0
            """);
        MakeExecutable(dotnet);
        return dotnet;
    }

    private static void WritePackageFile(string packages, string packageId, string version, string relativePath, string content)
    {
        var path = Path.Combine(packages, packageId, version, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
