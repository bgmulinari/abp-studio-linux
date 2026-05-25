namespace AbpStudioLinux.Installer.Packaging;

public enum NativePackageKind
{
    Deb,
    Rpm,
    Pacman
}

public static class NativePackageKindParser
{
    public static NativePackageKind Parse(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "deb" or "debian" => NativePackageKind.Deb,
            "rpm" => NativePackageKind.Rpm,
            "pacman" or "arch" => NativePackageKind.Pacman,
            _ => throw new ArgumentException($"Unsupported package format: {value}")
        };
    }

    public static string ToCliValue(NativePackageKind kind)
    {
        return kind switch
        {
            NativePackageKind.Deb => "deb",
            NativePackageKind.Rpm => "rpm",
            NativePackageKind.Pacman => "pacman",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}
