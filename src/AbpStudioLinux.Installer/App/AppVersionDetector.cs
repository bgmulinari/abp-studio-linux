using System.Reflection;

namespace AbpStudioLinux.Installer.App;

public static class AppVersionDetector
{
    private const string HostAssemblyFileName = "Volo.Abp.Studio.UI.Host.dll";

    public static string GetVersion(string appDirectory)
    {
        var hostAssemblyPath = Path.Combine(appDirectory, HostAssemblyFileName);

        if (!File.Exists(hostAssemblyPath))
        {
            throw new FileNotFoundException("Could not find ABP Studio host assembly.", hostAssemblyPath);
        }

        var version = AssemblyName.GetAssemblyName(hostAssemblyPath).Version
                      ?? throw new InvalidOperationException($"Could not read ABP Studio version from {hostAssemblyPath}.");

        return NormalizeVersion(version);
    }

    private static string NormalizeVersion(Version version) =>
        version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
}
