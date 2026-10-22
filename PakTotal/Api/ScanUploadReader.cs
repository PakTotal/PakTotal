using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using PakTotal.Core;
using PakTotal.Core.Archives;
using PakTotal.Core.Engine;

namespace PakTotal.Api;

public sealed class UploadTooLargeException : Exception;

public delegate Task<IDisposable> ReserveBytes(long bytes, CancellationToken token);

public sealed record UploadResult(ScanRequest Request, IDisposable? ExtraLease);

public sealed partial class ScanUploadReader
{
    private const int MaxArchiveFiles = 16;
    private const int MaxFieldLength = 256;

    private readonly List<string> _archives = [];
    private readonly List<string> _globals = [];
    private string? _mappings;
    private string? _zip;
    private string? _aesKey;
    private string? _engineVersion;
    private long _bytes;

    public static async Task<UploadResult> ReadAsync(HttpRequest request, string scanId, string directory, long maxBytes, ReserveBytes reserve,
        CancellationToken token)
    {
        var reader = new ScanUploadReader();
        await reader.ReadSectionsAsync(request, directory, maxBytes, token);
        var lease = reader._zip is null ? null : await reader.ExtractZipAsync(directory, maxBytes, reserve, token);
        try
        {
            return new UploadResult(reader.Build(scanId, directory), lease);
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }

    private async Task<IDisposable> ExtractZipAsync(string directory, long maxBytes, ReserveBytes reserve, CancellationToken token)
    {
        var zip = _zip!;
        var declared = ZipExtractor.DeclaredArchiveBytes(zip);
        if (declared > maxBytes) throw new UploadTooLargeException();

        var lease = await reserve(declared, token);
        try
        {
            foreach (var path in ZipExtractor.Extract(zip, directory, maxBytes, n => SanitizeFileName(n)))
            {
                var isGlobal = Path.GetFileNameWithoutExtension(path).Equals("global", StringComparison.OrdinalIgnoreCase);
                if (isGlobal) _globals.Add(path);
                else _archives.Add(path);
            }
            File.Delete(zip);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private async Task ReadSectionsAsync(HttpRequest request, string directory, long maxBytes, CancellationToken token)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
            !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new ScanInputException("Expected multipart/form-data.");

        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary)) throw new ScanInputException("Missing multipart boundary.");

        var reader = new MultipartReader(boundary, request.Body) { BodyLengthLimit = maxBytes };
        while (await reader.ReadNextSectionAsync(token) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
            var field = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";

            if (disposition.IsFileDisposition())
            {
                var rawName = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
                var fileName = HeaderUtilities.RemoveQuotes(rawName).Value;
                var target = ResolveTarget(field, fileName, directory);
                await CopyAsync(section.Body, target, maxBytes, token);
            }
            else if (disposition.IsFormDisposition())
            {
                var value = await ReadFieldAsync(section.Body, token);
                switch (field)
                {
                    case "aesKey":
                        _aesKey = value.Trim();
                        break;
                    case "engineVersion":
                        _engineVersion = value.Trim();
                        break;
                }
            }
        }
    }

    private string ResolveTarget(string field, string? fileName, string directory)
    {
        var name = SanitizeFileName(fileName);
        switch (field)
        {
            case "archive":
            {
                if (_archives.Count >= MaxArchiveFiles) throw new ScanInputException("Too many archive files.");
                if (_zip is not null) throw new ScanInputException("Upload either one .zip file or the archive files, not both.");
                if (HasExtension(name, ".zip"))
                {
                    if (_archives.Count > 0) throw new ScanInputException("Upload either one .zip file or the archive files, not both.");
                    _zip = Path.Combine(directory, "upload.zip");
                    return _zip;
                }
                if (!HasExtension(name, ".pak", ".utoc", ".ucas"))
                    throw new ScanInputException($"'{name}' is not a .pak, .utoc, .ucas or .zip file.");
                if (Path.GetFileNameWithoutExtension(name).Equals("global", StringComparison.OrdinalIgnoreCase))
                    throw new ScanInputException("Upload global.utoc/global.ucas in the global field.");
                var path = Path.Combine(directory, name);
                if (_archives.Contains(path, StringComparer.OrdinalIgnoreCase)) throw new ScanInputException($"'{name}' was uploaded twice.");
                _archives.Add(path);
                return path;
            }
            case "global":
            {
                var extension = HasExtension(name, ".utoc") ? ".utoc" : HasExtension(name, ".ucas") ? ".ucas"
                    : throw new ScanInputException("The global files must be global.utoc and global.ucas.");
                var path = Path.Combine(directory, "global" + extension);
                if (_globals.Contains(path)) throw new ScanInputException($"global{extension} was uploaded twice.");
                _globals.Add(path);
                return path;
            }
            case "mappings":
            {
                if (_mappings is not null) throw new ScanInputException("Only one mappings file is allowed.");
                var extension = HasExtension(name, ".jmap.gz") ? ".jmap.gz" : HasExtension(name, ".jmap") ? ".jmap"
                    : HasExtension(name, ".usmap") ? ".usmap" : throw new ScanInputException("Mappings must be .usmap, .jmap or .jmap.gz.");
                _mappings = Path.Combine(directory, "mappings" + extension);
                return _mappings;
            }
            default:
                throw new ScanInputException($"Unexpected file field '{field}'.");
        }
    }

    private async Task CopyAsync(Stream source, string target, long maxBytes, CancellationToken token)
    {
        var buffer = new byte[81920];
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            _bytes += read;
            if (_bytes > maxBytes) throw new UploadTooLargeException();
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static async Task<string> ReadFieldAsync(Stream body, CancellationToken token)
    {
        var buffer = new byte[MaxFieldLength + 1];
        var read = await body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token);
        if (read > MaxFieldLength) throw new ScanInputException("A form field is too long.");
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private ScanRequest Build(string scanId, string directory)
    {
        if (!_archives.Any(p => HasExtension(p, ".pak", ".utoc")))
            throw new ScanInputException("Upload a .pak file, or .utoc/.ucas/.pak for IoStore.");

        var tocs = _archives.Where(p => HasExtension(p, ".utoc")).Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cas = _archives.Where(p => HasExtension(p, ".ucas")).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!tocs.SetEquals(cas)) throw new ScanInputException("Every .utoc needs a .ucas with the same name.");

        var globalCount = _globals.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (tocs.Count > 0 && globalCount != 2)
            throw new ScanInputException("IoStore archives need the game's global.utoc and global.ucas.");

        if (!EngineVersionCatalog.TryParseGame(_engineVersion, out var game)) throw new ScanInputException("Select a valid engine version.");

        string? aesKey = null;
        if (!string.IsNullOrEmpty(_aesKey))
        {
            if (!AesKeyPattern().IsMatch(_aesKey)) throw new ScanInputException("The AES key must be 64 hex characters.");
            aesKey = _aesKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? _aesKey : "0x" + _aesKey;
        }

        var uploaded = _archives.Concat(tocs.Count > 0 ? _globals : []).Append(_mappings).OfType<string>().ToList();
        if (uploaded.FirstOrDefault(p => !FileSignatures.IsValid(p)) is { } invalid)
            throw new ScanInputException($"'{Path.GetFileName(invalid)}' does not look like a valid file of its type.");

        return new ScanRequest
        {
            ScanId = scanId,
            WorkDirectory = directory,
            Game = game,
            ArchivePaths = _archives,
            GlobalPaths = tocs.Count > 0 ? _globals : [],
            MappingsPath = _mappings,
            AesKey = aesKey,
            TotalBytes = _bytes
        };
    }

    private static bool HasExtension(string name, params string[] extensions) =>
        extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name) builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        var sanitized = builder.ToString().TrimStart('.');
        if (sanitized.Length is 0 or > 128) throw new ScanInputException("Invalid file name.");
        return sanitized;
    }

    [GeneratedRegex("^(0x)?[0-9a-fA-F]{64}$")]
    private static partial Regex AesKeyPattern();
}
