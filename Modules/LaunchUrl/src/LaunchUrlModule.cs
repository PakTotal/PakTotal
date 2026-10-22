using PakTotal.Sdk.Analysis;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Modules.LaunchUrl;

public sealed class LaunchUrlModule : IScanModule
{
    private const string NameReason = "References LaunchURL by name, which can be used to call it indirectly.";

    private LaunchUrlSettings _settings = new();

    public void Initialize(ModuleManifest manifest)
    {
        _settings = manifest.GetSettings<LaunchUrlSettings>() ?? new LaunchUrlSettings();
    }

    public Task<ModuleResult> AnalyseAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = new ModuleResult();
        var collection = CallArgumentCollector.Collect(context, f => MatchTarget(f) is not null, cancellationToken);
        foreach (var note in collection.Notes) result.MarkPartial(note);

        foreach (var call in collection.Calls) AnalyseCall(call, MatchTarget(call.Site.Target)!, result);
        foreach (var blueprint in context.Blueprints)
        {
            var hasDirectCall = collection.Calls.Any(c => c.Site.Function.Owner == blueprint);
            CheckNameReferences(blueprint, hasDirectCall, result);
        }
        return Task.FromResult(result);
    }

    private LaunchTarget? MatchTarget(FunctionRef function) => _settings.Targets.Find(t => function.Matches(t.Owner, t.Function));

    private static void AnalyseCall(CollectedCall call, LaunchTarget target, ModuleResult result)
    {
        var site = call.Site;
        var values = call.ValuesOf(target.UrlParameter).ToList();
        if (values.Count == 0) values.Add(call.StaticLiteral(target.UrlParameter));

        var location = $"{site.Function} @{site.Statement.StatementIndex}";
        var asset = site.Function.Owner.PackagePath;
        var classifications = values.Select(v => (Value: v, Class: UrlClassifier.Classify(v))).ToList();
        if (classifications.All(c => c.Class == UrlClassifier.Unresolved))
        {
            var unresolved = UrlClassifier.Unresolved;
            var trace = PinTracer.DescribeCallInput(site, target.Owner, target.Function, target.UrlParameter);
            var detail = $"{target.Function} at {location} with unresolved URL{trace}";
            result.Add(new Finding(unresolved.Level, unresolved.Severity, unresolved.Code, unresolved.Reason, detail, asset));
            result.MarkPartial($"{location}: URL could not be reconstructed");
            return;
        }

        foreach (var group in classifications.GroupBy(c => c.Class))
        {
            var shown = string.Join(", ", group.Select(c => KismetValues.Describe(c.Value)).Distinct().Take(4).Select(v => KismetValues.Truncate(v)));
            var classification = group.Key;
            var detail = $"{target.Function} at {location}: {shown}";
            result.Add(new Finding(classification.Level, classification.Severity, classification.Code, classification.Reason, detail, asset));
        }
    }

    private void CheckNameReferences(BlueprintAsset blueprint, bool hasDirectCall, ModuleResult result)
    {
        foreach (var function in blueprint.Functions)
        {
            foreach (var statement in function.Bytecode)
            {
                foreach (var literal in KismetTree.StringLiterals(statement))
                {
                    var name = _settings.NameReferences.Find(n => literal.Contains(n, StringComparison.OrdinalIgnoreCase));
                    if (name is null) continue;
                    var detail = $"'{name}' referenced by name in {function} @{statement.StatementIndex}: \"{KismetValues.Truncate(literal)}\"";
                    result.Suspicious(Severity.Medium, "LaunchUrlByName", NameReason, detail, blueprint.PackagePath);
                }
            }
        }

        if (hasDirectCall) return;
        foreach (var entry in blueprint.Package.NameMap)
        {
            if (!_settings.NameReferences.Any(n => entry.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
            var detail = $"'{entry.Name}' is in the name table without a direct call";
            result.Suspicious(Severity.Medium, "LaunchUrlNameEntry", NameReason, detail, blueprint.PackagePath);
        }
    }
}
