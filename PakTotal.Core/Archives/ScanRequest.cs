using CUE4Parse.UE4.Versions;

namespace PakTotal.Core.Archives;

public sealed class ScanRequest
{
    public required string ScanId { get; init; }
    public required string WorkDirectory { get; init; }
    public required EGame Game { get; init; }
    public required IReadOnlyList<string> ArchivePaths { get; init; }
    public IReadOnlyList<string> GlobalPaths { get; init; } = [];
    public string? MappingsPath { get; init; }
    public string? AesKey { get; init; }
    public long TotalBytes { get; init; }

    public bool IsIoStore => ArchivePaths.Any(p => p.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase));
}
