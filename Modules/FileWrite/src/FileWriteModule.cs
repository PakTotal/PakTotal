using PakTotal.Sdk.Analysis.FileWrites;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Modules.FileWrite;

public sealed class FileWriteModule : IScanModule
{
    private FileWriteSettings _settings = new();

    public void Initialize(ModuleManifest manifest)
    {
        _settings = manifest.GetSettings<FileWriteSettings>() ?? new FileWriteSettings();
    }

    public Task<ModuleResult> AnalyseAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = new ModuleResult();
        FileWriteAnalyzer.Analyse(context, _settings.Targets, _settings.Policy, result, cancellationToken);
        return Task.FromResult(result);
    }
}
