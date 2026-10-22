using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Microsoft.Extensions.Logging;

namespace PakTotal.Sdk.Context;

public sealed class ScanContext
{
    public required string ScanId { get; init; }
    public required EGame Game { get; init; }
    public required IFileProvider Provider { get; init; }
    public required IReadOnlyList<ArchiveFile> Files { get; init; }
    public required IReadOnlyList<BlueprintAsset> Blueprints { get; init; }
    public required IReadOnlySet<string> ImportedScriptPackages { get; init; }
    public required IReadOnlyList<string> LoadProblems { get; init; }
    public required bool HasGraphSupport { get; init; }
    public required ILogger Logger { get; init; }

    public bool ImportsScriptPackage(string packageName) => ImportedScriptPackages.Contains(packageName);

    public IEnumerable<ArchiveFile> ConfigFiles => Files.Where(f => f.Extension.Equals("ini", StringComparison.OrdinalIgnoreCase));

    public bool HasContent(string kind) => kind.ToLowerInvariant() switch
    {
        Modules.ContentKinds.Blueprints => Blueprints.Count > 0,
        Modules.ContentKinds.Config => ConfigFiles.Any(),
        Modules.ContentKinds.Packages => Files.Any(f => f.IsPackage),
        _ => false
    };

    private Dictionary<string, BlueprintAsset>? _classIndex;

    public BlueprintAsset? FindBlueprintByClass(string? classNameOrPath)
    {
        if (string.IsNullOrEmpty(classNameOrPath)) return null;
        _classIndex ??= BuildClassIndex();
        return _classIndex.GetValueOrDefault(classNameOrPath);
    }

    private Dictionary<string, BlueprintAsset> BuildClassIndex()
    {
        var index = new Dictionary<string, BlueprintAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var blueprint in Blueprints)
        {
            index.TryAdd(blueprint.Class.Name, blueprint);
            index.TryAdd(blueprint.Class.GetPathName(), blueprint);
        }
        return index;
    }
}
