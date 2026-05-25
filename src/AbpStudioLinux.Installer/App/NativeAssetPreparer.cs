using System.Security;

namespace AbpStudioLinux.Installer.App;

public static class NativeAssetPreparer
{
    public static void Prepare(InstallOptions options, TextWriter stdout, TextWriter stderr)
    {
        stderr.WriteLine("[INFO] Restoring Linux native assets");
        var project = WriteRestoreProject(options);
        RunRequired(
            new ShellCommand(options.DotNetCommand, new[] { "restore", project, "--packages", options.NuGetPackagesDirectory }),
            stdout,
            stderr);

        BuildAppCommand.ResetDirectory(options.NativeOverridesDirectory);
        Directory.CreateDirectory(Path.Combine(options.NativeOverridesDirectory, "CefGlueBrowserProcess"));

        var packages = options.NuGetPackagesDirectory;
        var versions = options.NativeAssetVersions;
        CopyDirectory(Path.Combine(packages, "cef.redist.linux64", versions.Cef, "CEF"), options.NativeOverridesDirectory);
        CopyDirectory(Path.Combine(packages, "volo.cefglue.common", versions.CefGlue, "bin", "linux-x64"), Path.Combine(options.NativeOverridesDirectory, "CefGlueBrowserProcess"));

        CopyFile(Path.Combine(packages, "volo.cefglue.avalonia", versions.CefGlue, "lib", "net8.0", "Xilium.CefGlue.Avalonia.dll"), Path.Combine(options.NativeOverridesDirectory, "Xilium.CefGlue.Avalonia.dll"));
        CopyFile(Path.Combine(packages, "volo.cefglue.common", versions.CefGlue, "lib", "net8.0", "Xilium.CefGlue.Common.Shared.dll"), Path.Combine(options.NativeOverridesDirectory, "Xilium.CefGlue.Common.Shared.dll"));
        CopyFile(Path.Combine(packages, "volo.cefglue.common", versions.CefGlue, "lib", "net8.0", "Xilium.CefGlue.Common.dll"), Path.Combine(options.NativeOverridesDirectory, "Xilium.CefGlue.Common.dll"));
        CopyFile(Path.Combine(packages, "volo.cefglue.common", versions.CefGlue, "lib", "net8.0", "Xilium.CefGlue.dll"), Path.Combine(options.NativeOverridesDirectory, "Xilium.CefGlue.dll"));
        CopyFile(Path.Combine(packages, "skiasharp.nativeassets.linux", versions.SkiaSharp, "runtimes", "linux-x64", "native", "libSkiaSharp.so"), Path.Combine(options.NativeOverridesDirectory, "libSkiaSharp.so"));
        CopyFile(Path.Combine(packages, "harfbuzzsharp.nativeassets.linux", versions.HarfBuzzSharp, "runtimes", "linux-x64", "native", "libHarfBuzzSharp.so"), Path.Combine(options.NativeOverridesDirectory, "libHarfBuzzSharp.so"));
        CopyFile(Path.Combine(packages, "sqlitepclraw.lib.e_sqlite3", versions.SqlitePclRaw, "runtimes", "linux-x64", "native", "libe_sqlite3.so"), Path.Combine(options.NativeOverridesDirectory, "libe_sqlite3.so"));
        CopyFile(Path.Combine(packages, "libgit2sharp.nativebinaries", versions.LibGit2SharpNative, "runtimes", "linux-x64", "native", "libgit2-3f4182d.so"), Path.Combine(options.NativeOverridesDirectory, "libgit2-3f4182d.so"));
        CopyFirstMatch(
            Path.Combine(options.NativeOverridesDirectory, "libmongocrypt.so"),
            Path.Combine(packages, "mongodb.libmongocrypt", versions.MongoDbLibmongocrypt, "runtimes", "linux", "native", "libmongocrypt.so"),
            Path.Combine(packages, "mongodb.libmongocrypt", versions.MongoDbLibmongocrypt, "runtimes", "linux-x64", "native", "libmongocrypt.so"));
        CopyFirstMatch(
            Path.Combine(options.NativeOverridesDirectory, "Tmds.DBus.Protocol.dll"),
            Path.Combine(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, "lib", "net9.0", "Tmds.DBus.Protocol.dll"),
            Path.Combine(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, "lib", "net8.0", "Tmds.DBus.Protocol.dll"),
            Path.Combine(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, "lib", "net6.0", "Tmds.DBus.Protocol.dll"),
            Path.Combine(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, "lib", "netstandard2.1", "Tmds.DBus.Protocol.dll"),
            Path.Combine(packages, "tmds.dbus.protocol", versions.TmdsDBusProtocol, "lib", "netstandard2.0", "Tmds.DBus.Protocol.dll"));
    }

    private static string WriteRestoreProject(InstallOptions options)
    {
        var projectDirectory = Path.Combine(options.WorkDirectory, "native-restore");
        Directory.CreateDirectory(projectDirectory);
        var project = Path.Combine(projectDirectory, "NativeAssets.csproj");
        var packagesPath = SecurityElement.Escape(options.NuGetPackagesDirectory);
        var versions = options.NativeAssetVersions;
        File.WriteAllText(
            project,
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
                 <RestorePackagesPath>{packagesPath}</RestorePackagesPath>
               </PropertyGroup>
               <ItemGroup>
                 <PackageReference Include="cef.redist.linux64" Version="{versions.Cef}" />
                 <PackageReference Include="Volo.CefGlue.Avalonia" Version="{versions.CefGlue}" />
                 <PackageReference Include="Volo.CefGlue.Common" Version="{versions.CefGlue}" />
                 <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="{versions.SkiaSharp}" />
                 <PackageReference Include="HarfBuzzSharp.NativeAssets.Linux" Version="{versions.HarfBuzzSharp}" />
                 <PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="{versions.SqlitePclRaw}" />
                 <PackageReference Include="LibGit2Sharp.NativeBinaries" Version="{versions.LibGit2SharpNative}" />
                 <PackageReference Include="MongoDB.Libmongocrypt" Version="{versions.MongoDbLibmongocrypt}" />
                 <PackageReference Include="Tmds.DBus.Protocol" Version="{versions.TmdsDBusProtocol}" />
               </ItemGroup>
             </Project>
             """);
        return project;
    }

    private static void CopyDirectory(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Missing required native asset directory: {source}");
        }

        BuildAppCommand.CopyDirectory(source, target, true);
    }

    private static void CopyFile(string source, string target)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Missing required native asset.", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    private static void CopyFirstMatch(string target, params string[] sources)
    {
        foreach (var source in sources)
        {
            if (File.Exists(source))
            {
                CopyFile(source, target);
                return;
            }
        }

        throw new FileNotFoundException($"Missing required native asset for {target}.");
    }

    private static void RunRequired(ShellCommand command, TextWriter stdout, TextWriter stderr)
    {
        var exitCode = ProcessRunner.Run(command, stdout, stderr);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{command.FileName} exited with code {exitCode}.");
        }
    }
}
