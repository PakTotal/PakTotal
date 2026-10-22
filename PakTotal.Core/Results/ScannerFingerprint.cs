using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PakTotal.Core.Modules;
using PakTotal.Sdk;

namespace PakTotal.Core.Results;

public static class ScannerFingerprint
{
    private static readonly string[] ScanningAssemblyPrefixes = ["CUE4Parse", "UEBlueprintGraphViewer"];

    public static string Compute(ModuleCatalog catalog)
    {
        var lines = new List<string>();
        var core = typeof(ScannerFingerprint).Assembly;
        var sdk = typeof(SdkInfo).Assembly;
        var assemblies = new[] { core, sdk }
            .Concat(new[] { core, sdk }.SelectMany(a => a.GetReferencedAssemblies())
                .Where(n => ScanningAssemblyPrefixes.Any(p => n.Name?.StartsWith(p, StringComparison.Ordinal) == true))
                .Select(Assembly.Load))
            .DistinctBy(a => a.FullName);
        lines.AddRange(assemblies.Select(a => $"assembly:{a.GetName().Name}:{a.ManifestModule.ModuleVersionId:N}"));

        foreach (var module in catalog.Modules.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            var manifest = module.Manifest;
            lines.Add($"module:{manifest.Id}:{manifest.Version}:{HashFile(Path.Combine(manifest.Directory, "module.json"))}:" +
                      HashFile(Path.Combine(manifest.Directory, manifest.Assembly)));
        }
        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
