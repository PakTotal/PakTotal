using System.Text;
using CUE4Parse.FileProvider.Objects;

namespace PakTotal.Sdk.Context;

public sealed class ArchiveFile(GameFile file)
{
    public GameFile GameFile { get; } = file;
    public string Path => GameFile.Path;
    public string Name => GameFile.Name;
    public string Extension => GameFile.Extension;
    public long Size => GameFile.Size;
    public bool IsPackage => GameFile.IsUePackage;

    public byte[]? TryReadBytes() => GameFile.SafeRead();

    public string? TryReadText()
    {
        var bytes = TryReadBytes();
        if (bytes is null) return null;
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public override string ToString() => Path;
}
