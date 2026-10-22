using System.Text;

namespace PakTotal.Api;

public static class FileSignatures
{
    private static readonly byte[] PakMagic = BitConverter.GetBytes(0x5A6F12E1u);
    private static readonly byte[] TocMagic = "-==--==--==--==-"u8.ToArray();
    private static readonly byte[] UsmapMagic = BitConverter.GetBytes((ushort)0x30C4);
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    public static bool IsValid(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) return HasPakFooter(path);
        if (name.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase)) return StartsWith(path, TocMagic);
        if (name.EndsWith(".ucas", StringComparison.OrdinalIgnoreCase)) return new FileInfo(path).Length > 0;
        if (name.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase)) return StartsWith(path, UsmapMagic);
        if (name.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase)) return StartsWith(path, GzipMagic);
        if (name.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase)) return StartsWithJson(path);
        return false;
    }

    private static bool StartsWith(string path, byte[] magic)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[magic.Length];
        return stream.ReadAtLeast(buffer, magic.Length, throwOnEndOfStream: false) == magic.Length && buffer.SequenceEqual(magic);
    }

    private static bool StartsWithJson(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[64];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var text = Encoding.UTF8.GetString(buffer[..read]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith('{');
    }

    private static bool HasPakFooter(string path)
    {
        using var stream = File.OpenRead(path);
        var length = (int)Math.Min(stream.Length, 1024);
        if (length < PakMagic.Length) return false;
        stream.Seek(-length, SeekOrigin.End);
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return buffer.AsSpan().IndexOf(PakMagic) >= 0;
    }
}
