using System.IO.Compression;

namespace PakTotal.Core.Archives;

public static class ZipExtractor
{
    private const int MaxEntries = 10_000;
    private const long RatioCheckThreshold = 64L * 1024 * 1024;
    private const long MaxRatio = 1000;
    private static readonly string[] ArchiveExtensions = [".pak", ".utoc", ".ucas"];

    public static long DeclaredArchiveBytes(string zipPath)
    {
        using var zip = Open(zipPath);
        if (zip.Entries.Count > MaxEntries) throw new ScanInputException("The zip file contains too many entries.");
        long total = 0;
        foreach (var entry in zip.Entries.Where(IsArchiveEntry))
        {
            if (entry.Length > RatioCheckThreshold && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxRatio)
                throw new ScanInputException("The zip file has an unrealistic compression ratio.");
            total += entry.Length;
        }
        return total;
    }

    public static IReadOnlyList<string> Extract(string zipPath, string directory, long maxBytes, Func<string, string> sanitize)
    {
        using var zip = Open(zipPath);
        var written = new List<string>();
        long total = 0;
        foreach (var entry in zip.Entries.Where(IsArchiveEntry))
        {
            var target = Path.Combine(directory, sanitize(entry.Name));
            if (written.Contains(target, StringComparer.OrdinalIgnoreCase) || File.Exists(target))
                throw new ScanInputException($"The zip file contains '{entry.Name}' more than once.");

            try
            {
                using var source = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                var buffer = new byte[81920];
                long entryBytes = 0;
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    entryBytes += read;
                    total += read;
                    if (entryBytes > entry.Length) throw new ScanInputException("The zip file is corrupt.");
                    if (total > maxBytes) throw new ScanInputException("The zip file expands beyond the upload limit.");
                    output.Write(buffer, 0, read);
                }
            }
            catch (InvalidDataException)
            {
                throw new ScanInputException("The zip file could not be read.");
            }
            written.Add(target);
        }
        if (written.Count == 0) throw new ScanInputException("The zip file does not contain any .pak, .utoc or .ucas files.");
        return written;
    }

    private static bool IsArchiveEntry(ZipArchiveEntry entry) =>
        entry.Name.Length > 0 && ArchiveExtensions.Any(e => entry.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static ZipArchive Open(string zipPath)
    {
        try
        {
            return ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException)
        {
            throw new ScanInputException("The zip file could not be read.");
        }
    }
}
