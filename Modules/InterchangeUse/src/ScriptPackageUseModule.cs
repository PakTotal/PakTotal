using PakTotal.Sdk.Analysis;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Modules.InterchangeUse;

public sealed class ScriptPackageUseSettings
{
    public List<string> Packages { get; init; } = [];
    public Severity Severity { get; init; } = Severity.Medium;
    public string Reason { get; init; } = "Calls a node from a flagged script package.";
}

public sealed class ScriptPackageUseModule : IScanModule
{
    private ScriptPackageUseSettings _settings = new();

    public void Initialize(ModuleManifest manifest)
    {
        _settings = manifest.GetSettings<ScriptPackageUseSettings>() ?? new ScriptPackageUseSettings();
    }

    public Task<ModuleResult> AnalyseAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = new ModuleResult();
        foreach (var asset in CallSite.FindAll(context)
                     .Where(s => _settings.Packages.Any(p => s.Target.PackageName?.StartsWith(p, StringComparison.OrdinalIgnoreCase) == true))
                     .GroupBy(s => s.Function.Owner.PackagePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = asset.First();
            var detail = $"{call.Target} at {call.Function} @{call.Statement.StatementIndex}";
            result.Suspicious(_settings.Severity, "ScriptPackageUse", _settings.Reason, detail, asset.Key);
        }
        return Task.FromResult(result);
    }
}
