using System.Buffers.Binary;
using System.Text;

namespace AbpStudioLinux.Installer.Tests.Packaging;

public sealed class IconStagerTests
{
    [Fact]
    public void ExtractsPngEntriesFromIcns()
    {
        var root = TestPaths.CreateTempDirectory();
        var icns = Path.Combine(root, "abp.icns");
        File.WriteAllBytes(icns, CreateIcns(CreateMinimalPng(16, 16), CreateMinimalPng(32, 32)));

        var entries = IconStager.ExtractPngIconEntries(icns);

        Assert.Contains(entries, entry => entry.Width == 16 && entry.Height == 16);
        Assert.Contains(entries, entry => entry.Width == 32 && entry.Height == 32);
    }

    [Fact]
    public void PackageStagerUsesAppIconInsteadOfRepositorySvg()
    {
        var root = TestPaths.CreateTempDirectory();
        var app = Path.Combine(root, "app");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "Volo.Abp.Studio.UI.Host.dll"), "app");
        File.WriteAllBytes(Path.Combine(app, "abp.icns"), CreateIcns(CreateMinimalPng(16, 16)));

        PackageStager.Stage(new PackageStageOptions(
            NativePackageKind.Rpm,
            TestPaths.RepositoryRoot(),
            app,
            staging));

        Assert.True(File.Exists(Path.Combine(staging,
            "usr",
            "share",
            "icons",
            "hicolor",
            "16x16",
            "apps",
            "abp-studio.png")));
        Assert.False(File.Exists(Path.Combine(staging,
            "usr",
            "share",
            "icons",
            "hicolor",
            "scalable",
            "apps",
            "abp-studio.svg")));
        Assert.Equal(new[] { "abp-studio" }, Directory.GetFiles(Path.Combine(staging, "usr", "bin")).Select(Path.GetFileName));
    }

    private static byte[] CreateIcns(params byte[][] pngEntries)
    {
        var length = 8 + pngEntries.Sum(entry => 8 + entry.Length);
        var bytes = new byte[length];
        "icns"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), length);
        var offset = 8;

        for (var index = 0; index < pngEntries.Length; index++)
        {
            Encoding.ASCII.GetBytes($"ic{index:D2}").CopyTo(bytes.AsSpan(offset, 4));
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset + 4, 4), 8 + pngEntries[index].Length);
            pngEntries[index].CopyTo(bytes.AsSpan(offset + 8));
            offset += 8 + pngEntries[index].Length;
        }

        return bytes;
    }

    private static byte[] CreateMinimalPng(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }
}
