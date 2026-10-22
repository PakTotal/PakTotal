using PakTotal.Core.Modules;
using PakTotal.Sdk.Modules;

namespace PakTotal.Core.Scanning;

public enum Verdict
{
    Undetected,
    Unprocessable,
    Suspicious,
    Malicious
}

public sealed record ReasonReport(string Text, Severity Severity);

public sealed record AssetReport(string Path, Verdict Verdict, Severity? Severity, IReadOnlyList<ReasonReport> Reasons);

public sealed record ModuleReport(string Name, bool IsPrivate, Verdict Verdict, Severity? Severity, bool Incomplete,
    IReadOnlyList<AssetReport> Assets);

public sealed record ScanOutcome(Verdict Verdict, Severity? Severity, bool Incomplete, IReadOnlyList<ModuleReport> Modules,
    bool HasFailedModules = false);

public enum ModuleRunState
{
    Ran,
    Skipped
}

public sealed record ModuleOutcome(LoadedModule Module, ModuleResult Result, ModuleRunState State = ModuleRunState.Ran)
{
    public string ModuleId => Module.Id;
}

public static class VerdictAggregator
{
    public static ScanOutcome Aggregate(IReadOnlyCollection<ModuleOutcome> outcomes, int loadProblemCount)
    {
        var reports = outcomes.Select(o => BuildReport(o, loadProblemCount > 0))
            .OrderBy(r => r.IsPrivate)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var detected = reports.Where(r => IsDetection(r.Verdict)).ToList();
        Verdict verdict;
        if (detected.Count > 0) verdict = detected.Max(r => r.Verdict);
        else if (loadProblemCount > 0 || reports.Any(r => r.Verdict == Verdict.Unprocessable)) verdict = Verdict.Unprocessable;
        else verdict = Verdict.Undetected;

        var severity = detected.Select(r => r.Severity).OfType<Severity>().DefaultIfEmpty(Severity.None).Max();
        var incomplete = loadProblemCount > 0 || reports.Any(r => r.Incomplete);
        var failed = outcomes.Any(o => o.Result.Coverage == Coverage.Failed);
        return new ScanOutcome(verdict, detected.Count > 0 ? severity : null, incomplete, reports, failed);
    }

    public static bool IsDetection(Verdict verdict) => verdict is Verdict.Suspicious or Verdict.Malicious;

    private static ModuleReport BuildReport(ModuleOutcome outcome, bool hasLoadProblems)
    {
        var manifest = outcome.Module.Manifest;
        var isPrivate = manifest.Visibility == ModuleVisibility.Private;
        var name = ModuleCodeNames.For(manifest);

        var assets = outcome.Result.Findings
            .Where(f => !isPrivate || f.Level != FindingLevel.Notice)
            .GroupBy(f => AssetPaths.ToDisplay(f.AssetPath))
            .Select(g => BuildAsset(g.Key, g.ToList(), isPrivate))
            .OrderByDescending(a => a.Verdict)
            .ThenByDescending(a => a.Severity)
            .ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var detected = assets.Where(a => IsDetection(a.Verdict)).ToList();
        var failed = outcome.Result.Coverage == Coverage.Failed;
        Verdict verdict;
        if (detected.Count > 0) verdict = detected.Max(a => a.Verdict);
        else if (hasLoadProblems || failed) verdict = Verdict.Unprocessable;
        else verdict = Verdict.Undetected;

        var severity = detected.Count == 0 ? (Severity?)null : detected.Max(a => a.Severity);
        var incomplete = outcome.State == ModuleRunState.Ran && outcome.Result.Coverage != Coverage.Complete;
        return new ModuleReport(name, isPrivate, verdict, severity, incomplete, assets);
    }

    private static AssetReport BuildAsset(string path, List<Finding> findings, bool isPrivate)
    {
        var verdict = findings.Max(f => f.Level) switch
        {
            FindingLevel.Malicious => Verdict.Malicious,
            FindingLevel.Suspicious => Verdict.Suspicious,
            _ => Verdict.Undetected
        };
        Severity? severity = verdict == Verdict.Undetected ? null : findings.Where(f => f.Level != FindingLevel.Notice).Max(f => f.Severity);
        if (isPrivate) return new AssetReport(path, verdict, severity, []);

        var reasons = findings
            .OrderByDescending(f => f.Level)
            .ThenByDescending(f => f.Severity)
            .GroupBy(f => f.Reason)
            .Select(g => new ReasonReport(g.Key, g.Max(f => f.Severity)))
            .ToList();
        return new AssetReport(path, verdict, severity, reasons);
    }
}

public static class AssetPaths
{
    private static readonly string[] PackageExtensions = [".uasset", ".umap"];

    public static string ToDisplay(string? packagePath)
    {
        if (string.IsNullOrEmpty(packagePath)) return "(archive)";
        var path = packagePath.Replace('\\', '/');
        var extension = PackageExtensions.FirstOrDefault(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (extension is null) return path;
        path = path[..^extension.Length];

        var parts = path.Split('/');
        var content = Array.FindIndex(parts, p => p.Equals("Content", StringComparison.OrdinalIgnoreCase));
        if (content < 0) return "/" + path.TrimStart('/');
        var mount = content >= 2 && parts[content - 2].Equals("Plugins", StringComparison.OrdinalIgnoreCase) ? parts[content - 1] : "Game";
        return $"/{mount}/{string.Join('/', parts.Skip(content + 1))}";
    }
}
