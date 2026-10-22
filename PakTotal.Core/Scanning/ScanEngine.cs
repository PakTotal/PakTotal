using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakTotal.Core.Archives;
using PakTotal.Core.Modules;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Core.Scanning;

public sealed class ScanEngine(ArchiveLoader loader, ModuleCatalog catalog, IOptions<PakTotalOptions> options, ILogger<ScanEngine> logger)
{
    public async Task<ScanOutcome> RunAsync(ScanRequest request, CancellationToken cancellationToken)
    {
        using var loaded = await loader.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        var context = loaded.Context;

        logger.LogInformation("Scan {ScanId}: {Files} files, {Blueprints} blueprints, {Problems} load problems, graph support {Graph}",
            request.ScanId, context.Files.Count, context.Blueprints.Count, context.LoadProblems.Count, context.HasGraphSupport);
        foreach (var problem in context.LoadProblems) logger.LogDebug("Scan {ScanId} load problem: {Problem}", request.ScanId, problem);

        var outcomes = await Task.WhenAll(catalog.Modules.Select(m => catalog.IsApplicable(m, context)
            ? RunModuleAsync(m, context, cancellationToken)
            : Task.FromResult(new ModuleOutcome(m, new ModuleResult(), ModuleRunState.Skipped)))).ConfigureAwait(false);

        foreach (var outcome in outcomes)
        {
            logger.LogInformation("Scan {ScanId} module {Module}: coverage {Coverage}, {Count} findings",
                request.ScanId, outcome.ModuleId, outcome.Result.Coverage, outcome.Result.Findings.Count);
            foreach (var finding in outcome.Result.Findings)
            {
                logger.LogInformation("Scan {ScanId} module {Module} finding {Level} {Code} at {Asset}: {Detail}",
                    request.ScanId, outcome.ModuleId, finding.Level, finding.Code, finding.AssetPath, finding.Detail);
            }
            foreach (var note in outcome.Result.CoverageNotes)
                logger.LogInformation("Scan {ScanId} module {Module} coverage note: {Note}", request.ScanId, outcome.ModuleId, note);
        }

        return VerdictAggregator.Aggregate(outcomes, context.LoadProblems.Count);
    }

    private async Task<ModuleOutcome> RunModuleAsync(LoadedModule module, ScanContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.ModuleTimeout);
        try
        {
            var work = Task.Run(() => module.Instance.AnalyseAsync(context, timeout.Token), timeout.Token);
            var finished = await Task.WhenAny(work, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
            if (finished != work) return new ModuleOutcome(module, ModuleResult.Failed("timed out"));
            return new ModuleOutcome(module, await work.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModuleOutcome(module, ModuleResult.Failed("timed out"));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Scan {ScanId} module {Module} threw", context.ScanId, module.Id);
            return new ModuleOutcome(module, ModuleResult.Failed(e.Message));
        }
    }
}
