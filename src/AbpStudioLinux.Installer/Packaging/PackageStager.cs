namespace AbpStudioLinux.Installer.Packaging;

public sealed record PackageStageOptions(
    NativePackageKind Kind,
    string RepositoryRoot,
    string AppDirectory,
    string StagingRoot);

public static class PackageStager
{
    public static void Stage(PackageStageOptions options)
    {
        if (!Directory.Exists(options.AppDirectory))
        {
            throw new DirectoryNotFoundException(options.AppDirectory);
        }

        BuildAppCommand.ResetDirectory(options.StagingRoot);
        var appRoot = Path.Combine(options.StagingRoot, "opt", "abp-studio");
        BuildAppCommand.CopyDirectory(options.AppDirectory, appRoot);

        var usrBin = Path.Combine(options.StagingRoot, "usr", "bin");
        Directory.CreateDirectory(usrBin);
        WriteTextExecutable(
            Path.Combine(usrBin, "abp-studio"),
            """
            #!/bin/bash
            exec /opt/abp-studio/start.sh "$@"
            """);

        CopyTemplate(options, "packaging/linux/abp-studio.desktop", "usr/share/applications/abp-studio.desktop");
        IconStager.StageFromAppIcon(options.AppDirectory, options.StagingRoot);
    }

    private static void CopyTemplate(PackageStageOptions options, string sourceRelativePath, string targetRelativePath)
    {
        var source = Path.Combine(options.RepositoryRoot, sourceRelativePath);
        var target = Path.Combine(options.StagingRoot, targetRelativePath);

        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Required packaging template is missing.", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    private static void WriteTextExecutable(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        BuildAppCommand.MakeExecutable(path);
    }
}
