using System.Text.Json;
using CUE4Parse.UE4.Versions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakTotal.Core.Engine;
using PakTotal.Sdk;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Core.Modules;

public sealed record LoadedModule(ModuleManifest Manifest, IScanModule Instance)
{
    public string Id => Manifest.Id;
}

public sealed class ModuleCatalog
{
    private readonly ILogger<ModuleCatalog> _logger;

    public ModuleCatalog(IOptions<PakTotalOptions> options, ILogger<ModuleCatalog> logger)
    {
        _logger = logger;
        var directory = Path.GetFullPath(options.Value.ModulesDirectory, AppContext.BaseDirectory);
        Modules = LoadAll(directory);
    }

    public IReadOnlyList<LoadedModule> Modules { get; }

    public bool IsApplicable(LoadedModule module, ScanContext context)
    {
        try
        {
            return module.Manifest.Requires.All(context.HasContent) && MatchesManifest(module.Manifest.AppliesTo, context) &&
                   module.Instance.IsApplicable(context);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Module {Module} applicability check failed for scan {ScanId}", module.Id, context.ScanId);
            return true;
        }
    }

    private List<LoadedModule> LoadAll(string directory)
    {
        var modules = new List<LoadedModule>();
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Modules directory {Directory} does not exist, no modules loaded", directory);
            return modules;
        }

        foreach (var moduleDirectory in Directory.GetDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(moduleDirectory, "module.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                if (TryLoad(moduleDirectory, manifestPath) is { } module) modules.Add(module);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to load module from {Directory}", moduleDirectory);
            }
        }

        if (modules.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new InvalidOperationException($"Duplicate module id '{duplicate.Key}'");
        if (modules.GroupBy(m => ModuleCodeNames.For(m.Manifest), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } sameName)
            throw new InvalidOperationException($"Duplicate module code name '{sameName.Key}' ({string.Join(", ", sameName.Select(m => m.Id))})");

        _logger.LogInformation("Loaded {Count} modules: {Modules}", modules.Count,
            string.Join(", ", modules.Select(m => $"{m.Id}@{m.Manifest.Version} ({m.Manifest.Visibility})")));
        return modules;
    }

    private LoadedModule? TryLoad(string moduleDirectory, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(manifestPath), ModuleManifest.JsonOptions)
                       ?? throw new InvalidDataException("Empty manifest");
        manifest.Directory = moduleDirectory;

        if (!manifest.Enabled)
        {
            _logger.LogInformation("Module {Module} is disabled", manifest.Id);
            return null;
        }
        if (!SemVer.TryParse(manifest.Version, out _)) throw new InvalidDataException($"Module version '{manifest.Version}' is not semver");
        if (!SemVer.TryParse(manifest.SdkVersion, out var sdkVersion))
            throw new InvalidDataException($"SDK version '{manifest.SdkVersion}' is not semver");
        if (!sdkVersion.IsCompatibleWith(SdkInfo.Version))
        {
            _logger.LogWarning("Module {Module} targets SDK {ModuleSdk}, host SDK is {HostSdk}; skipped", manifest.Id, sdkVersion, SdkInfo.Version);
            return null;
        }
        ValidateApplicability(manifest);
        var known = new[] { ContentKinds.Blueprints, ContentKinds.Config, ContentKinds.Packages };
        if (manifest.Requires.FirstOrDefault(r => !known.Contains(r, StringComparer.OrdinalIgnoreCase)) is { } unknownKind)
            throw new InvalidDataException($"Unknown content kind '{unknownKind}' in {manifest.Id}");

        var assemblyPath = Path.Combine(moduleDirectory, manifest.Assembly);
        var context = new ModuleLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
        if (Activator.CreateInstance(type) is not IScanModule instance)
            throw new InvalidDataException($"{manifest.EntryType} does not implement {nameof(IScanModule)}");

        instance.Initialize(manifest);
        return new LoadedModule(manifest, instance);
    }

    private static void ValidateApplicability(ModuleManifest manifest)
    {
        var applies = manifest.AppliesTo;
        foreach (var name in new[] { applies.MinEngine, applies.MaxEngine }.OfType<string>().Concat(applies.Games).Concat(applies.ExcludeGames))
        {
            if (!EngineVersionCatalog.TryParseGame(name, out _)) throw new InvalidDataException($"Unknown EGame value '{name}' in {manifest.Id}");
        }
    }

    private static bool MatchesManifest(ModuleApplicability applies, ScanContext context)
    {
        var engine = EngineVersionCatalog.EngineOf(context.Game);
        if (applies.MinEngine is { } min && engine < EngineVersionCatalog.EngineOf(Enum.Parse<EGame>(min, true))) return false;
        if (applies.MaxEngine is { } max && engine > EngineVersionCatalog.EngineOf(Enum.Parse<EGame>(max, true))) return false;
        if (applies.Games.Count > 0 && !applies.Games.Any(g => Enum.Parse<EGame>(g, true) == context.Game)) return false;
        if (applies.ExcludeGames.Any(g => Enum.Parse<EGame>(g, true) == context.Game)) return false;
        return applies.RequiresAnyScriptPackage.Count == 0 || applies.RequiresAnyScriptPackage.Any(context.ImportsScriptPackage);
    }
}
