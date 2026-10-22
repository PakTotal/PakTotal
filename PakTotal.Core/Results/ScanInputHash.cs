using System.Security.Cryptography;
using System.Text;
using PakTotal.Core.Archives;

namespace PakTotal.Core.Results;

public sealed record InputFileHash(string Role, string Name, long Size, string Sha256);

public sealed record ScanInputHashes(string Hash, IReadOnlyList<InputFileHash> Files);

public static class ScanInputHash
{
    private const string Format = "paktotal-input/1";

    public static async Task<ScanInputHashes> ComputeAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        var files = request.ArchivePaths.Select(p => ("archive", p))
            .Concat(request.GlobalPaths.Select(p => ("global", p)))
            .Concat(request.MappingsPath is { } mappings ? [("mappings", mappings)] : Array.Empty<(string, string)>());

        var hashes = new List<InputFileHash>();
        foreach (var (role, path) in files)
            hashes.Add(new InputFileHash(role, Path.GetFileName(path), new FileInfo(path).Length,
                await HashFileAsync(path, cancellationToken).ConfigureAwait(false)));

        var lines = hashes.Select(f => $"{f.Role}:{f.Name.ToLowerInvariant()}:{f.Size}:{f.Sha256}").Order(StringComparer.Ordinal);

        var aesKey = request.AesKey is { } key ? key[(key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 2 : 0)..].ToLowerInvariant() : "";
        var manifest = string.Join('\n', [Format, $"engine:{(uint)request.Game:x8}", $"aes:{aesKey}", .. lines]);
        return new ScanInputHashes(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))), hashes);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
