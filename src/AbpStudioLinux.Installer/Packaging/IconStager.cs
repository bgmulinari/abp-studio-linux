using System.Buffers.Binary;

namespace AbpStudioLinux.Installer.Packaging;

public sealed record PngIconEntry(
    int Width,
    int Height,
    byte[] Bytes);

public static class IconStager
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };

    public static void StageFromAppIcon(string appDirectory, string stagingRoot)
    {
        var source = Directory
            .EnumerateFiles(appDirectory, "*.icns", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path).Equals("abp.icns", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (source is null)
        {
            throw new FileNotFoundException("Could not find ABP Studio .icns icon in the converted app.", Path.Combine(appDirectory, "abp.icns"));
        }

        var entries = ExtractPngIconEntries(source)
            .Where(entry => entry.Width == entry.Height)
            .GroupBy(entry => entry.Width)
            .Select(group => group.OrderByDescending(entry => entry.Bytes.Length).First())
            .OrderBy(entry => entry.Width)
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"ABP Studio icon did not contain PNG icon entries: {source}");
        }

        foreach (var entry in entries)
        {
            var target = Path.Combine(
                stagingRoot,
                "usr",
                "share",
                "icons",
                "hicolor",
                $"{entry.Width}x{entry.Height}",
                "apps",
                "abp-studio.png");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, entry.Bytes);
        }
    }

    public static IReadOnlyList<PngIconEntry> ExtractPngIconEntries(string icnsPath)
    {
        var bytes = File.ReadAllBytes(icnsPath);

        if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual("icns"u8))
        {
            throw new InvalidDataException($"Invalid ICNS file: {icnsPath}");
        }

        var entries = new List<PngIconEntry>();
        var offset = 8;

        while (offset + 8 <= bytes.Length)
        {
            var entryLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset + 4, 4));

            if (entryLength < 8 || offset + entryLength > bytes.Length)
            {
                break;
            }

            var dataOffset = offset + 8;
            var dataLength = entryLength - 8;
            var data = bytes.AsSpan(dataOffset, dataLength);

            if (data.StartsWith(PngSignature) && TryReadPngDimensions(data, out var width, out var height))
            {
                entries.Add(new PngIconEntry(width, height, data.ToArray()));
            }

            offset += entryLength;
        }

        return entries;
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data.Length < 24 || !data.StartsWith(PngSignature) || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
        return width > 0 && height > 0;
    }
}
