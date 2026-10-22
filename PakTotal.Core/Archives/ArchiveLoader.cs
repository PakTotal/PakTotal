using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using Microsoft.Extensions.Logging;
using PakTotal.Sdk.Context;
using UEBlueprintGraphViewer;
using UEBlueprintGraphViewer.Assets;
using UEBlueprintGraphViewer.Decompiler;

namespace PakTotal.Core.Archives;

public sealed class LoadedScan(StreamedFileProvider provider, ScanContext context) : IDisposable
{
    public ScanContext Context { get; } = context;

    public void Dispose() => provider.Dispose();
}

public sealed class ArchiveLoader(MappingsCache mappingsCache, ILoggerFactory loggerFactory)
{
    public async Task<LoadedScan> LoadAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger($"PakTotal.Scan.{request.ScanId}");
        var provider = new StreamedFileProvider("PakTotal", new VersionContainer(request.Game), StringComparer.OrdinalIgnoreCase)
        {
            ReadScriptData = true
        };

        try
        {
            var graphSettings = await ConfigureMappingsAsync(provider, request, cancellationToken).ConfigureAwait(false);
            provider.Initialize();

            foreach (var global in request.GlobalPaths.Where(IsContainer)) provider.RegisterVfs(global);
            foreach (var archive in request.ArchivePaths.Where(IsContainer)) provider.RegisterVfs(archive);

            await provider.MountAsync().ConfigureAwait(false);
            await SubmitKeysAsync(provider, request.AesKey).ConfigureAwait(false);

            if (provider.MountedVfs.Count == 0) throw new ScanInputException("The archive could not be read. Check the file and engine version.");
            if (request.IsIoStore && provider.GlobalData is null)
                throw new ScanInputException("The global.utoc/global.ucas files could not be read.");

            var problems = new List<string>();
            var files = provider.Files.Values.Where(f => !IsGlobalEntry(f)).Select(f => new ArchiveFile(f)).ToList();
            var imports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blueprints = new List<BlueprintAsset>();

            foreach (var file in files.Where(f => f.IsPackage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IPackage package;
                try
                {
                    package = await provider.LoadPackageAsync(file.GameFile).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    problems.Add($"{file.Path}: package load failed: {e.Message}");
                    continue;
                }

                CollectScriptImports(package, imports, problems);
                blueprints.AddRange(LoadBlueprints(package, file, graphSettings, problems));
            }

            var context = new ScanContext
            {
                ScanId = request.ScanId,
                Game = request.Game,
                Provider = provider,
                Files = files,
                Blueprints = blueprints,
                ImportedScriptPackages = imports,
                LoadProblems = problems,
                HasGraphSupport = graphSettings is not null,
                Logger = logger
            };
            return new LoadedScan(provider, context);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private static bool IsContainer(string path) =>
        path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase);

    private static bool IsGlobalEntry(GameFile file) =>
        file is VfsEntry entry && entry.Vfs.Name.StartsWith("global", StringComparison.OrdinalIgnoreCase);

    private async Task<GameSettings?> ConfigureMappingsAsync(StreamedFileProvider provider, ScanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.MappingsPath is not { } path) return null;
        var mappings = await mappingsCache.GetAsync(path, cancellationToken).ConfigureAwait(false);
        provider.MappingsContainer = new CachedTypeMappingsProvider(mappings.Types);
        return mappings.Jmap is null ? null : new GameSettings { UEVersion = request.Game, ObjectDump = path, Jmap = mappings.Jmap };
    }

    private static async Task SubmitKeysAsync(StreamedFileProvider provider, string? aesKey)
    {
        if (provider.RequiredKeys.Count == 0) return;
        if (string.IsNullOrWhiteSpace(aesKey)) throw new ScanInputException("The archive is encrypted. An AES key is required.");

        var key = new FAesKey(aesKey);
        foreach (var guid in provider.RequiredKeys.ToArray()) await provider.SubmitKeyAsync(guid, key).ConfigureAwait(false);
        if (provider.RequiredKeys.Count > 0) throw new ScanInputException("The AES key does not match the archive.");
    }

    private static void CollectScriptImports(IPackage package, HashSet<string> imports, List<string> problems)
    {
        for (var i = 0; i < package.ImportMapLength; i++)
        {
            try
            {
                var resolved = package.ResolvePackageIndex(new FPackageIndex(package, -(i + 1)));
                while (resolved?.Outer is { } outer) resolved = outer;
                var name = resolved?.Name.Text;
                if (name is not null && name.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)) imports.Add(name);
            }
            catch (Exception e)
            {
                problems.Add($"{package.Name}: import {i} unresolved: {e.Message}");
                return;
            }
        }
    }

    private static IEnumerable<BlueprintAsset> LoadBlueprints(IPackage package, ArchiveFile file, GameSettings? graphSettings, List<string> problems)
    {
        var classes = new List<UBlueprintGeneratedClass>();
        var functions = new List<UFunction>();
        for (var i = 0; i < package.ExportsLazy.Length; i++)
        {
            try
            {
                switch (package.ExportsLazy[i].Value)
                {
                    case UBlueprintGeneratedClass bpClass:
                        classes.Add(bpClass);
                        break;
                    case UFunction function:
                        functions.Add(function);
                        break;
                }
            }
            catch (Exception e)
            {
                problems.Add($"{file.Path}: export {i} failed: {e.Message}");
            }
        }

        foreach (var bpClass in classes)
        {
            Asset? graphAsset = null;
            string? graphError = null;
            try
            {
                graphAsset = new Asset(package, file.GameFile.NameWithoutExtension);
                if (!graphAsset.IsBP) graphAsset = null;
            }
            catch (Exception e)
            {
                graphError = e.Message;
                problems.Add($"{file.Path}: blueprint graph setup failed: {e.Message}");
            }

            var defaultObject = TryLoadDefaultObject(bpClass, file, problems);
            var blueprint = new BlueprintAsset
            {
                PackagePath = file.Path,
                Package = package,
                Class = bpClass,
                DefaultObject = defaultObject,
                GraphAsset = graphAsset,
                GraphAssetError = graphError
            };

            var ubergraph = bpClass.UberGraphFunction?.ResolvedObject?.Name.Text;
            var owned = functions.Where(f => f.Outer?.Name == bpClass.Name).Distinct();
            foreach (var function in owned)
            {
                var isUbergraph = function.Name == ubergraph;
                var isEvent = graphAsset?.IsEvent(function) ?? false;
                blueprint.Functions.Add(new BlueprintFunction(blueprint, function, isUbergraph, isEvent, f => Decompile(f, graphSettings)));
            }
            yield return blueprint;
        }
    }

    private static CUE4Parse.UE4.Assets.Exports.UObject? TryLoadDefaultObject(UBlueprintGeneratedClass bpClass, ArchiveFile file,
        List<string> problems)
    {
        try
        {
            return bpClass.ClassDefaultObject.Load<CUE4Parse.UE4.Assets.Exports.UObject>();
        }
        catch (Exception e)
        {
            problems.Add($"{file.Path}: default object failed: {e.Message}");
            return null;
        }
    }

    private static DecompiledGraph? Decompile(BlueprintFunction function, GameSettings? settings)
    {
        if (settings is null || function.Owner.GraphAsset is not { } asset) return null;

        var target = function.Function;
        if (function.IsEvent && !function.IsUbergraph)
            return function.Owner.Functions.FirstOrDefault(f => f.IsUbergraph)?.GetGraph();
        if (function.IsUbergraph)
        {
            if (asset.SortedEvents.Count == 0) return null;
            target = asset.SortedEvents[0];
        }

        try
        {
            var decompiler = new FunctionDecompiler(asset, settings, target);
            decompiler.GlobalContext.IsParsingMacros = false;
            decompiler.GlobalContext.IsClearingTempVars = false;
            var result = decompiler.Decompile(null);
            return new DecompiledGraph(decompiler.Graph, result.Problems.Select(p => p.Message).ToList(), result.HasCriticalErrors);
        }
        catch (Exception e)
        {
            return new DecompiledGraph(new BPGraph(), [e.Message], true);
        }
    }
}
