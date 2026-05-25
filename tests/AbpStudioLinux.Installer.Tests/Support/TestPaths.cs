namespace AbpStudioLinux.Installer.Tests.Support;

internal static class TestPaths
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "abp-studio-linux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AbpStudioLinux.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
