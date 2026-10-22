using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Sdk.Analysis.FileWrites;

public static class FileWriteAnalyzer
{
    public static void Analyse(ScanContext context, IReadOnlyList<FileWriteTarget> targets, FileWritePolicy policy, ModuleResult result,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0) return;
        var collection = CallArgumentCollector.Collect(context, f => TargetFor(targets, f) is not null, cancellationToken);
        foreach (var note in collection.Notes) result.MarkPartial(note);
        foreach (var call in collection.Calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TargetFor(targets, call.Site.Target) is { } target) AnalyseCall(call, target, policy, result);
        }
    }

    private static FileWriteTarget? TargetFor(IReadOnlyList<FileWriteTarget> targets, FunctionRef function) =>
        targets.FirstOrDefault(t => function.Matches(t.Owner, t.Function));

    private static void AnalyseCall(CollectedCall call, FileWriteTarget target, FileWritePolicy policy, ModuleResult result)
    {
        var site = call.Site;
        var asset = site.Function.Owner.PackagePath;
        var location = $"{target.Function} at {site.Function} @{site.Statement.StatementIndex}";
        if (target.Rule == FileWriteRule.AnyUse)
        {
            Emit(result, policy.AnyUse, "FileWriteAnyUse", $"Uses {target.Label}, which can write a file to any path on disk.", location, asset);
            return;
        }

        var observations = call.Observations.Count > 0 ? call.Observations : [StaticArguments(call, target)];
        var issues = observations.Aggregate(FileWriteIssue.None, (all, o) => all | FilePathClassifier.Classify(target, o));
        var detail = $"{location}: {Describe(target, observations)}";

        if (issues.HasFlag(FileWriteIssue.NullCharacter))
            Emit(result, policy.NullCharacter, "FileWriteNullCharacter",
                $"Uses {target.Label} with a null character in the file name or path, which can cut off the file extension.", detail, asset);
        else if (issues.HasFlag(FileWriteIssue.Extension))
            Emit(result, policy.Extension, "FileWriteExtension",
                $"Uses {target.Label} to write a file that is not {string.Join(" or ", target.AllowedExtensions!)}.", detail, asset);
        else if (issues.HasFlag(FileWriteIssue.Escape))
            Emit(result, policy.Escape, "FileWriteEscape", $"Uses {target.Label} to write a file outside {Folder(target)}.", detail, asset);
        else if (issues.HasFlag(FileWriteIssue.Unresolved))
        {
            var input = target.Inputs().First().Input;
            var trace = PinTracer.DescribeCallInput(site, target.Owner, target.Function, input.Parameter);
            Emit(result, policy.Unresolved, "FileWriteUnresolved",
                $"Uses {target.Label} to write a file whose name or path could not be determined.", detail + trace, asset);
        }
    }

    private static IReadOnlyList<object?> StaticArguments(CollectedCall call, FileWriteTarget target)
    {
        var count = target.Inputs().Select(i => i.Input.Parameter + 1).DefaultIfEmpty(0).Max();
        return Enumerable.Range(0, count).Select(call.StaticLiteral).ToList();
    }

    private static string Describe(FileWriteTarget target, IReadOnlyList<IReadOnlyList<object?>> observations) =>
        string.Join(", ", observations
            .Select(o => string.Join(" | ", target.Inputs().Select(i => KismetValues.Truncate(KismetValues.Describe(FilePathClassifier.ValueOf(o, i.Input))))))
            .Distinct().Take(4));

    private static string Folder(FileWriteTarget target) => target.BaseFolder is { } folder ? $"the game's {folder} folder" : "the game's folders";

    private static void Emit(ModuleResult result, FindingRule rule, string code, string reason, string detail, string asset)
    {
        switch (rule.Level)
        {
            case FindingLevel.Malicious: result.Malicious(rule.Severity, code, reason, detail, asset); break;
            case FindingLevel.Suspicious: result.Suspicious(rule.Severity, code, reason, detail, asset); break;
            default: result.Notice(code, reason, detail, asset); break;
        }
    }
}
