using System.Security.Cryptography;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Jmap;
using CUE4Parse.MappingsProvider.Usmap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UEBlueprintGraphViewer;

namespace PakTotal.Core.Archives;

public sealed record ParsedMappings(TypeMappings? Types, JmapData? Jmap);

public sealed class MappingsCache(IOptions<PakTotalOptions> options, ILogger<MappingsCache> logger)
{
    private readonly Lock _lock = new();
    private readonly LinkedList<(string Key, Lazy<Task<ParsedMappings>> Value)> _entries = [];

    public async Task<ParsedMappings> GetAsync(string path, CancellationToken cancellationToken)
    {
        var capacity = options.Value.MappingsCacheEntries;
        if (capacity <= 0) return await Task.Run(() => Parse(path), cancellationToken).ConfigureAwait(false);

        var key = $"{Kind(path)}:{await HashAsync(path, cancellationToken).ConfigureAwait(false)}";
        Lazy<Task<ParsedMappings>> entry;
        lock (_lock)
        {
            var node = _entries.First;
            while (node is not null && node.Value.Key != key) node = node.Next;
            if (node is null)
            {
                var directory = Path.Combine(Path.GetFullPath(options.Value.TempDirectory, AppContext.BaseDirectory), "_mappings");
                var copy = Path.Combine(directory, $"{Guid.NewGuid():N}{Extension(path)}");
                node = _entries.AddFirst((key, new Lazy<Task<ParsedMappings>>(() => Task.Run(() => ParseCopy(path, copy)))));
                while (_entries.Count > capacity) _entries.RemoveLast();
                logger.LogInformation("Parsing mappings {Key}", key[..Math.Min(key.Length, 20)]);
            }
            else if (node != _entries.First)
            {
                _entries.Remove(node);
                _entries.AddFirst(node);
            }
            entry = node.Value.Value;
        }

        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (_lock)
            {
                var node = _entries.First;
                while (node is not null && !ReferenceEquals(node.Value.Value, entry)) node = node.Next;
                if (node is not null) _entries.Remove(node);
            }
            throw;
        }
    }

    // The upload is deleted when its scan ends, while the parse may still be shared by other scans, so it reads a private copy.
    private static ParsedMappings ParseCopy(string path, string copy)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(path, copy);
        try
        {
            return Parse(copy);
        }
        finally
        {
            File.Delete(copy);
        }
    }

    private static ParsedMappings Parse(string path)
    {
        if (Kind(path) == "usmap") return new ParsedMappings(new FileUsmapTypeMappingsProvider(path, StringComparer.OrdinalIgnoreCase).MappingsForGame, null);
        var types = Task.Run(() => new JmapTypeMappingsProvider(path, StringComparer.OrdinalIgnoreCase).MappingsForGame);
        var jmap = new JmapData(path);
        return new ParsedMappings(types.GetAwaiter().GetResult(), jmap);
    }

    private static string Kind(string path) => path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase) ? "usmap" : "jmap";

    private static string Extension(string path) => path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? ".jmap.gz" : Path.GetExtension(path);

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class CachedTypeMappingsProvider(TypeMappings? mappings) : ITypeMappingsProvider
{
    public TypeMappings? MappingsForGame { get; } = mappings;

    public void Load(string path, StringComparer? comparer = null) => throw new NotSupportedException();

    public void Load(byte[] bytes, StringComparer? comparer = null) => throw new NotSupportedException();

    public void Reload() { }
}
