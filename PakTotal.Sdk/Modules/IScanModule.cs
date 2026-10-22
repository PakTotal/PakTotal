using PakTotal.Sdk.Context;

namespace PakTotal.Sdk.Modules;

public interface IScanModule
{
    void Initialize(ModuleManifest manifest) { }

    bool IsApplicable(ScanContext context) => true;

    Task<ModuleResult> AnalyseAsync(ScanContext context, CancellationToken cancellationToken);
}
